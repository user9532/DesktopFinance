using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace FinanceApp.Data
{
    public class DatabaseManager
    {
        private readonly string _connectionString;

        public DatabaseManager()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbFolder = Path.Combine(localAppData, "DesktopFinance", "Database");
            Directory.CreateDirectory(dbFolder);
            DbPath = Path.Combine(dbFolder, "MyFinance.fdb");
            _connectionString = $"Data Source={DbPath}";
        }

        public string DbPath { get; }

        public void InitializeDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            CreateTables(conn);
            MigrateSchema(conn);
            SeedVersionRow(conn);
        }

        // Adds columns that were introduced after a database was first created.
        // CREATE TABLE IF NOT EXISTS does not retroactively add columns to an
        // existing table, so older database files need an explicit ALTER TABLE.
        private static void MigrateSchema(SqliteConnection conn)
        {
            EnsureColumn(conn, "LoanData", "AccountName", "TEXT");
            EnsureColumn(conn, "LoanData", "Institution",  "TEXT");
            EnsureColumn(conn, "LoanData", "StartDate",    "TEXT");
            EnsureColumn(conn, "LoanData", "Principal",    "REAL");
            EnsureColumn(conn, "LoanData", "Interest",     "REAL");
            EnsureColumn(conn, "LoanData", "Term",         "REAL");
            EnsureColumn(conn, "LoanData", "Escrow",       "REAL");
            EnsureColumn(conn, "LoanData", "Pmt",          "REAL");
            EnsureColumn(conn, "LoanData", "FirstPmnt",    "TEXT");
            EnsureColumn(conn, "LoanData", "CurrBalance",  "REAL");
            EnsureColumn(conn, "CatHistory", "Type",       "INTEGER");
            EnsureColumn(conn, "AccountChart", "Status",   "INTEGER");
        }

        private static void EnsureColumn(SqliteConnection conn, string table, string column, string type)
        {
            using var check = new SqliteCommand($"PRAGMA table_info({table});", conn);
            using (var r = check.ExecuteReader())
            {
                while (r.Read())
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return;
            }

            using var alter = new SqliteCommand($"ALTER TABLE {table} ADD COLUMN {column} {type};", conn);
            alter.ExecuteNonQuery();
        }
    
        public SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public bool AccountExists(string accountName)
        {
            using var conn = OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM AccountChart WHERE AccountName = @name;", conn);
            cmd.Parameters.AddWithValue("@name", accountName);
            return (long)cmd.ExecuteScalar()! > 0;
        }

        private static void CreateTables(SqliteConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS dbversion (
                    Major     INTEGER NOT NULL,
                    Minor     INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS AccountChart (
                    AccountName  TEXT,
                    AccountType  TEXT,
                    AccountCode  REAL,
                    Portfolio    TEXT,
                    AccountGroup TEXT,
                    Status       INTEGER
                );

                CREATE TABLE IF NOT EXISTS Transactions (
                    Account     TEXT,
                    Date        TEXT,
                    CheckNo     TEXT,
                    Type        TEXT,
                    Amount      REAL,
                    Name        TEXT,
                    Memo        TEXT,
                    Category    TEXT,
                    TrnType     TEXT,
                    Source      TEXT,
                    Clear       INTEGER,
                    FinancialId TEXT,
                    Action      TEXT,
                    Symbol      TEXT,
                    Quantity    REAL,
                    Price       REAL,
                    Commission  REAL
                );

                CREATE TABLE IF NOT EXISTS CatHistory (
                    Category    TEXT,
                    Description TEXT,
                    Amount      REAL,
                    Type        INTEGER
                );

                CREATE TABLE IF NOT EXISTS AccountMap (
                    AccountName  TEXT,
                    InternalName TEXT
                );

                CREATE TABLE IF NOT EXISTS Budget (
                    Date     TEXT,
                    Category TEXT,
                    Budget   REAL
                );

                CREATE TABLE IF NOT EXISTS RecurTrans (
                    Id         INTEGER,
                    Name       TEXT,
                    Amount     REAL,
                    Memo       TEXT,
                    Account    TEXT,
                    Category   TEXT,
                    Frequency  INTEGER,
                    NextDue    TEXT,
                    LastPosted TEXT,
                    TrType     INTEGER
                );

                CREATE TABLE IF NOT EXISTS LoanData (
                    AccountName TEXT,
                    Institution TEXT,
                    StartDate   TEXT,
                    Principal   REAL,
                    Interest    REAL,
                    Term        REAL,
                    Escrow      REAL,
                    Pmt         REAL,
                    FirstPmnt   TEXT,
                    CurrBalance REAL
                );

                CREATE TABLE IF NOT EXISTS Securities (
                    Symbol      TEXT,
                    Description TEXT,
                    IsSplit     INTEGER,
                    SplitDate   TEXT,
                    SplitRatio  REAL
                );

                CREATE TABLE IF NOT EXISTS Quotes (
                    Date   TEXT,
                    Symbol TEXT,
                    Price  REAL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_quotes_date_symbol ON Quotes (Date, Symbol);

                CREATE TABLE IF NOT EXISTS BackupSettings (
                    BackupOnClose INTEGER NOT NULL,
                    BackupFolder  TEXT
                );";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public (bool BackupOnClose, string? BackupFolder) GetBackupSettings()
        {
            using var conn = OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT BackupOnClose, BackupFolder FROM BackupSettings LIMIT 1;", conn);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return (r.GetInt64(0) != 0, r.IsDBNull(1) ? null : r.GetString(1));
            return (false, null);
        }

        public void SaveBackupSettings(bool backupOnClose, string? backupFolder)
        {
            using var conn = OpenConnection();
            using var delCmd = new SqliteCommand("DELETE FROM BackupSettings;", conn);
            delCmd.ExecuteNonQuery();
            using var insCmd = new SqliteCommand(
                "INSERT INTO BackupSettings (BackupOnClose, BackupFolder) VALUES (@on, @folder);", conn);
            insCmd.Parameters.AddWithValue("@on", backupOnClose ? 1 : 0);
            insCmd.Parameters.AddWithValue("@folder", (object?)backupFolder ?? DBNull.Value);
            insCmd.ExecuteNonQuery();
        }

        private static void SeedVersionRow(SqliteConnection conn)
        {
            using var countCmd = new SqliteCommand("SELECT COUNT(*) FROM dbversion;", conn);
            var count = (long)countCmd.ExecuteScalar()!;

            if (count == 0)
            {
                using var insertCmd = new SqliteCommand(
                    "INSERT INTO dbversion (Major, Minor) VALUES (1, 0);", conn);
                insertCmd.ExecuteNonQuery();
            }
        }
    }
}
