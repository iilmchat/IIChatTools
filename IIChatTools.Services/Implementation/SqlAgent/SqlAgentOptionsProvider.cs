using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.SqlAgent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Провайдер актуальных опций Database Agent
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 5.4).
    /// <para>
    /// Хранит <b>baseline</b> из <c>appsettings.json</c> (через <c>IOptions</c>)
    /// и <b>runtime-overrides</b> подключений (из <c>AppSettings</c>, применённые
    /// через <see cref="UpdateConnection"/>/<see cref="UpdateEnabled"/>).
    /// </para>
    /// <para>
    /// <b>Потокобезопасность:</b> все операции чтения/записи защищены одним
    /// <c>lock</c>. Изменения применяются мгновенно (без рестарта приложения).
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Singleton. Все потребители (<c>SqlConnectionProvider</c>,
    /// <c>SqlQueryValidator</c>, <c>SqlAgentService</c>) читают актуальные значения
    /// при каждом вызове — не кэшируют.
    /// </para>
    /// </summary>
    public class SqlAgentOptionsProvider
    {
        private const int MinMaxRows = 1;
        private const int MaxMaxRows = 10_000;
        private const int MinTimeoutSeconds = 1;
        private const int MaxTimeoutSeconds = 300;

        private readonly SqlAgentOptions _baseline;
        private readonly ILogger<SqlAgentOptionsProvider> _logger;
        private readonly object _lock = new object();
        private readonly Dictionary<string, SqlAgentConnectionOptions> _overrides =
            new Dictionary<string, SqlAgentConnectionOptions>(StringComparer.OrdinalIgnoreCase);

        private bool _runtimeEnabled;

        /// <summary>
        /// Создаёт провайдер из baseline-опций и выполняет первичную валидацию.
        /// </summary>
        /// <param name="options">Опции из <c>appsettings:SqlAgent</c></param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="options"/> = null</exception>
        /// <exception cref="InvalidOperationException">
        /// Если baseline невалиден (DESIGN § 5.5) — например,
        /// <c>DefaultConnection</c> не найден в <c>Connections</c>.
        /// </exception>
        public SqlAgentOptionsProvider(
            IOptions<SqlAgentOptions> options,
            ILogger<SqlAgentOptionsProvider> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _baseline = options.Value
                ?? throw new InvalidOperationException(
                    "Секция 'SqlAgent' отсутствует в конфигурации.");
            _logger = logger;
            _runtimeEnabled = _baseline.Enabled;
            ValidateBaseline(_baseline);
        }

        /// <summary>
        /// Глобальный флаг включения Database Agent (с учётом runtime-override).
        /// </summary>
        public bool Enabled
        {
            get { lock (_lock) { return _runtimeEnabled; } }
        }

        /// <summary>
        /// Имя подключения по умолчанию (baseline, из <c>appsettings.json</c>).
        /// </summary>
        public string DefaultConnection => _baseline.DefaultConnection;

        /// <summary>
        /// Правила валидации SQL (baseline, из <c>appsettings.json</c>).
        /// </summary>
        public SqlQueryValidationOptions QueryValidation => _baseline.QueryValidation;

        /// <summary>
        /// Возвращает актуальные опции подключения (override → baseline).
        /// Возвращает <c>null</c>, если подключение не зарегистрировано.
        /// <para>
        /// <b>Возвращает копию</b> — вызывающий не может случайно
        /// модифицировать внутреннее состояние провайдера.
        /// </para>
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <returns>Копия опций или <c>null</c></returns>
        public SqlAgentConnectionOptions Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            lock (_lock)
            {
                if (_overrides.TryGetValue(name, out var ov)) return Clone(ov);
                if (_baseline.Connections.TryGetValue(name, out var bs)) return Clone(bs);
                return null;
            }
        }

        /// <summary>
        /// Возвращает имена всех известных подключений (baseline ∪ overrides).
        /// </summary>
        public IReadOnlyList<string> GetAllConnectionNames()
        {
            lock (_lock)
            {
                var set = new HashSet<string>(
                    _baseline.Connections.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var k in _overrides.Keys) set.Add(k);
                return set.ToList();
            }
        }

        /// <summary>
        /// Проверяет, что подключение зарегистрировано И включено (Enabled=true).
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <returns>true, если подключение существует и включено</returns>
        public bool Exists(string name)
        {
            var opts = Get(name);
            return opts != null && opts.Enabled;
        }

        /// <summary>
        /// Применяет runtime-override опций подключения
        /// (вызывается из <c>Program.LoadSqlAgentOverridesAsync</c> и админки).
        /// </summary>
        /// <param name="name">Имя подключения</param>
        /// <param name="options">Новые опции (заменяют override полностью)</param>
        public void UpdateConnection(string name, SqlAgentConnectionOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя подключения обязательно.", nameof(name));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            lock (_lock)
            {
                _overrides[name] = options;
            }
            _logger.LogInformation(
                "SqlAgent override обновлён: connection={Name}, allowedTables={Allowed}, maxRows={Max}",
                name, options.AllowedTables?.Count ?? 0, options.MaxRows);
        }

        /// <summary>
        /// Применяет runtime-override глобального флага <c>Enabled</c>.
        /// </summary>
        /// <param name="enabled">Новое значение флага</param>
        public void UpdateEnabled(bool enabled)
        {
            lock (_lock) { _runtimeEnabled = enabled; }
            _logger.LogInformation("SqlAgent global Enabled = {Enabled}", enabled);
        }

        /// <summary>
        /// Сбрасывает override указанного подключения (возврат к baseline).
        /// </summary>
        /// <param name="name">Имя подключения</param>
        public void Reset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (_lock) { _overrides.Remove(name); }
            _logger.LogInformation("SqlAgent override сброшен: connection={Name}", name);
        }

        // ============ Private ============

        private void ValidateBaseline(SqlAgentOptions opts)
        {
            // Критичная проверка (DESIGN § 5.5): DefaultConnection должен существовать.
            if (string.IsNullOrWhiteSpace(opts.DefaultConnection))
                throw new InvalidOperationException(
                    "SqlAgent:DefaultConnection не задан в конфигурации.");

            if (!opts.Connections.ContainsKey(opts.DefaultConnection))
                throw new InvalidOperationException(
                    $"SqlAgent:DefaultConnection='{opts.DefaultConnection}' отсутствует в Connections.");

            // Некритичные проверки: clamp + warning.
            foreach (var kv in opts.Connections)
            {
                var name = kv.Key;
                var conn = kv.Value;

                if (string.IsNullOrWhiteSpace(conn.Provider))
                    throw new InvalidOperationException(
                        $"SqlAgent:Connections['{name}'].Provider не задан.");

                if (string.IsNullOrWhiteSpace(conn.ConnectionStringKey))
                    _logger.LogWarning(
                        "SqlAgent:Connections['{Name}'].ConnectionStringKey не задан — " +
                        "подключение не сможет открыть соединение.", name);

                if (conn.MaxRows < MinMaxRows || conn.MaxRows > MaxMaxRows)
                {
                    var clamped = Math.Clamp(conn.MaxRows, MinMaxRows, MaxMaxRows);
                    _logger.LogWarning(
                        "SqlAgent:Connections['{Name}'].MaxRows={Old} вне [{Min}..{Max}] — " +
                        "применён clamp до {New}.", name, conn.MaxRows, MinMaxRows, MaxMaxRows, clamped);
                    conn.MaxRows = clamped;
                }

                if (conn.StatementTimeoutSeconds < MinTimeoutSeconds
                    || conn.StatementTimeoutSeconds > MaxTimeoutSeconds)
                {
                    var clamped = Math.Clamp(conn.StatementTimeoutSeconds,
                        MinTimeoutSeconds, MaxTimeoutSeconds);
                    _logger.LogWarning(
                        "SqlAgent:Connections['{Name}'].StatementTimeoutSeconds={Old} вне " +
                        "[{Min}..{Max}] — применён clamp до {New}.",
                        name, conn.StatementTimeoutSeconds,
                        MinTimeoutSeconds, MaxTimeoutSeconds, clamped);
                    conn.StatementTimeoutSeconds = clamped;
                }

                var intersection = conn.AllowedTables
                    .Intersect(conn.DeniedTables, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (intersection.Count > 0)
                {
                    _logger.LogWarning(
                        "SqlAgent:Connections['{Name}'] — таблицы одновременно в Allowed и " +
                        "Denied (Denied перебивает): {Tables}.",
                        name, string.Join(", ", intersection));
                }

                if (conn.AllowedTables.Count == 0)
                {
                    _logger.LogWarning(
                        "SqlAgent:Connections['{Name}'] — AllowedTables пуст. " +
                        "Агент не сможет выполнить ни одного запроса.", name);
                }
            }
        }

        private static SqlAgentConnectionOptions Clone(SqlAgentConnectionOptions src)
        {
            if (src == null) return null;
            return new SqlAgentConnectionOptions
            {
                DisplayName = src.DisplayName,
                Provider = src.Provider,
                ConnectionStringKey = src.ConnectionStringKey,
                AllowedTables = src.AllowedTables == null
                    ? new List<string>() : new List<string>(src.AllowedTables),
                DeniedTables = src.DeniedTables == null
                    ? new List<string>() : new List<string>(src.DeniedTables),
                MaxRows = src.MaxRows,
                StatementTimeoutSeconds = src.StatementTimeoutSeconds,
                RequiresApproval = src.RequiresApproval,
                Description = src.Description,
                Enabled = src.Enabled
            };
        }
    }
}