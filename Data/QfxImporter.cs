using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FinanceApp.Data
{
    public class QfxTransaction
    {
        public string  TrnType    { get; set; } = "";   // <TRNTYPE> → Transactions.Type
        public string  DtPosted   { get; set; } = "";   // parsed to yyyy-MM-dd
        public decimal TrnAmt     { get; set; }          // original (may be negative)
        public string  FitId      { get; set; } = "";
        public string  Name       { get; set; } = "";
        public string  Memo       { get; set; } = "";
    }

    public class QfxImporter
    {
        private readonly DatabaseManager _db;

        public QfxImporter(DatabaseManager db) => _db = db;

        // ── ORG / FID / ACCTID ──────────────────────────────────────────────

        public (string org, string fid, string acctIdLast4) ReadOrgFid(string filePath)
        {
            var content = File.ReadAllText(filePath);
            var acctId  = GetTagValue(content, "ACCTID");
            var last4   = acctId.Length > 4 ? acctId[^4..] : acctId;
            return (GetTagValue(content, "ORG"), GetTagValue(content, "FID"), last4);
        }

        // ── AccountMap lookup / save ──────────────────────────────────────

        public string? LookupAccountMap(string orgFid)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountMap WHERE InternalName = @key;", conn);
            cmd.Parameters.AddWithValue("@key", orgFid);
            return cmd.ExecuteScalar() as string;
        }

        public void SaveAccountMap(string accountName, string orgFid)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "INSERT INTO AccountMap (AccountName, InternalName) VALUES (@name, @key);", conn);
            cmd.Parameters.AddWithValue("@name", accountName);
            cmd.Parameters.AddWithValue("@key",  orgFid);
            cmd.ExecuteNonQuery();
        }

        // ── transaction parse ─────────────────────────────────────────────

        public List<QfxTransaction> ReadTransactions(string filePath)
        {
            var content = File.ReadAllText(filePath);
            var list    = new List<QfxTransaction>();

            foreach (var block in GetTransactionBlocks(content))
            {
                var amtStr = GetTagValue(block, "TRNAMT");
                if (!decimal.TryParse(amtStr, NumberStyles.Any,
                                      CultureInfo.InvariantCulture, out var amt))
                    continue;

                list.Add(new QfxTransaction
                {
                    TrnType  = GetTagValue(block, "TRNTYPE"),
                    DtPosted = ParseQfxDate(GetTagValue(block, "DTPOSTED")),
                    TrnAmt   = amt,
                    FitId    = GetTagValue(block, "FITID"),
                    Name     = GetTagValue(block, "NAME"),
                    Memo     = GetTagValue(block, "MEMO")
                });
            }

            return list;
        }

        // ── import into DB ────────────────────────────────────────────────

        public void ImportTransactions(string accountName, List<QfxTransaction> transactions)
        {
            using var conn = _db.OpenConnection();

            foreach (var t in transactions)
            {
                // CREDIT if TRNAMT negative, DEBIT if positive
                var trnType   = t.TrnAmt < 0 ? "CREDIT" : "DEBIT";
                var absAmount = (double)Math.Abs(t.TrnAmt);

                // Clean memo/name for storage
                var cleanMemo = CleanText(t.Memo);
                var cleanName = CleanText(t.Name);

                // Look up category from CatHistory, keyed on Description (matches NAME)
                var category = LookupCatHistory(conn, cleanName);

                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, Type, Amount, FinancialId, Name,
                         Memo, Category, TrnType, Clear)
                    VALUES
                        (@acct,@date,@type,@amt,@fid,@name,
                         @memo,@cat,@trntype,0);", conn);

                cmd.Parameters.AddWithValue("@acct",    accountName);
                cmd.Parameters.AddWithValue("@date",    t.DtPosted);
                cmd.Parameters.AddWithValue("@type",    t.TrnType);
                cmd.Parameters.AddWithValue("@amt",     absAmount);
                cmd.Parameters.AddWithValue("@fid",     t.FitId);
                cmd.Parameters.AddWithValue("@name",    cleanName);
                cmd.Parameters.AddWithValue("@memo",    cleanMemo);
                cmd.Parameters.AddWithValue("@cat",     category);
                cmd.Parameters.AddWithValue("@trntype", trnType);
                cmd.ExecuteNonQuery();
            }
        }

        private static string LookupCatHistory(SqliteConnection conn, string memo)
        {
            if (string.IsNullOrWhiteSpace(memo)) return "";
            using var cmd = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description = @d;", conn);
            cmd.Parameters.AddWithValue("@d", memo);
            return cmd.ExecuteScalar() as string ?? "";
        }

        // ── QFX parsing helpers ───────────────────────────────────────────

        private static List<string> GetTransactionBlocks(string content)
        {
            var blocks = new List<string>();

            // Handles both XML format (</STMTTRN> present) and SGML (no closing tag)
            var xmlMatches = Regex.Matches(content,
                @"<STMTTRN>(.*?)</STMTTRN>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (xmlMatches.Count > 0)
            {
                foreach (Match m in xmlMatches)
                    blocks.Add(m.Groups[1].Value);
                return blocks;
            }

            // SGML fallback: split on <STMTTRN>, stop at next block-level tag
            var parts = Regex.Split(content, @"<STMTTRN>", RegexOptions.IgnoreCase);
            foreach (var part in parts.Skip(1))
            {
                var end = Regex.Match(part,
                    @"</?(?:STMTTRN|BANKTRANLIST|STMTTRNRS|INVTRANLIST)>",
                    RegexOptions.IgnoreCase);
                blocks.Add(end.Success ? part[..end.Index] : part);
            }

            return blocks;
        }

        // Extracts a tag value from XML (<TAG>val</TAG>) or SGML (<TAG>val\n) content.
        private static string GetTagValue(string content, string tag)
        {
            var xml = Regex.Match(content,
                $@"<{tag}>\s*([^<]*?)\s*</{tag}>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (xml.Success) return xml.Groups[1].Value.Trim();

            var sgml = Regex.Match(content,
                $@"<{tag}>\s*([^\r\n<]+)",
                RegexOptions.IgnoreCase);
            return sgml.Success ? sgml.Groups[1].Value.Trim() : "";
        }

        // Converts QFX date strings (YYYYMMDD, YYYYMMDDHHMMSS, etc.) to yyyy-MM-dd.
        private static string ParseQfxDate(string dt)
        {
            if (string.IsNullOrWhiteSpace(dt)) return "";
            var digits = new string(dt.Where(char.IsDigit).Take(8).ToArray());
            if (digits.Length < 8) return dt;
            return $"{digits[..4]}-{digits[4..6]}-{digits[6..8]}";
        }

        private static string CleanText(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : Regex.Replace(s.Trim(), @"\s+", " ");
    }
}
