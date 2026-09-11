using System;
using IIChatTools.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIChatTools.API.Extensions
{
    /// <summary>
    /// Расширения для выбора провайдера БД на основе конфигурации.
    /// Поддерживаются три режима: SqlServer, Sqlite, InMemory.
    /// </summary>
    public static class DbContextOptionsExtensions
    {
        /// <summary>
        /// Регистрирует AppDbContext с провайдером, выбранным в конфигурации.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        /// <param name="configuration">Конфигурация</param>
        /// <returns>Коллекция сервисов</returns>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        /// <exception cref="InvalidOperationException">Если провайдер не поддерживается</exception>
        public static IServiceCollection AddAppDbContext(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var provider = configuration["Database:Provider"] ?? "SqlServer";

            services.AddDbContext<AppDbContext>(options =>
            {
                switch (provider.ToLowerInvariant())
                {
                    case "sqlserver":
                        options.UseSqlServer(
                            configuration["Database:SqlServerConnectionString"]
                            ?? configuration.GetConnectionString("DefaultConnection"));
                        break;

                    case "sqlite":
                    {
                        var cs = configuration["Database:SqliteConnectionString"]
                            ?? "Data Source=Data/iichattools.db";

                        // Гарантируем существование каталога для файла БД
                        var dataSource = cs.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                        var dir = System.IO.Path.GetDirectoryName(dataSource);
                        if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir);

                        options.UseSqlite(cs);
                        break;
                    }

                    case "inmemory":
                        options.UseInMemoryDatabase(
                            configuration["Database:InMemoryDatabaseName"] ?? "IIChatTools");
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Неподдерживаемый провайдер БД: '{provider}'. " +
                            "Допустимые значения: SqlServer, Sqlite, InMemory.");
                }
            });

            return services;
        }

        /// <summary>
        /// Возвращает признак того, что провайдер поддерживает миграции EF Core.
        /// Для InMemory миграции не применяются.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        /// <returns>true — если нужно применять миграции</returns>
        public static bool ShouldApplyMigrations(this IConfiguration configuration)
        {
            var provider = configuration["Database:Provider"] ?? "SqlServer";
            return !string.Equals(provider, "inmemory", StringComparison.OrdinalIgnoreCase);
        }
    }
}