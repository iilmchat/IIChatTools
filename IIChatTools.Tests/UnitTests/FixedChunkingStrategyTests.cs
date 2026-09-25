using System.Linq;
using System.Text;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты фиксированной стратегии чанкинга по токенам
    /// (v1.5.0, KI-083, Шаг 3C).
    /// </summary>
    public class FixedChunkingStrategyTests
    {
        private static readonly TokenCounter Counter = new TokenCounter();
        private static readonly FixedChunkingStrategy Strategy = new FixedChunkingStrategy();

        /// <summary>
        /// Короткий текст (≤ ChunkSize) — один чанк.
        /// Длинный текст — режется на чанки, каждый ≤ ChunkSize токенов.
        /// </summary>
        [Fact]
        public void Chunk_ExactTokenCount()
        {
            // Короткий текст → 1 чанк.
            var shortText = "Hello, world!";
            var shortChunks = Strategy.Chunk(shortText, new ChunkingOptions
            {
                ChunkSize = 100,
                ChunkOverlap = 0
            }, Counter);
            Assert.Single(shortChunks);

            // Длинный текст → несколько чанков.
            var sb = new StringBuilder();
            for (int i = 0; i < 200; i++)
                sb.Append($"word{i} ");
            var longText = sb.ToString();

            var options = new ChunkingOptions
            {
                ChunkSize = 50,
                ChunkOverlap = 0
            };

            var chunks = Strategy.Chunk(longText, options, Counter);

            Assert.True(chunks.Count > 1);

            // Каждый чанк ≤ ChunkSize (с запасом на декодирование +5).
            foreach (var chunk in chunks)
            {
                var tokens = Counter.CountTokens(chunk);
                Assert.True(
                    tokens <= options.ChunkSize + 5,
                    $"Чанк содержит {tokens} токенов — превышает {options.ChunkSize + 5}");
            }
        }

        /// <summary>
        /// При Overlap &gt; 0 суммарное число токенов по чанкам больше,
        /// чем в исходном тексте (эффект перекрытия).
        /// </summary>
        [Fact]
        public void Chunk_OverlapCorrect()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 200; i++)
                sb.Append($"token{i} ");
            var text = sb.ToString();

            var options = new ChunkingOptions
            {
                ChunkSize = 50,
                ChunkOverlap = 20
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.True(chunks.Count > 1);

            var totalChunkTokens = chunks.Sum(c => Counter.CountTokens(c));
            var originalTokens = Counter.CountTokens(text);

            Assert.True(
                totalChunkTokens > originalTokens,
                $"Суммарно по чанкам {totalChunkTokens} должно быть больше " +
                $"оригинала {originalTokens} из-за overlap");
        }
    }
}