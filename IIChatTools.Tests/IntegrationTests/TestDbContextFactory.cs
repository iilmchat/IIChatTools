using System;
using IIChatTools.Data;
using Microsoft.EntityFrameworkCore;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Фабрика контекста БД в памяти для интеграционных тестов.
    /// </summary>
    public static class TestDbContextFactory
    {
        /// <summary>
        /// Создаёт изолированный контекст с уникальным именем БД.
        /// </summary>
        /// <returns>Контекст БД</returns>
        public static AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }
    }
}