using System;
using System.Collections.Generic;
using System.Linq;                // v1.4.0 Фаза 6 (KI-052)
using System.Threading.Tasks;
using IIChatTools.API.Extensions;
using IIChatTools.Data;
using IIChatTools.Services.DTO.SqlAgent;   // v1.7.0 (KI-097): SqlAgent
using IIChatTools.Services.DTO.SubAgent;   // v1.4.0 Фаза 6 (KI-052)
using IIChatTools.Services.Implementation.SqlAgent;  // v1.7.0 (KI-097): SqlAgentOptionsProvider
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;             // v1.4.0 Фаза 6 (KI-052)

namespace IIChatTools.API
{
    /// <summary>
    /// Точка входа в приложение IIChatTools.
    /// Выполняет инициализацию (проверка зависимостей, подготовка БД, seed, синхронизация настроек)
    /// и запускает web-хост.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Запускает приложение: проверяет зависимости, применяет миграции,
        /// инициализирует роли и синхронизирует настройки.
        /// </summary>
        /// <param name="args">Аргументы командной строки</param>
        /// <returns>Асинхронная задача</returns>
        public static async Task Main(string[] args)
        {
            // Настраиваем прокси для .NET (влияет на ClientWebSocket в PuppeteerSharp,
            // а также на стандартные HttpClient, если они не настроены явно).
            // Локальные адреса должны идти напрямую, иначе WebSocket-соединение
            // PuppeteerSharp ↔ Chromium упадёт с 403 от корпоративного прокси.
            ConfigureDefaultProxy();

            var host = CreateHostBuilder(args).Build();

            try
            {
                await InitializeApplicationAsync(host);
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                // Логируем фатальную ошибку и корректно останавливаем хост
                var logger = host.Services
                    .GetService<ILogger<Program>>();

                logger?.LogCritical(ex, "Фатальная ошибка при запуске приложения IIChatTools");
                throw;
            }
            finally
            {
                // Graceful shutdown: даём приложению 10 секунд на завершение
                // (IBrowserSessionManager, IDisposable-сервисы освобождаются автоматически хостом)
                try
                {
                    await host.StopAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception stopEx)
                {
                    var logger = host.Services.GetService<ILogger<Program>>();
                    logger?.LogWarning(stopEx, "Ошибка при остановке хоста");
                }
            }
        }

        /// <summary>
        /// Настраивает прокси по умолчанию для .NET с исключением loopback-адресов.
        /// Учитывает, что в .NET Core 3.1 ClientWebSocket читает HttpClient.DefaultProxy,
        /// а не WebRequest.DefaultWebProxy.
        /// </summary>
        private static void ConfigureDefaultProxy()
        {
            var proxyUrl  = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY");
            var proxyUser = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY_USER");
            var proxyPass = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY_PASS");

            if (string.IsNullOrWhiteSpace(proxyUrl))
            {
                Console.WriteLine("[INFO] Прокси не настроен (IICHATTOOLS_PROXY пуст)");
                return;
            }

            // Placeholder из appsettings — игнорируем (KI-059).
            if (proxyUrl.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[INFO] Прокси не настроен (placeholder, не задан в User Secrets)");
                return;
            }

            // Требуем абсолютный URL со схемой http/https.
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var parsedProxy)
                || (parsedProxy.Scheme != Uri.UriSchemeHttp && parsedProxy.Scheme != Uri.UriSchemeHttps))
            {
                Console.Error.WriteLine($"[WARN] Некорректный URL прокси: {proxyUrl}");
                return;
            }

            try
            {
                // 1) HTTP-прокси для HttpClient и ClientWebSocket (используется PuppeteerSharp)
                var httpProxy = new System.Net.Http.HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy(proxyUrl, true)
                    {
                        BypassList = new[]
                        {
                            @"localhost",
                            @"127\.0\.0\.1",
                            @"::1",
                            @".*\.local"
                        },
                        BypassProxyOnLocal = true,
                        Credentials = !string.IsNullOrWhiteSpace(proxyUser)
                            ? new System.Net.NetworkCredential(proxyUser, proxyPass)
                            : null
                    },
                    UseProxy = true
                };

                // Устанавливаем глобальный прокси для HttpClient
                System.Net.Http.HttpClient.DefaultProxy = httpProxy.Proxy;

                // 2) То же для старого WebRequest (на случай legacy-кода)
                System.Net.WebRequest.DefaultWebProxy = new System.Net.WebProxy(proxyUrl, true)
                {
                    BypassList = new[]
                    {
                        @"localhost",
                        @"127\.0\.0\.1",
                        @"::1"
                    },
                    BypassProxyOnLocal = true,
                    Credentials = !string.IsNullOrWhiteSpace(proxyUser)
                        ? new System.Net.NetworkCredential(proxyUser, proxyPass)
                        : null
                };

                Console.WriteLine($"[INFO] Прокси настроен: {proxyUrl} (bypass: localhost, 127.0.0.1)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Не удалось настроить прокси: {ex.Message}");
            }
        }

        /// <summary>
        /// Создаёт построитель хоста с настройками логирования и Startup.
        /// </summary>
        /// <param name="args">Аргументы командной строки</param>
        /// <returns>Построитель хоста</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    // Очищаем провайдеры по умолчанию и настраиваем свои.
                    // Файловое логирование технических сообщений — в плане v1.1 (через Serilog).
                    // Аудит действий пользователей пишется в БД и/или JSONL-файл (см. CompositeAuditService).
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();

                    // В Development добавляем детализацию
                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Information);
                    }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        /// <summary>
        /// Выполняет инициализацию приложения до старта web-сервера:
        /// 1) проверка внешних зависимостей;
        /// 2) подготовка БД (миграции или EnsureCreated в зависимости от провайдера);
        /// 3) создание ролей Admin/User;
        /// 4) синхронизация настроек из appsettings.json в БД.
        /// </summary>
        /// <param name="host">Хост приложения</param>
        /// <returns>Асинхронная задача</returns>
        /// <exception cref="ArgumentNullException">Если host равен null</exception>
        private static async Task InitializeApplicationAsync(IHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            using var scope = host.Services.CreateScope();
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            try
            {
                logger.LogInformation("=== IIChatTools v{Version} — запуск инициализации ===", AppVersion.Current);
                logger.LogInformation("{Copyright}", AppVersion.Copyright);

                // Передаём версию в слой сервисов (разрыв зависимости Services → API)
                AppVersionHolder.Current = AppVersion.Current;

                // ---------- 1. Проверка внешних зависимостей ----------
                var checker = services.GetRequiredService<IDependencyChecker>();
                var deps = await checker.CheckAllAsync();

                foreach (var dep in deps)
                {
                    var status = dep.Value != null
                        ? $"доступен ({dep.Value})"
                        : "не установлен";
                    logger.LogInformation("Зависимость {Name}: {Status}", dep.Key, status);
                }

                // ---------- 2. Подготовка БД ----------
                var provider = configuration["Database:Provider"] ?? "SqlServer";
                logger.LogInformation("Провайдер базы данных: {Provider}", provider);

                var dbContext = services.GetRequiredService<AppDbContext>();

                if (string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
                {
                    // Для SQL Server применяем миграции EF Core
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Миграции SQL Server применены успешно");
                }
                else if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    // Для SQLite используем EnsureCreatedAsync — схема создаётся автоматически.
                    // При изменении модели потребуется удалить файл БД и перезапустить приложение.
                    await dbContext.Database.EnsureCreatedAsync();
                    logger.LogInformation("Схема SQLite создана/проверена");
                }
                else if (string.Equals(provider, "inmemory", StringComparison.OrdinalIgnoreCase))
                {
                    // Для InMemory создаём схему в памяти
                    await dbContext.Database.EnsureCreatedAsync();
                    logger.LogInformation("Схема InMemory создана (данные будут утеряны при перезапуске)");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Неподдерживаемый провайдер БД: '{provider}'. " +
                        "Допустимые значения: SqlServer, Sqlite, InMemory.");
                }

                // ---------- 3. Создание ролей ----------
                await SeedData.InitializeAsync(services);
                logger.LogInformation("Роли Admin/User инициализированы");

                // ---------- 4. Синхронизация настроек ----------
                // В режиме InMemory настройки всё равно синхронизируются при каждом старте
                var settingsService = services.GetRequiredService<IAppSettingsService>();
                var syncedCount = await settingsService.SyncDefaultsFromConfigurationAsync();
                logger.LogInformation("Синхронизировано настроек по умолчанию: {Count}", syncedCount);

                // ---------- 5. SubAgent overrides (v1.4.0 Фаза 6, KI-052) ----------
                // Читаем сохранённые в AppSettings override'ы агентов и применяем к реестру.
                await LoadSubAgentOverridesAsync(services, logger);

                // ---------- 6. SqlAgent overrides (v1.7.0 KI-097, Фаза 2) ----------
                // Читаем сохранённые в AppSettings override'ы подключений SQL Agent
                // (ключи с префиксом SqlAgent.) и применяем к SqlAgentOptionsProvider.
                LoadSqlAgentOverrides(services, logger);

                logger.LogInformation(
                    "=== IIChatTools v{Version} готов к работе ===",
                    AppVersion.Current);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "Критическая ошибка инициализации. Приложение будет остановлено.");
                throw;
            }
        }
        /// <summary>
        /// Загружает override'ы специализированных суб-агентов из AppSettings
        /// и применяет их к <see cref="ISubAgentRegistry"/> (v1.4.0 Фаза 6, KI-052).
        ///
        /// Override'ы хранятся как JSON по ключу <c>SubAgents.{name}</c>
        /// (см. <c>AdminAgentsController.SaveAgentOverrideAsync</c>).
        /// </summary>
        /// <param name="services">Провайдер сервисов (уже в scope)</param>
        /// <param name="logger">Логгер</param>
        private static async Task LoadSubAgentOverridesAsync(IServiceProvider services, ILogger logger)
        {
            try
            {
                var settingsService = services.GetRequiredService<IAppSettingsService>();
                var registry = services.GetRequiredService<ISubAgentRegistry>();

                var settings = await settingsService.GetAllAsync();
                var overrides = settings
                    .Where(s => s.Key.StartsWith("SubAgents.", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(s.Value))
                    .ToList();

                if (overrides.Count == 0)
                {
                    logger.LogInformation("SubAgent overrides: нет");
                    return;
                }

                var applied = 0;
                foreach (var s in overrides)
                {
                    try
                    {
                        var descriptor = JsonConvert.DeserializeObject<SubAgentDescriptor>(s.Value);
                        if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Name))
                        {
                            logger.LogWarning("SubAgent override {Key}: пустой дескриптор", s.Key);
                            continue;
                        }

                        registry.Update(descriptor);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "SubAgent override {Key}: ошибка разбора JSON", s.Key);
                    }
                }

                logger.LogInformation("SubAgent overrides: применено {Applied} из {Total}", applied, overrides.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SubAgent overrides: не удалось загрузить");
            }
        }

        /// <summary>
        /// Загружает override'ы подключений Database Agent из <c>AppSettings</c>
        /// и применяет их к <see cref="SqlAgentOptionsProvider"/>
        /// (v1.7.0, KI-097, DESIGN_DB_AGENT § 5.4).
        /// <para>
        /// Формат ключей в <c>AppSettings</c> (например, для подключения
        /// <c>internal</c>):
        /// <list type="bullet">
        ///   <item><description><c>SqlAgent.internal.enabled</c> — bool;</description></item>
        ///   <item><description><c>SqlAgent.internal.allowedTables</c> — JSON-массив строк;</description></item>
        ///   <item><description><c>SqlAgent.internal.deniedTables</c> — JSON-массив строк;</description></item>
        ///   <item><description><c>SqlAgent.internal.maxRows</c> — int;</description></item>
        ///   <item><description><c>SqlAgent.internal.statementTimeoutSeconds</c> — int;</description></item>
        ///   <item><description><c>SqlAgent.enabled</c> — bool (глобальный флаг).</description></item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="services">Провайдер сервисов (уже в scope)</param>
        /// <param name="logger">Логгер</param>
        private static void LoadSqlAgentOverrides(IServiceProvider services, ILogger logger)
        {
            try
            {
                var settingsService = services.GetRequiredService<IAppSettingsService>();
                var provider = services.GetRequiredService<SqlAgentOptionsProvider>();

                // Синхронное чтение (метод вызывается один раз при старте).
                var settings = settingsService.GetAllAsync().GetAwaiter().GetResult();
                var sqlAgentSettings = settings
                    .Where(s => s.Key.StartsWith("SqlAgent.", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(s.Value))
                    .ToList();

                if (sqlAgentSettings.Count == 0)
                {
                    logger.LogInformation("SqlAgent overrides: нет");
                    return;
                }

                // Глобальный флаг: ключ "SqlAgent.enabled".
                var globalEnabled = sqlAgentSettings
                    .FirstOrDefault(s => s.Key.Equals("SqlAgent.enabled",
                        StringComparison.OrdinalIgnoreCase));
                if (globalEnabled != null && bool.TryParse(globalEnabled.Value, out var gEnabled))
                {
                    provider.UpdateEnabled(gEnabled);
                }

                // Per-connection override'ы: группируем по имени подключения.
                // Ключ "SqlAgent.{name}.{field}".
                var byConnection = sqlAgentSettings
                    .Where(s => !s.Key.Equals("SqlAgent.enabled", StringComparison.OrdinalIgnoreCase))
                    .Select(s => new { Parts = s.Key.Split('.'), Setting = s })
                    .Where(x => x.Parts.Length >= 3)
                    .GroupBy(x => x.Parts[1], StringComparer.OrdinalIgnoreCase);

                var applied = 0;
                foreach (var group in byConnection)
                {
                    var name = group.Key;
                    var baseline = provider.Get(name);
                    if (baseline == null)
                    {
                        logger.LogWarning(
                            "SqlAgent override: подключение '{Name}' не найдено в baseline — пропущен.",
                            name);
                        continue;
                    }

                    foreach (var item in group)
                    {
                        var field = item.Parts.Length >= 3 ? item.Parts[2] : null;
                        var value = item.Setting.Value;
                        try
                        {
                            switch (field?.ToLowerInvariant())
                            {
                                case "enabled":
                                    if (bool.TryParse(value, out var en)) baseline.Enabled = en;
                                    break;
                                case "allowedtables":
                                    baseline.AllowedTables =
                                        JsonConvert.DeserializeObject<List<string>>(value)
                                        ?? new List<string>();
                                    break;
                                case "deniedtables":
                                    baseline.DeniedTables =
                                        JsonConvert.DeserializeObject<List<string>>(value)
                                        ?? new List<string>();
                                    break;
                                case "maxrows":
                                    if (int.TryParse(value, out var mr)) baseline.MaxRows = mr;
                                    break;
                                case "statementtimeoutseconds":
                                    if (int.TryParse(value, out var st))
                                        baseline.StatementTimeoutSeconds = st;
                                    break;
                                default:
                                    logger.LogWarning(
                                        "SqlAgent override {Key}: неизвестное поле '{Field}' — пропущено.",
                                        item.Setting.Key, field);
                                    break;
                            }
                        }
                        catch (Exception parseEx)
                        {
                            logger.LogWarning(parseEx,
                                "SqlAgent override {Key}: ошибка разбора значения '{Value}'.",
                                item.Setting.Key, value);
                        }
                    }

                    provider.UpdateConnection(name, baseline);
                    applied++;
                }

                logger.LogInformation(
                    "SqlAgent overrides: применено {Applied} подключений из {Total} настроек.",
                    applied, sqlAgentSettings.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SqlAgent overrides: не удалось загрузить");
            }
        }
    }
}