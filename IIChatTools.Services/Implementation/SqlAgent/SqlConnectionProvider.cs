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
using System.IO;   // v1.7.0 (KI-100): Path.Combine для Sqlite relative paths

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
        private readonly IAppPathProvider _appPathProvider;
        private readonly ILogger<SqlConnectionProvider> _logger;

        /// <summary>
        /// Создаёт провайдер.
        /// </summary>
        /// <param name="optionsProvider">Провайдер актуальных опций SqlAgent</param>
        /// <param name="configuration">Конфигурация приложения (для резолвинга connection string)</param>
        /// <param name="appPathProvider">
        /// Провайдер путей приложения. Используется для резолвинга **относительных**
        /// путей Sqlite относительно <c>ContentRootPath</c> (KI-100).
        /// </param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public SqlConnectionProvider(
            SqlAgentOptionsProvider optionsProvider,
            IConfiguration configuration,
            IAppPathProvider appPathProvider,
            ILogger<SqlConnectionProvider> logger)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _appPathProvider = appPathProvider ?? throw new ArgumentNullException(nameof(appPathProvider));
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
            bool ignoreEnabled = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentException("Имя подключения обязательно.", nameof(connectionName));

            var opts = _optionsProvider.Get(connectionName)
                ?? throw new ArgumentException(
                    $"Подключение '{connectionName}' не зарегистрировано.",
                    nameof(connectionName));

            // v1.7.0 (KI-097, Фаза 6A): ignoreEnabled — для админского теста
            // отключённых подключений (проверка «до включения»).
            if (!ignoreEnabled && !opts.Enabled)
                throw new ArgumentException(
                    $"Подключение '{connectionName}' отключено администратором.",
                    nameof(connectionName));

            var connStr = ResolveConnectionString(connectionName, opts);
            var provider = (opts.Provider ?? string.Empty).Trim();

            DbConnection connection;
            switch (provider.ToLowerInvariant())
            {
                case "sqlite":
                    // KI-100: Sqlite открывает файл относительно CWD процесса, а не
                    // относительно ContentRootPath. Резолвим относительный путь от
                    // IWebHostEnvironment.ContentRootPath, чтобы `dotnet run` из любой
                    // директории и запуск как службы работали одинаково.
                    var sqliteConnStr = ResolveSqlitePath(connStr, connectionName);
                    connection = new SqliteConnection(sqliteConnStr);
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

        /// <summary>
        /// Резолвит относительный путь Sqlite относительно
        /// <see cref="IAppPathProvider.ContentRootPath"/> (KI-100).
        /// <para>
        /// <c>:memory:</c> и абсолютные пути возвращаются без изменений.
        /// При ошибке парсинга — возвращаем строку как есть (defensive).
        /// </para>
        /// </summary>
        /// <param name="connStr">Исходная connection string</param>
        /// <param name="connectionName">Имя подключения (для логов)</param>
        /// <returns>Connection string с абсолютным DataSource (если был относительный)</returns>
        private string ResolveSqlitePath(string connStr, string connectionName)
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder(connStr);
                var dataSource = builder.DataSource;
                if (string.IsNullOrWhiteSpace(dataSource)) return connStr;

                // Специальные значения Sqlite — не трогаем.
                if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
                    return connStr;
                if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    return connStr;

                if (Path.IsPathRooted(dataSource)) return connStr;

                var absolute = Path.GetFullPath(
                    Path.Combine(_appPathProvider.ContentRootPath, dataSource));
                builder.DataSource = absolute;

                _logger.LogDebug(
                    "Sqlite DataSource резолвлен: {Relative} → {Absolute} (connection={Name})",
                    dataSource, absolute, connectionName);

                return builder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось резолвить Sqlite DataSource для connection={Name}. " +
                    "Использую исходную строку.", connectionName);
                return connStr;
            }
        }
    }
}