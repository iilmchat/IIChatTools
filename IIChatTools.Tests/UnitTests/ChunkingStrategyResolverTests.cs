using System;
using System.Collections.Generic;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Interfaces;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты резолвера стратегий чанкинга
    /// (v1.5.0, KI-083, Шаг 3C).
    /// </summary>
    public class ChunkingStrategyResolverTests
    {
        /// <summary>Создаёт resolver со всеми 3 стратегиями.</summary>
        private static ChunkingStrategyResolver CreateResolver()
        {
            var strategies = new List<IChunkingStrategy>
            {
                new RecursiveChunkingStrategy(),
                new SentenceChunkingStrategy(),
                new FixedChunkingStrategy()
            };
            return new ChunkingStrategyResolver(strategies);
        }

        /// <summary>
        /// Resolve находит стратегию по имени. Имена — case-insensitive.
        /// Неизвестное имя / null → fallback на "recursive".
        /// </summary>
        [Fact]
        public void Resolve_KnownNames_ReturnsStrategy()
        {
            var resolver = CreateResolver();

            Assert.Equal("recursive", resolver.Resolve("recursive").Name);
            Assert.Equal("sentence", resolver.Resolve("sentence").Name);
            Assert.Equal("fixed", resolver.Resolve("fixed").Name);

            // Case-insensitive.
            Assert.Equal("fixed", resolver.Resolve("FIXED").Name);

            // Fallback на default.
            Assert.Equal("recursive", resolver.Resolve("unknown").Name);
            Assert.Equal("recursive", resolver.Resolve(null).Name);
            Assert.Equal("recursive", resolver.Resolve("").Name);

            // GetDefault.
            Assert.Equal("recursive", resolver.GetDefault().Name);
        }

        /// <summary>
        /// Конструктор бросает, если стратегия "recursive" (default)
        /// отсутствует в коллекции.
        /// </summary>
        [Fact]
        public void Constructor_NoRecursive_Throws()
        {
            var strategies = new List<IChunkingStrategy>
            {
                new SentenceChunkingStrategy()
                // Recursive отсутствует
            };

            Assert.Throws<InvalidOperationException>(
                () => new ChunkingStrategyResolver(strategies));
        }
    }
}