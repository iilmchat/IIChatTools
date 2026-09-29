using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.SqlAgent
{
    /// <summary>
    /// Инструмент Database Agent (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1 и § 7.5).
    /// Read-only SQL-доступ к БД приложения IIChatTools.
    ///
    /// <para>
    /// <b>Не наследует <c>AgentToolBase</c></b> — в отличие от 6 специализированных
    /// агентов (file_system, code, web, git, github, planner). Сделано намеренно
    /// (DESIGN § 4.1): агент не «думает», а детерминированно выполняет одну из
    /// 4 операций через <see cref="ISqlAgentService"/>.
    /// </para>
    ///
    /// <para>
    /// <b>4 действия:</b>
    /// <list type="bullet">
    ///   <item><description><c>list_databases</c> — список подключений;</description></item>
    ///   <item><description><c>list_tables</c> — whitelist-таблицы + row count;</description></item>
    ///   <item><description><c>describe_table</c> — колонки + типы + пример значения;</description></item>
    ///   <item><description><c>execute_query</c> — read-only SQL-запрос с валидацией.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Approval:</b> <see cref="RequiresApprovalByDefault"/> = <c>true</c>
    /// (все 4 действия требуют подтверждения пользователя).
    /// <para>
    /// <b>Отклонение от DESIGN § 3.3:</b> DESIGN описывает per-action approval
    /// (<c>execute_query</c> — да, метаданные — нет). Но <c>RequiresApprovalByDefault</c>
    /// в текущей архитектуре — <b>свойство всего tool</b>, не per-call. <c>ChatStreamService</c>
    /// не имеет механизма «решить по args». Поэтому выставлено <c>true</c> для всего агента.
    /// Per-action approval — задача KI-101, требует доработки <c>ChatStreamService</c>.
    /// </para>
    /// </para>
    /// </summary>
    public class DatabaseAgentTool : ITool
    {
        private const string ActionListDatabases = "list_databases";
        private const string ActionListTables = "list_tables";
        private const string ActionDescribeTable = "describe_table";
        private const string ActionExecuteQuery = "execute_query";

        private readonly ISqlAgentService _sqlAgentService;
        private readonly ILogger<DatabaseAgentTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="sqlAgentService">Оркестратор Database Agent</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public DatabaseAgentTool(
            ISqlAgentService sqlAgentService,
            ILogger<DatabaseAgentTool> logger)
        {
            _sqlAgentService = sqlAgentService
                ?? throw new ArgumentNullException(nameof(sqlAgentService));
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "database_agent";

        /// <inheritdoc />
        public string Description =>
            "Database Agent. Read-only доступ к БД приложения IIChatTools " +
            "(чаты, сообщения, аудит, чанки, вложения). 4 действия: " +
            "list_databases (список подключений), list_tables (whitelist-таблицы), " +
            "describe_table (колонки таблицы), execute_query (read-only SQL). " +
            "Используй для вопросов вида «сколько чатов у меня в БД», «какие таблицы доступны», " +
            "«покажи последние N сообщений». Запрещены INSERT/UPDATE/DELETE/DROP и " +
            "таблицы с PII (AspNetUsers). Все действия требуют подтверждения пользователя.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "action",
                Type = "string",
                Description =
                    "Действие. Одно из: " +
                    "list_databases (список подключений), " +
                    "list_tables (whitelist-таблицы подключения), " +
                    "describe_table (колонки таблицы), " +
                    "execute_query (read-only SQL: только SELECT или WITH).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "connection",
                Type = "string",
                Description =
                    "Имя подключения (например, internal). " +
                    "Необязательно: если не указано — используется подключение по умолчанию.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "table",
                Type = "string",
                Description = "Имя таблицы (для describe_table). Обязательно для этого действия.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "sql",
                Type = "string",
                Description =
                    "SQL-запрос для execute_query. Только SELECT или WITH (CTE). " +
                    "Без INSERT/UPDATE/DELETE/DROP/ALTER/CREATE. Без множественных операторов. " +
                    "Таблицы должны быть в whitelist подключения.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxRows",
                Type = "integer",
                Description =
                    "Максимум строк в результате execute_query (1–10000). " +
                    "По умолчанию — из настроек подключения.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            if (context == null)
                return ToolResult.Fail("Контекст выполнения не задан.");

            var action = (arguments?.GetString("action") ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return ToolResult.Fail(
                    "Не указан параметр 'action'. Допустимые значения: " +
                    "list_databases, list_tables, describe_table, execute_query.");

            try
            {
                switch (action)
                {
                    case ActionListDatabases:
                        return await HandleListDatabasesAsync(context);
                    case ActionListTables:
                        return await HandleListTablesAsync(context, arguments);
                    case ActionDescribeTable:
                        return await HandleDescribeTableAsync(context, arguments);
                    case ActionExecuteQuery:
                        return await HandleExecuteQueryAsync(context, arguments);
                    default:
                        return ToolResult.Fail(
                            $"Неизвестное действие '{action}'. " +
                            "Допустимые: list_databases, list_tables, describe_table, execute_query.");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Database Agent отменён клиентом (action={Action})", action);
                return ToolResult.Fail("Операция отменена.");
            }
            catch (ArgumentException ex)
            {
                // Ожидаемые ошибки: невалидный SQL, неизвестное подключение,
                // таблица не в whitelist, не найдено user-сообщение и т.п.
                _logger.LogInformation(
                    "Database Agent: некорректный запрос (action={Action}): {Message}",
                    action, ex.Message);
                return ToolResult.Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                // Подключение отключено, провайдер не поддерживается и т.п.
                _logger.LogWarning(ex,
                    "Database Agent: операция невозможна (action={Action})", action);
                return ToolResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                // Неожиданные ошибки (SqlException, timeout, ...).
                _logger.LogError(ex,
                    "Database Agent: неожиданная ошибка (action={Action})", action);
                return ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
            }
        }

        // ============ Обработчики действий ============

        /// <summary>
        /// <c>list_databases</c> — список зарегистрированных подключений.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <returns>Результат с массивом <see cref="DatabaseConnectionInfoDto"/></returns>
        private async Task<ToolResult> HandleListDatabasesAsync(ToolExecutionContext context)
        {
            var connections = await _sqlAgentService.ListConnectionsAsync(
                context.CancellationToken);

            if (connections.Count == 0)
                return ToolResult.Ok(
                    Array.Empty<object>(),
                    "Не зарегистрировано ни одного подключения. " +
                    "Проверь секцию SqlAgent:Connections в appsettings.json.");

            return ToolResult.Ok(
                connections,
                $"Зарегистрировано подключений: {connections.Count}.");
        }

        /// <summary>
        /// <c>list_tables</c> — whitelist-таблицы + row count.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы (connection — optional)</param>
        /// <returns>Результат с массивом <see cref="SqlTableInfoDto"/></returns>
        private async Task<ToolResult> HandleListTablesAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var connection = arguments?.GetString("connection");

            var tables = await _sqlAgentService.ListTablesAsync(
                connection, context.CancellationToken);

            if (tables.Count == 0)
                return ToolResult.Ok(
                    Array.Empty<object>(),
                    $"Для подключения '{connection ?? "<default>"}' нет доступных таблиц " +
                    "(AllowedTables пуст или все в DeniedTables).");

            return ToolResult.Ok(
                tables,
                $"Доступно таблиц: {tables.Count}.");
        }

        /// <summary>
        /// <c>describe_table</c> — колонки таблицы + типы + пример значения.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы (connection, table)</param>
        /// <returns>Результат с массивом <see cref="SqlColumnInfoDto"/></returns>
        private async Task<ToolResult> HandleDescribeTableAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var connection = arguments?.GetString("connection");
            var table = arguments?.GetString("table");

            if (string.IsNullOrWhiteSpace(table))
                return ToolResult.Fail(
                    "Для действия describe_table обязательно укажи параметр 'table' " +
                    "(имя таблицы из whitelist).");

            var columns = await _sqlAgentService.DescribeTableAsync(
                connection, table, context.CancellationToken);

            if (columns.Count == 0)
                return ToolResult.Ok(
                    Array.Empty<object>(),
                    $"Таблица '{table}' не содержит колонок (или недоступна).");

            return ToolResult.Ok(
                columns,
                $"Таблица '{table}': {columns.Count} колонок.");
        }

        /// <summary>
        /// <c>execute_query</c> — валидирует и выполняет read-only SQL-запрос.
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="arguments">Аргументы (connection, sql, maxRows)</param>
        /// <returns>Результат с <see cref="SqlQueryResultDto"/></returns>
        private async Task<ToolResult> HandleExecuteQueryAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var sql = arguments?.GetString("sql");
            if (string.IsNullOrWhiteSpace(sql))
                return ToolResult.Fail(
                    "Для действия execute_query обязательно укажи параметр 'sql' " +
                    "(только SELECT или WITH).");

            var connection = arguments?.GetString("connection");
            var maxRows = arguments != null ? arguments.GetInt("maxRows", 0) : 0;

            var request = new SqlQueryRequest
            {
                Connection = connection,
                Sql = sql,
                MaxRows = maxRows > 0 ? maxRows : (int?)null
            };

            var result = await _sqlAgentService.ExecuteQueryAsync(
                request, context.CancellationToken);

            var message = result.Truncated
                ? $"Возвращено {result.RowCount} строк (обрезано по лимиту) за {result.DurationMs} мс."
                : $"Возвращено {result.RowCount} строк за {result.DurationMs} мс.";

            return ToolResult.Ok(result, message);
        }
    }
}