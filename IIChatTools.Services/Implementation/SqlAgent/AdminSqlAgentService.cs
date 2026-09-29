using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Административный сервис управления подключениями Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 7.6).
    /// <para>
    /// <b>Lifecycle:</b> Scoped (работает с <c>AppDbContext</c> через
    /// <see cref="IAppSettingsService"/> + <see cref="IAuditService"/>).
    /// </para>
    /// <para>
    /// <b>Persist:</b> override'ы хранятся в <c>AppSettings</c> отдельными
    /// ключами (не одним JSON'ом, как у агентов — см. § 5.4):
    /// <list type="bullet">
    ///   <item><c>SqlAgent.{name}.enabled</c> — <c>bool</c>;</item>
    ///   <item><c>SqlAgent.{name}.allowedTables</c> — JSON-массив;</item>
    ///   <item><c>SqlAgent.{name}.deniedTables</c> — JSON-массив;</item>
    ///   <item><c>SqlAgent.{name}.maxRows</c> — <c>int</c>;</item>
    ///   <item><c>SqlAgent.{name}.statementTimeoutSeconds</c> — <c>int</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class AdminSqlAgentService : IAdminSqlAgentService
    {
        /// <summary>Префикс ключей в AppSettings для override'ов подключений.</summary>
        private const string SettingKeyPrefix = "SqlAgent.";

        /// <summary>Категория в AppSettings для override'ов подключений.</summary>
        private const string SettingCategory = "SQL Agent";

        /// <summary>Поле: enabled.</summary>
        private const string FieldEnabled = "enabled";

        /// <summary>Поле: allowedTables.</summary>
        private const string FieldAllowedTables = "allowedTables";

        /// <summary>Поле: deniedTables.</summary>
        private const string FieldDeniedTables = "deniedTables";

        /// <summary>Поле: maxRows.</summary>
        private const string FieldMaxRows = "maxRows";

        /// <summary>Поле: statementTimeoutSeconds.</summary>
        private const string FieldStatementTimeoutSeconds = "statementTimeoutSeconds";

        private const int MinMaxRows = 1;
        private const int MaxMaxRows = 10_000;
        private const int MinTimeoutSeconds = 1;
        private const int MaxTimeoutSeconds = 300;

        private readonly SqlAgentOptionsProvider _optionsProvider;
        private readonly ISqlConnectionProvider _connectionProvider;
        private readonly IAppSettingsService _settingsService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminSqlAgentService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="optionsProvider">Провайдер актуальных опций SqlAgent</param>
        /// <param name="connectionProvider">Фабрика соединений (для <c>test</c>)</param>
        /// <param name="settingsService">Сервис настроек (persist override'ов)</param>
        /// <param name="auditService">Сервис аудита</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public AdminSqlAgentService(
            SqlAgentOptionsProvider optionsProvider,
            ISqlConnectionProvider connectionProvider,
            IAppSettingsService settingsService,
            IAuditService auditService,
            ILogger<AdminSqlAgentService> logger)
        {
            _optionsProvider = optionsProvider
                ?? throw new ArgumentNullException(nameof(optionsProvider));
            _connectionProvider = connectionProvider
                ?? throw new ArgumentNullException(nameof(connectionProvider));
            _settingsService = settingsService
                ?? throw new ArgumentNullException(nameof(settingsService));
            _auditService = auditService
                ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<SqlAgentConnectionItemDto>> GetAllConnectionsAsync(
            CancellationToken cancellationToken = default)
        {
            var allSettings = await _settingsService.GetAllAsync();
            var overrideNames = GetOverrideConnectionNames(allSettings);

            var names = _optionsProvider.GetAllConnectionNames();
            var result = new List<SqlAgentConnectionItemDto>(names.Count);

            foreach (var name in names)
            {
                var opts = _optionsProvider.Get(name);
                if (opts == null) continue;

                result.Add(ToDto(name, opts, overrideNames.Contains(name)));
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<SqlAgentConnectionItemDto> UpdateConnectionAsync(
            string name,
            UpdateSqlAgentConnectionRequest request,
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя подключения обязательно.", nameof(name));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var current = _optionsProvider.Get(name)
                ?? throw new ArgumentException(
                    $"Подключение '{name}' не зарегистрировано.", nameof(name));

            // --- Валидация входных значений (clamp + ArgumentException вне диапазона) ---
            if (request.MaxRows.HasValue &&
                (request.MaxRows.Value < MinMaxRows || request.MaxRows.Value > MaxMaxRows))
            {
                throw new ArgumentException(
                    $"MaxRows должен быть в диапазоне {MinMaxRows}–{MaxMaxRows}.",
                    nameof(request));
            }

            if (request.StatementTimeoutSeconds.HasValue &&
                (request.StatementTimeoutSeconds.Value < MinTimeoutSeconds ||
                 request.StatementTimeoutSeconds.Value > MaxTimeoutSeconds))
            {
                throw new ArgumentException(
                    $"StatementTimeoutSeconds должен быть в диапазоне " +
                    $"{MinTimeoutSeconds}–{MaxTimeoutSeconds}.",
                    nameof(request));
            }

            // --- Применяем изменения к текущим опциям ---
            if (request.Enabled.HasValue)
                current.Enabled = request.Enabled.Value;

            if (request.AllowedTables != null)
                current.AllowedTables = request.AllowedTables.ToList();

            if (request.DeniedTables != null)
                current.DeniedTables = request.DeniedTables.ToList();

            if (request.MaxRows.HasValue)
                current.MaxRows = request.MaxRows.Value;

            if (request.StatementTimeoutSeconds.HasValue)
                current.StatementTimeoutSeconds = request.StatementTimeoutSeconds.Value;

            // --- Persist в AppSettings: только переданные поля ---
            var allSettings = await _settingsService.GetAllAsync();

            if (request.Enabled.HasValue)
                await UpsertSettingAsync(allSettings, name, FieldEnabled,
                    current.Enabled.ToString().ToLowerInvariant(), "bool");

            if (request.AllowedTables != null)
                await UpsertSettingAsync(allSettings, name, FieldAllowedTables,
                    JsonConvert.SerializeObject(current.AllowedTables), "json");

            if (request.DeniedTables != null)
                await UpsertSettingAsync(allSettings, name, FieldDeniedTables,
                    JsonConvert.SerializeObject(current.DeniedTables), "json");

            if (request.MaxRows.HasValue)
                await UpsertSettingAsync(allSettings, name, FieldMaxRows,
                    current.MaxRows.ToString(), "int");

            if (request.StatementTimeoutSeconds.HasValue)
                await UpsertSettingAsync(allSettings, name, FieldStatementTimeoutSeconds,
                    current.StatementTimeoutSeconds.ToString(), "int");

            // --- Применяем к провайдеру in-memory (без рестарта) ---
            _optionsProvider.UpdateConnection(name, current);

            // --- Аудит ---
            await LogAdminActionAsync(userId, "admin.sqlagent.connection.update", name);

            _logger.LogInformation(
                "SqlAgent: обновлено подключение '{Name}' администратором {UserId}",
                name, userId);

            return ToDto(name, current, isOverridden: true);
        }

        /// <inheritdoc/>
        public async Task<SqlAgentTestResultDto> TestConnectionAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(name))
            {
                sw.Stop();
                return new SqlAgentTestResultDto
                {
                    Success = false,
                    Message = "Имя подключения обязательно.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            try
            {
                if (!_optionsProvider.GetAllConnectionNames()
                        .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    sw.Stop();
                    return new SqlAgentTestResultDto
                    {
                        Success = false,
                        Message = $"Подключение '{name}' не зарегистрировано.",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                // ignoreEnabled: true — тестируем даже отключённые подключения
                // (админ проверяет «до включения»).
                await using var conn = await _connectionProvider
                    .CreateConnectionAsync(name, ignoreEnabled: true, cancellationToken);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = 10;

                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                sw.Stop();

                var ok = result != null && result != DBNull.Value;
                return new SqlAgentTestResultDto
                {
                    Success = ok,
                    Message = ok
                        ? $"Соединение установлено (SELECT 1 → {result})."
                        : "Соединение установлено, но SELECT 1 вернул пусто.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new SqlAgentTestResultDto
                {
                    Success = false,
                    Message = "Проверка отменена.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex,
                    "SqlAgent: тест подключения '{Name}' завершился ошибкой", name);
                return new SqlAgentTestResultDto
                {
                    Success = false,
                    Message = $"Ошибка подключения: {ex.Message}",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        /// <inheritdoc/>
        public async Task<SqlAgentConnectionItemDto> ResetConnectionAsync(
            string name,
            int userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя подключения обязательно.", nameof(name));

            // Проверяем, что подключение вообще есть в baseline.
            if (_optionsProvider.Get(name) == null)
                throw new ArgumentException(
                    $"Подключение '{name}' не зарегистрировано.", nameof(name));

            // --- Удаляем все override-ключи для этого подключения ---
            var allSettings = await _settingsService.GetAllAsync();
            var prefix = $"{SettingKeyPrefix}{name}.";

            var toDelete = allSettings
                .Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var s in toDelete)
            {
                await _settingsService.DeleteAsync(s.Id);
            }

            // --- Применяем reset in-memory (SqlAgentOptionsProvider вернётся к baseline) ---
            _optionsProvider.Reset(name);

            // --- Аудит ---
            await LogAdminActionAsync(userId, "admin.sqlagent.connection.reset", name);

            _logger.LogInformation(
                "SqlAgent: подключение '{Name}' сброшено к baseline администратором {UserId} " +
                "(удалено override'ов: {Count})",
                name, userId, toDelete.Count);

            var baseline = _optionsProvider.Get(name);
            return ToDto(name, baseline, isOverridden: false);
        }

        // ============ Private ============

        /// <summary>
        /// Возвращает множество имён подключений, для которых есть override
        /// в AppSettings (ключи с префиксом <c>SqlAgent.{name}.</c>).
        /// </summary>
        private static HashSet<string> GetOverrideConnectionNames(
            IReadOnlyList<SettingDto> allSettings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in allSettings)
            {
                if (string.IsNullOrEmpty(s.Key)) continue;
                if (!s.Key.StartsWith(SettingKeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                // Ключ вида SqlAgent.{name}.{field} — берём {name}.
                // Отсекаем "SqlAgent.enabled" (глобальный флаг) — у него нет второй точки.
                var rest = s.Key.Substring(SettingKeyPrefix.Length);
                var dotIdx = rest.IndexOf('.');
                if (dotIdx <= 0) continue;

                result.Add(rest.Substring(0, dotIdx));
            }

            return result;
        }

        /// <summary>
        /// Upsert-ит значение override'а в AppSettings.
        /// </summary>
        private async Task UpsertSettingAsync(
            IReadOnlyList<SettingDto> allSettings,
            string connectionName,
            string field,
            string value,
            string type)
        {
            var key = $"{SettingKeyPrefix}{connectionName}.{field}";
            var existing = allSettings.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                await _settingsService.UpdateAsync(existing.Id, new SettingDto
                {
                    Id = existing.Id,
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = SettingCategory
                });
            }
            else
            {
                await _settingsService.CreateAsync(new SettingDto
                {
                    Key = key,
                    Value = value,
                    Type = type,
                    Category = SettingCategory
                });
            }
        }

        /// <summary>
        /// Мапит опции подключения в DTO для админки.
        /// </summary>
        private static SqlAgentConnectionItemDto ToDto(
            string name, SqlAgentConnectionOptions opts, bool isOverridden)
        {
            return new SqlAgentConnectionItemDto
            {
                Name = name,
                DisplayName = opts.DisplayName,
                Provider = opts.Provider,
                Enabled = opts.Enabled,
                AllowedTables = opts.AllowedTables ?? new List<string>(),
                DeniedTables = opts.DeniedTables ?? new List<string>(),
                MaxRows = opts.MaxRows,
                StatementTimeoutSeconds = opts.StatementTimeoutSeconds,
                RequiresApproval = opts.RequiresApproval,
                Description = opts.Description,
                IsOverridden = isOverridden
            };
        }

        /// <summary>
        /// Пишет действие администратора в журнал аудита.
        /// </summary>
        private async Task LogAdminActionAsync(int userId, string action, string connectionName)
        {
            try
            {
                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = userId,
                    ToolName = action,
                    ParametersJson = $"{{\"connection\":\"{connectionName.Replace("\"", "\\\"")}\"}}",
                    Status = "Success",
                    DurationMs = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось записать аудит SqlAgent действия {Action}", action);
            }
        }
    }
}