using System;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Data
{
    /// <summary>
    /// Класс инициализации начальных данных БД.
    /// Создаёт роли Admin и User. Настройки по умолчанию загружаются из appsettings.
    /// </summary>
    public static class SeedData
    {
        /// <summary>
        /// Имя роли администратора.
        /// </summary>
        public const string RoleAdmin = "Admin";

        /// <summary>
        /// Имя роли обычного пользователя.
        /// </summary>
        public const string RoleUser = "User";

        /// <summary>
        /// Выполняет инициализацию ролей.
        /// Первый зарегистрированный пользователь получает роль Admin (см. AuthController).
        /// </summary>
        /// <param name="serviceProvider">Провайдер служб</param>
        /// <returns>Асинхронная задача</returns>
        /// <exception cref="ArgumentNullException">Если serviceProvider равен null</exception>
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

            await EnsureRoleAsync(roleManager, RoleAdmin, logger);
            await EnsureRoleAsync(roleManager, RoleUser, logger);
        }

        /// <summary>
        /// Проверяет существование роли и создаёт её при необходимости.
        /// </summary>
        /// <param name="roleManager">Менеджер ролей</param>
        /// <param name="roleName">Имя роли</param>
        /// <param name="logger">Логгер</param>
        /// <returns>Асинхронная задача</returns>
        private static async Task EnsureRoleAsync(
            RoleManager<IdentityRole<int>> roleManager,
            string roleName,
            ILogger logger)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                return;

            var result = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
            if (result.Succeeded)
                logger.LogInformation("Роль {RoleName} успешно создана", roleName);
            else
                logger.LogError(
                    "Не удалось создать роль {RoleName}: {Errors}",
                    roleName,
                    string.Join("; ", result.Errors));
        }
    }
}