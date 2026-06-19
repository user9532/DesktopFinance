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
            var appFolder = AppDomain.CurrentDomain.BaseDirectory;
            var dbFolder = Path.Combine(appFolder, "Database");
            Directory.CreateDirectory(dbFolder);
            DbPath = Path.Combine(dbFolder, "MyFinance.fdb");
            _connectionString = $"Data Source={DbPath}";
        }

        public string DbPath { get; }

        public void InitializeDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            RunMigrations(conn);
            CreateTables(conn);
            SeedVersionRow(conn);
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

        // Runs one-time schema changes for databases created under earlier versions.
        private static void RunMigrations(SqliteConnection conn)
        {
            // Rename RecurPayment -> RecurTrans if the old table still exists.
            using var checkCmd = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RecurPayment';",
                conn);
            var exists = (long)checkCmd.ExecuteScalar()! > 0;

            if (exists)
            {
                using var renameCmd = new SqliteCommand(
                    "ALTER TABLE RecurPayment RENAME TO RecurTrans;", conn);
                renameCmd.ExecuteNonQuery();
            }

            // Add split columns to Securities if the table and columns don't exist yet.
            using var secCheck = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Securities';", conn);
            if ((long)secCheck.ExecuteScalar()! > 0)
            {
                AddColumnIfMissing(conn, "Securities", "IsSplit",    "INTEGER");
                AddColumnIfMissing(conn, "Securities", "SplitDate",  "TEXT");
                AddColumnIfMissing(conn, "Securities", "SplitRatio", "REAL");
            }

            // Unique index on Quotes(Date, Symbol) — only if Quotes table already exists.
            using var quotesCheck = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Quotes';", conn);
            if ((long)quotesCheck.ExecuteScalar()! > 0)
            {
                using var idxCmd = new SqliteCommand(
                    "CREATE UNIQUE INDEX IF NOT EXISTS ux_quotes_date_symbol ON Quotes (Date, Symbol);",
                    conn);
                idxCmd.ExecuteNonQuery();
            }
        }

        private static void AddColumnIfMissing(SqliteConnection conn,
            string table, string column, string type)
        {
            using var infoCmd = new SqliteCommand(
                $"PRAGMA table_info({table});", conn);
            using var r = infoCmd.ExecuteReader();
            while (r.Read())
                if (r.GetString(1) == column) return;

            using var alterCmd = new SqliteCommand(
                $"ALTER TABLE {table} ADD COLUMN {column} {type};", conn);
            alterCmd.ExecuteNonQuery();
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
                    AccountGroup TEXT
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
                    Amount      REAL
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
                    Principle   REAL,
                    Interest    REAL,
                    Term        REAL,
                    Escrow      REAL,
                    Pmt         REAL
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
                );";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
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
