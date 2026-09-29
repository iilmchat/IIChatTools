using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.SqlAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.SqlAgent
{
    /// <summary>
    /// Фабрика <see cref="DbConnection"/> по имени подключения
    /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 4.1 и § 6.1).
    /// <para>
    /// <b>Стратегия:</b> switch по <c>Provider</c> — тип известен на этапе
    /// компиляции, что даёт compile-time проверку, скорость и совместимость
    /// с AOT/trimming. Рефлексия (<c>Activator.CreateInstance</c>) не
    /// используется — список провайдеров фиксирован в DESIGN § 5.1.
    /// </para>
    /// <para>
    /// <b>Connection string</b> резолвится из <see cref="IConfiguration"/>
    /// по ключу <see cref="SqlAgentConnectionOptions.ConnectionStringKey"/>
    /// (User Secrets / env / appsettings) — <b>при каждом вызове</b>,
    /// без кэша (см. DESIGN § 5.4).
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Singleton. Не держит состояния, кроме ссылок на
    /// Singleton-зависимости.
    /// </para>
    /// </summary>
    public class SqlConnectionProvider : ISqlConnectionProvider
    {
        private readonly SqlAgentOptionsProvider _optionsProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SqlConnectionProvider> _logger;

        /// <summary>
        /// Создаёт провайдер.
        /// </summary>
        /// <param name="optionsProvider">Провайдер актуальных опций SqlAgent</param>
        /// <param name="configuration">Конфигурация приложения (для резолвинга connection string)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public SqlConnectionProvider(
            SqlAgentOptionsProvider optionsProvider,
            IConfiguration configuration,
            ILogger<SqlConnectionProvider> logger)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
        }

        /// <inheritdoc/>
        public bool Exists(string connectionName)
        {
            return _optionsProvider.Exists(connectionName);
        }

        /// <inheritdoc/>
        public string GetProvider(string connectionName)
        {
            var opts = _optionsProvider.Get(connectionName);
            if (opts == null)
                throw new ArgumentException(
                    $"Подключение '{connectionName}' не зарегистрировано.",
                    nameof(connectionName));
            return opts.Provider;
        }

        /// <inheritdoc/>
        public async Task<DbConnection> CreateConnectionAsync(
            string connectionName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentException("Имя подключения обязательно.", nameof(connectionName));

            var opts = _optionsProvider.Get(connectionName)
                ?? throw new ArgumentException(
                    $"Подключение '{connectionName}' не зарегистрировано.",
                    nameof(connectionName));

            if (!opts.Enabled)
                throw new ArgumentException(
                    $"Подключение '{connectionName}' отключено администратором.",
                    nameof(connectionName));

            var connStr = ResolveConnectionString(connectionName, opts);
            var provider = (opts.Provider ?? string.Empty).Trim();

            DbConnection connection;
            switch (provider.ToLowerInvariant())
            {
                case "sqlite":
                    connection = new SqliteConnection(connStr);
                    break;
                case "sqlserver":
                    connection = new SqlConnection(connStr);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Провайдер БД '{provider}' не поддерживается. " +
                        "Допустимые (Фаза 1): Sqlite, SqlServer. " +
                        "Внешние провайдеры (Postgres / MySql) — Фаза 2 (KI-099).");
            }

            try
            {
                await connection.OpenAsync(cancellationToken);
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            _logger.LogDebug(
                "SqlAgent: соединение открыто. connection={Name}, provider={Provider}",
                connectionName, provider);

            return connection;
        }

        /// <summary>
        /// Резолвит строку подключения по ключу <see cref="SqlAgentConnectionOptions.ConnectionStringKey"/>.
        /// </summary>
        /// <param name="name">Имя подключения (для сообщений об ошибках)</param>
        /// <param name="opts">Опции подключения</param>
        /// <returns>Строка подключения</returns>
        /// <exception cref="InvalidOperationException">
        /// Если ключ не задан или значение не найдено в конфигурации.
        /// </exception>
        private string ResolveConnectionString(string name, SqlAgentConnectionOptions opts)
        {
            if (string.IsNullOrWhiteSpace(opts.ConnectionStringKey))
                throw new InvalidOperationException(
                    $"Подключение '{name}': не задан ConnectionStringKey в конфигурации SqlAgent.");

            var connStr = _configuration[opts.ConnectionStringKey];
            if (string.IsNullOrWhiteSpace(connStr))
                throw new InvalidOperationException(
                    $"Подключение '{name}': строка подключения " +
                    $"'{opts.ConnectionStringKey}' не найдена. " +
                    "Задайте её в User Secrets (`dotnet user-secrets set ...`) " +
                    "или через переменную окружения.");

            return connStr;
        }
    }
}