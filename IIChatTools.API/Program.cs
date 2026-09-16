using System;
using System.Threading.Tasks;
using IIChatTools.API.Extensions;
using IIChatTools.Data;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var proxyUrl = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY")
                        ?? "http://222.1.20.1:8080";
            var proxyUser = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY_USER")
                            ?? "nikiforov";
            var proxyPass = Environment.GetEnvironmentVariable("IICHATTOOLS_PROXY_PASS")
                            ?? "6989";

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
    }
}