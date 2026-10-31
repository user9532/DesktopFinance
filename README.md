# DesktopFinance

A Windows desktop personal finance manager built with WPF (.NET 8) and SQLite. DesktopFinance lets you track bank, credit card, investment, and loan accounts in one local, offline application — no cloud account, subscription, or data upload required.

## Features

- **Account tracking** — Bank, credit card, other asset/liability, investment, and loan accounts, each with its own statement view.
- **Transaction entry** — Manual entry with inline editing, double-entry (Account + Category) accounting, and split transactions.
- **QFX/OFX import** — Import transactions directly from your bank or credit card's downloadable QFX/OFX files, with automatic institution-to-account mapping.
- **Recurring transactions** — Schedule recurring payments and deposits, including loan payments that automatically drop off once a loan is paid off.
- **Investments** — Record trades, track securities and quotes, handle stock splits, and view portfolio and investment performance.
- **Loans** — Track loan details (principal, interest, term, escrow) and remaining balance.
- **Budgeting** — Enter budgets by category and compare against actuals.
- **Reports** — Income and Expenses, Budget vs Actual, Net Worth Statement, Cash Flow Statement, Investment Performance, and category drilldown.
- **Backup & restore** — Configurable automatic backup on close, with manual restore from a backup file.
- **Autocomplete** — Name-column suggestions based on prior transaction history (`CatHistory`).

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build/run from source)

## Build & Run

```powershell
dotnet build
dotnet run
```

The project targets `net8.0-windows` and produces a WinExe (WPF application).

## Installer

An installer package is available under [Installer/](Installer/), built with the WiX Toolset:

```powershell
cd Installer
./build-installer.ps1
```

See [Installer/License.rtf](Installer/License.rtf) for the end-user license agreement shown during installation.

## Data Storage

DesktopFinance stores its SQLite database locally at:

```
%LOCALAPPDATA%\DesktopFinance\Database\MyFinance.fdb
```

All data stays on your machine. Use the **File → Backup** and **File → Restore Backup** menu options to configure and manage backups.

## Architecture

DesktopFinance follows a lightweight MVVM pattern without an MVVM framework:

| Layer | Description |
|-------|-------------|
| `MainWindow.xaml.cs` | Application shell — owns the tab control, menu commands, and tab lifecycle. |
| `Views/` | Statement views (Account, Credit Card, Other Account/Liability, Investment, Loan) and row-level view models. |
| `Data/DatabaseManager.cs` | SQLite connection string and schema initialization/migration. |
| `Data/QfxImporter.cs` | OFX/QFX file parsing and bulk transaction import. |
| `Dialogs/` | Modal dialogs for CRUD on accounts, categories, budgets, loans, investments, and splits. |

### Database Schema (SQLite)

| Table | Purpose |
|-------|---------|
| `AccountChart` | Account catalog with type and grouping |
| `Transactions` | Double-entry rows (Account + Category columns) |
| `CatHistory` | Category/name autocomplete history |
| `Budget` | Budget entries by category |
| `RecurTrans` | Recurring transactions |
| `LoanData` | Loan-specific account details |
| `AccountMap` | Maps QFX institution (ORG:FID) → account name |
| `Securities` | Investment securities and stock split info |
| `Quotes` | Security price quotes by date |
| `BackupSettings` | Backup-on-close configuration |

For more detail, see [CLAUDE.md](CLAUDE.md).

## Documentation

- [Documentation/Introducing.html](Documentation/Introducing.html) — feature overview
- [Documentation/UserManual.html](Documentation/UserManual.html) — full user manual
- [Documentation/Screenshots.html](Documentation/Screenshots.html) — screenshots

## License

See [Installer/License.rtf](Installer/License.rtf). The software is provided "as is," without warranty, and the license excludes the developer from liability arising from its use. This is not a substitute for legal advice.
