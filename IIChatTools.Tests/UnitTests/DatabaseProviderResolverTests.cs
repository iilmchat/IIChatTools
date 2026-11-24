using System;
using IIChatTools.Services.Utils;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>D07 / KI-242: тесты резолвера провайдера БД.</summary>
    public class DatabaseProviderResolverTests
    {
        [Theory]
        [InlineData("sqlite", "Sqlite")]
        [InlineData(" SQLSERVER ", "SqlServer")]
        [InlineData("inmemory", "InMemory")]
        [InlineData(null, "SqlServer")]
        public void Resolve_KnownProvider_ReturnsCanonical(string raw, string expected)
        {
            Assert.Equal(expected, DatabaseProviderResolver.Resolve(raw));
        }

        [Fact]
        public void Resolve_PostgreSQL_ThrowsWithGuidance()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => DatabaseProviderResolver.Resolve("PostgreSQL"));

            Assert.Contains("Npgsql", ex.Message);
        }

        [Fact]
        public void Resolve_Unknown_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => DatabaseProviderResolver.Resolve("Oracle"));
        }
    }
}
