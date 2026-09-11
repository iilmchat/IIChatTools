using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API
{
    /// <summary>
    /// Точка входа в приложение IIChatTools.
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
            var host = CreateHostBuilder(args).Build();

            await InitializeApplicationAsync(host);

            try
            {
                await host.RunAsync();
            }
            finally
            {
                await host.StopAsync(TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>
        /// Создаёт построитель хоста с настройками по умолчанию.
        /// </summary>
        /// <param name="args">Аргументы командной строки</param>
        /// <returns>Построитель хоста</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        /// <summary>
        /// Выполняет инициализацию приложения перед запуском web-сервера.
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

            try
            {
                logger.LogInformation("=== IIChatTools v{Version} — запуск инициализации ===", AppVersion.Current);
                AppVersionHolder.Current = AppVersion.Current;

                // 1. Проверка внешних зависимостей
                var checker = services.GetRequiredService<IDependencyChecker>();
                var deps = await checker.CheckAllAsync();
                foreach (var dep in deps)
                {
                    var status = dep.Value != null ? $"доступен ({dep.Value})" : "не установлен";
                    logger.LogInformation("Зависимость {Name}: {Status}", dep.Key, status);
                }

                // 2. Применение миграций EF Core
                var dbContext = services.GetRequiredService<AppDbContext>();
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Миграции применены успешно");

                // 3. Создание ролей
                await SeedData.InitializeAsync(services);
                logger.LogInformation("Роли инициализированы");

                // 4. Синхронизация настроек из appsettings.json в БД
                var settingsService = services.GetRequiredService<IAppSettingsService>();
                var syncedCount = await settingsService.SyncDefaultsFromConfigurationAsync();
                logger.LogInformation("Синхронизировано настроек по умолчанию: {Count}", syncedCount);

                logger.LogInformation(
                    "=== IIChatTools v{Version} готов к работе ({Copyright}) ===",
                    AppVersion.Current, AppVersion.Copyright);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Критическая ошибка инициализации. Приложение будет остановлено.");
                throw;
            }
        }
    }
}