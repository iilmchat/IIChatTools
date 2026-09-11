using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API
{
    /// <summary>
    /// Точка входа в приложение.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Запускает приложение: применяет миграции, проверяет зависимости, инициализирует роли.
        /// </summary>
        /// <param name="args">Аргументы командной строки</param>
        /// <returns>Асинхронная задача</returns>
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    // 1. Проверка внешних зависимостей
                    var checker = services.GetRequiredService<IDependencyChecker>();
                    var deps = await checker.CheckAllAsync();
                    foreach (var dep in deps)
                    {
                        logger.LogInformation(
                            "Зависимость {Name}: {Value}",
                            dep.Key, dep.Value ?? "Не установлена");
                    }

                    // 2. Применение миграций
                    var dbContext = services.GetRequiredService<AppDbContext>();
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Миграции применены успешно");

                    // 3. Инициализация ролей
                    await SeedData.InitializeAsync(services);
                    logger.LogInformation(
                        "Начальные данные инициализированы. Версия: {Version}",
                        AppVersion.Current);
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex,
                        "Критическая ошибка при инициализации. Приложение будет остановлено.");
                    throw;
                }
            }
            AppVersionHolder.Current = AppVersion.Current;
            await host.RunAsync();
        }

        /// <summary>
        /// Создаёт построитель хоста.
        /// </summary>
        /// <param name="args">Аргументы командной строки</param>
        /// <returns>Построитель хоста</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}