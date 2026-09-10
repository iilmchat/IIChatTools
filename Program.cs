using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            // Применяем миграции и создаём БД при старте
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var dbContext = services.GetRequiredService<AppDbContext>();
                    await dbContext.Database.MigrateAsync();

                    // Проверка зависимостей
                    var checker = services.GetRequiredService<IDependencyChecker>();
                    var deps = await checker.CheckAllAsync();
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    foreach (var dep in deps)
                    {
                        logger.LogInformation("{Key}: {Value}", dep.Key, dep.Value ?? "Не установлен");
                    }
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Ошибка при инициализации БД или проверке зависимостей");
                }
            }

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}