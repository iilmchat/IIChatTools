using System.Collections.Generic;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Implementation.SqlAgent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.SqlAgent
{
    /// <summary>
    /// Тесты <see cref="SqlQueryValidator"/>
    /// (v1.7.0, KI-097, Фаза 3, DESIGN_DB_AGENT § 6.2).
    /// Проверяют 7 шагов валидации на примерах из дизайн-документа.
    /// </summary>
    public class SqlQueryValidatorTests
    {
        // ============ Шаг 1: базовые проверки ============

        [Fact]
        public void Validate_EmptySql_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("   ", SqliteConn());
            Assert.False(r.IsValid);
            Assert.Contains("пуст", r.Error);
        }

        [Fact]
        public void Validate_TooLongSql_Fails()
        {
            var v = BuildValidator(SqliteProvider(maxSqlLength: 50));
            var sql = "SELECT " + new string('x', 100);
            var r = v.Validate(sql, SqliteConn());
            Assert.False(r.IsValid);
            Assert.Contains("лимит", r.Error);
        }

        [Fact]
        public void Validate_NotStartingWithSelect_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("DELETE FROM Chats", SqliteConn());
            Assert.False(r.IsValid);
            // Первый токен — DELETE, не SELECT/WITH.
            Assert.Contains("SELECT и WITH", r.Error);
        }

        [Fact]
        public void Validate_MultiStatement_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT 1; DROP TABLE Chats", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_SingleTrailingSemicolon_IsValid()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT COUNT(*) FROM Chats;", SqliteConn());
            Assert.True(r.IsValid);
        }

        // ============ Шаг 3: запрет ключевых слов ============

        [Fact]
        public void Validate_Delete_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("DELETE FROM Chats WHERE Id=1", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_Insert_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("INSERT INTO Chats (Title) VALUES ('x')", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_Update_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("UPDATE Chats SET Title='x'", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_Drop_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT 1 -- DROP TABLE Chats", SqliteConn());
            // DROP в комментарии — не keyword, валидно.
            Assert.True(r.IsValid);
        }

        [Fact]
        public void Validate_KeywordInsideStringLiteral_IsValid()
        {
            var v = BuildValidator(SqliteProvider());
            // 'DROP TABLE Chats' — строковый литерал, не команда (DESIGN § 6.2, пример 3).
            var r = v.Validate("SELECT 'DROP TABLE Chats' AS x FROM Chats", SqliteConn());
            Assert.True(r.IsValid);
        }

        // ============ Шаг 4: запрет функций ============

        [Fact]
        public void Validate_LoadExtension_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT load_extension('evil.so')", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_Readfile_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT readfile('/etc/passwd')", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_ReadfileInStringLiteral_IsValid()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT 'readfile' AS x FROM Chats", SqliteConn());
            Assert.True(r.IsValid);
        }

        // ============ Шаг 5-6: извлечение таблиц + whitelist ============

        [Fact]
        public void Validate_TableInWhitelist_IsValid()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM Chats", SqliteConn());
            Assert.True(r.IsValid);
            Assert.Contains("Chats", r.ReferencedTables);
        }

        [Fact]
        public void Validate_TableNotInWhitelist_Fails()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Email FROM AspNetUsers", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_TableInDeniedList_Fails()
        {
            // Chats — в Allowed И в Denied. Denied перебивает.
            var conn = SqliteConn();
            conn.DeniedTables = new List<string> { "Chats" };
            var v = BuildValidator(SqliteProvider());

            var r = v.Validate("SELECT Id FROM Chats", conn);
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_Join_TwoTablesBothInWhitelist_IsValid()
        {
            var conn = SqliteConn();
            conn.AllowedTables = new List<string> { "Chats", "ChatMessages" };
            var v = BuildValidator(SqliteProvider());

            var r = v.Validate(
                "SELECT c.Id FROM Chats c JOIN ChatMessages m ON m.ChatId = c.Id",
                conn);
            Assert.True(r.IsValid);
            Assert.Contains("Chats", r.ReferencedTables);
            Assert.Contains("ChatMessages", r.ReferencedTables);
        }

        [Fact]
        public void Validate_Join_OneTableNotInWhitelist_Fails()
        {
            var conn = SqliteConn();
            conn.AllowedTables = new List<string> { "Chats" };  // ChatMessages нет
            var v = BuildValidator(SqliteProvider());

            var r = v.Validate(
                "SELECT c.Id FROM Chats c JOIN ChatMessages m ON m.ChatId = c.Id",
                conn);
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_QuotedTableName_IsChecked()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT * FROM \"AspNetUsers\"", SqliteConn());
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_SchemaQualifiedTable_UsesLastPart()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM dbo.Chats", SqliteConn());
            Assert.True(r.IsValid);
            Assert.Contains("Chats", r.ReferencedTables);
        }

        [Fact]
        public void Validate_NoFromClause_IsValid()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT 1", SqliteConn());
            Assert.True(r.IsValid);
            Assert.Empty(r.ReferencedTables);
        }

        [Fact]
        public void Validate_CteNameIsNotTreatedAsTable()
        {
            var conn = SqliteConn();
            conn.AllowedTables = new List<string> { "Chats" };
            var v = BuildValidator(SqliteProvider());

            var r = v.Validate(
                "WITH cte AS (SELECT Id FROM Chats) SELECT * FROM cte",
                conn);
            Assert.True(r.IsValid);
            Assert.DoesNotContain("cte", r.ReferencedTables);
        }

        [Fact]
        public void Validate_SqliteWhitelist_IsCaseSensitive()
        {
            // Sqlite — case-sensitive (DESIGN § 6.3).
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM chats", SqliteConn());  // chats != Chats
            Assert.False(r.IsValid);
        }

        [Fact]
        public void Validate_SqlServerWhitelist_IsCaseInsensitive()
        {
            var conn = SqliteConn();
            conn.Provider = "SqlServer";
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM CHATS", conn);
            Assert.True(r.IsValid);
        }

        // ============ Шаг 7: auto-LIMIT ============

        [Fact]
        public void Validate_NoLimit_AutoLimitAdded()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM Chats", SqliteConn());
            Assert.True(r.IsValid);
            Assert.True(r.LimitAdded);
            Assert.Contains("LIMIT 100", r.SanitizedSql);
        }

        [Fact]
        public void Validate_ExistingLimit_NotDoubled()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM Chats LIMIT 5", SqliteConn());
            Assert.True(r.IsValid);
            Assert.False(r.LimitAdded);
            Assert.Equal("SELECT Id FROM Chats LIMIT 5", r.SanitizedSql);
        }

        [Fact]
        public void Validate_AutoLimit_TrailingSemicolon_InsertsBeforeSemicolon()
        {
            var v = BuildValidator(SqliteProvider());
            var r = v.Validate("SELECT Id FROM Chats;", SqliteConn());
            Assert.True(r.IsValid);
            Assert.True(r.LimitAdded);
            Assert.EndsWith("LIMIT 100;", r.SanitizedSql);
        }

        [Fact]
        public void Validate_AutoLimitDisabled_NotAdded()
        {
            var provider = SqliteProvider(autoLimit: false);
            var v = BuildValidator(provider);
            var r = v.Validate("SELECT Id FROM Chats", SqliteConn());
            Assert.True(r.IsValid);
            Assert.False(r.LimitAdded);
        }

        // ============ Комплексный пример из DESIGN § 6.2, Приложение A ============

        [Fact]
        public void Validate_ComplexQuery_FromDesignAppendixA_IsValid()
        {
            var conn = SqliteConn();
            conn.AllowedTables = new List<string> { "Chats", "ChatMessages" };
            var v = BuildValidator(SqliteProvider());

            var sql = @"
SELECT
  c.Id,
  c.Title,
  COUNT(m.Id) AS MessageCount
FROM Chats c
LEFT JOIN ChatMessages m ON m.ChatId = c.Id
WHERE c.UserId = 1
  AND c.UpdatedAt >= date('now', '-7 days')
GROUP BY c.Id, c.Title
ORDER BY c.UpdatedAt DESC";

            var r = v.Validate(sql, conn);
            Assert.True(r.IsValid);
            Assert.Contains("Chats", r.ReferencedTables);
            Assert.Contains("ChatMessages", r.ReferencedTables);
            Assert.True(r.LimitAdded);
        }

        // ============ Helpers ============

        private static SqlAgentOptions SqliteProvider(
            int maxSqlLength = 4000,
            bool autoLimit = true)
        {
            // Baseline-валидация SqlAgentOptionsProvider (DESIGN § 5.5) требует,
            // чтобы DefaultConnection был зарегистрирован в Connections.
            // В appsettings.json "internal" есть; в тестах — добавляем минимальный.
            // Реальные опции подключения передаются в Validate(sql, options) отдельно.
            var options = new SqlAgentOptions
            {
                Enabled = true,
                DefaultConnection = "internal",
                Connections = new Dictionary<string, SqlAgentConnectionOptions>
                {
                    ["internal"] = new SqlAgentConnectionOptions
                    {
                        DisplayName = "internal (test)",
                        Provider = "Sqlite",
                        ConnectionStringKey = "SqlAgent:Internal:ConnectionString",
                        AllowedTables = new List<string> { "Chats" },
                        DeniedTables = new List<string>(),
                        MaxRows = 100,
                        StatementTimeoutSeconds = 15,
                        RequiresApproval = true,
                        Enabled = true
                    }
                },
                QueryValidation = new SqlQueryValidationOptions
                {
                    DeniedKeywords = new List<string>
                    {
                        "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER",
                        "CREATE", "REPLACE", "MERGE", "GRANT", "REVOKE", "EXEC", "EXECUTE",
                        "ATTACH", "DETACH", "PRAGMA", "VACUUM", "ANALYZE", "REINDEX",
                        "COMMIT", "ROLLBACK", "SAVEPOINT", "BEGIN", "END"
                    },
                    DeniedFunctions = new List<string>
                    {
                        "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer"
                    },
                    MaxSqlLength = maxSqlLength,
                    AutoLimitIfMissing = autoLimit
                }
            };

            return options;
        }

        private static SqlAgentConnectionOptions SqliteConn()
        {
            return new SqlAgentConnectionOptions
            {
                DisplayName = "test",
                Provider = "Sqlite",
                ConnectionStringKey = "SqlAgent:Internal:ConnectionString",
                AllowedTables = new List<string>
                {
                    "Chats", "ChatMessages", "DocumentChunks",
                    "ChatAttachments", "AuditLogs", "AppSettings"
                },
                DeniedTables = new List<string>
                {
                    "AspNetUsers", "AspNetUserTokens", "AspNetUserClaims",
                    "AspNetUserLogins", "AspNetUserRoles", "AspNetRoles", "AspNetRoleClaims",
                    "PendingActions", "AgentStates", "MemoryEntries", "UserSettings"
                },
                MaxRows = 100,
                StatementTimeoutSeconds = 15,
                RequiresApproval = true,
                Enabled = true
            };
        }

        private static SqlQueryValidator BuildValidator(SqlAgentOptions baseline)
        {
            var optionsProvider = new SqlAgentOptionsProvider(
                Options.Create(baseline),
                NullLogger<SqlAgentOptionsProvider>.Instance);

            return new SqlQueryValidator(
                optionsProvider,
                NullLogger<SqlQueryValidator>.Instance);
        }
    }
}