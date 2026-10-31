# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
dotnet build
dotnet run
```

The project targets `net8.0-windows` and produces a WinExe (WPF application). There are no automated tests.

## Architecture

**DesktopFinance** is a WPF personal finance manager using SQLite for persistence. It follows a lightweight MVVM pattern without an MVVM framework.

### Layers

- **`MainWindow.xaml.cs`** — Application shell. Owns `MainTabControl`; opens and closes statement/report/view tabs dynamically (one permanent `HomeTab`). Handles all top-level menu commands: File (Import/Export, Backup/Restore, New Database, Exit), Accounts, Category, Investment, View, Reports, and Help. Also owns `RefreshBankStatementTab(string account)`, the central mechanism for pushing updates (e.g. a posted recurring transaction) into any already-open statement tab for that account.
- **`Views/AccountStatementView`** (and its Credit Card / Other Account / Other Liability / Investment / Loan counterparts) — The main data entry surfaces. Each displays transactions for one account in a `DataGrid`, with inline editing via a dirty-flag pattern (`IsDirty` per row, `HasUnsavedChanges` on the view) that gates tab close.
- **`Views/TransactionRowViewModel.cs` / `SplitLineViewModel.cs`** — Row-level view models implementing `INotifyPropertyChanged`. `TransactionRowViewModel` represents a double-sided entry (Account + Category columns).
- **`Data/DatabaseManager.cs`** — Instance class (constructed with `new DatabaseManager()`). Resolves the SQLite file path under `%LOCALAPPDATA%\DesktopFinance\Database\MyFinance.fdb` (exposed as `DbPath`) and creates all tables via `CREATE TABLE IF NOT EXISTS` on first run (`InitializeDatabase()`). There is no schema-migration step — all databases are created fresh from the current schema, so adding a column means adding it directly to the `CREATE TABLE` statement rather than an `ALTER TABLE` migration. Other classes get a connection via `OpenConnection()`; there is no repository abstraction or DI container.
- **`Data/QfxImporter.cs`** — Parses OFX/QFX files: reads ORG/FID to identify institution, maps to an account via `AccountMap` table, then bulk-inserts transactions.
- **`Dialogs/`** — Modal dialogs (`ShowDialog()`) for CRUD on accounts, categories, budgets, loans, investments/trades, and split transactions. On Save, a dialog must set `DialogResult = true` to close itself — several past bugs were dialogs that cleared their fields and showed a confirmation `MessageBox` instead of setting `DialogResult`, leaving the dialog open.

### Database Schema (SQLite)

| Table | Purpose |
|-------|---------|
| `AccountChart` | Account catalog with type and grouping |
| `Transactions` | Double-entry rows (Account + Category columns), including investment fields (Action, Symbol, Quantity, Price, Commission) |
| `CatHistory` | Category/Name autocomplete history (`Type`: 1=Payment, 2=Deposit) |
| `Budget` | Budget entries by category |
| `RecurTrans` | Recurring transactions (`TrType` 3 = loan payment) |
| `LoanData` | Loan-specific account details, including `CurrBalance` |
| `AccountMap` | Maps QFX institution (ORG:FID) → account name |
| `Securities` | Investment securities, including stock-split info |
| `Quotes` | Security price quotes by date (unique on Date+Symbol) |
| `BackupSettings` | Backup-on-close preference and target folder |

### Key Patterns

- **Dirty-flag tracking:** `TransactionRowViewModel.IsDirty` flags individual rows; `AccountStatementView.HasUnsavedChanges` (and its sibling views) gate tab close.
- **Parameterized SQL:** All queries use `SqliteParameter` (via `Microsoft.Data.Sqlite`) — maintain this pattern.
- **No DI container:** Dependencies are passed via constructor arguments or by holding a `DatabaseManager` instance.
- **Tab lifecycle:** Opening an account creates a new tab; closing checks `HasUnsavedChanges` and prompts before discarding. `File → New Database` and `File → Restore Backup` must close *every* non-Home tab (not just account statements), since any tab type can be left pointing at stale data.
- **Split transactions:** A single transaction can fan out to multiple `SplitLineViewModel` entries managed through `SplitTransactionDialog`.
- **Backup on close:** `MainWindow_Closing` always runs the configured backup in a `finally` block, even if saving unsaved tabs throws, so a backup failure elsewhere can't silently skip the file copy.
