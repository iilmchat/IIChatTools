using System;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Фабрика контекста БД в памяти для интеграционных тестов.
    /// По умолчанию сидирует трёх тестовых пользователей (Id=1, 2, 3),
    /// чтобы тесты ApprovalService (и подобные) могли ссылаться на реальные FK.
    /// </summary>
    public static class TestDbContextFactory
    {
        /// <summary>
        /// Создаёт изолированный контекст с уникальным именем БД.
        /// </summary>
        /// <param name="seedUsers">
        /// Если true (по умолчанию) — добавляет трёх тестовых пользователей с
        /// детерминированными Id=1, 2, 3. Это требуется для тестов, вызывающих
        /// ApprovalService.CreatePendingActionAsync (проверка существования пользователя).
        /// </param>
        /// <returns>Готовый к использованию контекст БД</returns>
        public static AppDbContext Create(bool seedUsers = true)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();

            if (seedUsers)
            {
                var now = DateTime.UtcNow;
                context.Users.AddRange(
                    CreateUser(1, "test-user-1@local", "Test User 1", now),
                    CreateUser(2, "test-user-2@local", "Test User 2", now),
                    CreateUser(3, "test-user-3@local", "Test User 3", now));
                context.SaveChanges();
            }

            return context;
        }

        /// <summary>
        /// Создаёт тестового пользователя с заданным Id и заполняет
        /// обязательные поля Identity.
        /// </summary>
        /// <param name="id">Идентификатор пользователя</param>
        /// <param name="email">Email (он же UserName)</param>
        /// <param name="fullName">Отображаемое имя</param>
        /// <param name="registeredAt">Дата регистрации (UTC)</param>
        /// <returns>Экземпляр ApplicationUser</returns>
        private static ApplicationUser CreateUser(int id, string email, string fullName, DateTime registeredAt)
        {
            var normalized = email.ToUpperInvariant();
            return new ApplicationUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = normalized,
                Email = email,
                NormalizedEmail = normalized,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                RegisteredAt = registeredAt,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true,
                AccessFailedCount = 0,
                TwoFactorEnabled = false,
                PhoneNumberConfirmed = false
            };
        }
    }
}