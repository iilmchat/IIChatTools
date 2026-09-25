using System.Linq;
using System.Text;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты стратегии чанкинга по предложениям
    /// (v1.5.0, KI-083, Шаг 3C).
    /// </summary>
    public class SentenceChunkingStrategyTests
    {
        private static readonly TokenCounter Counter = new TokenCounter();
        private static readonly SentenceChunkingStrategy Strategy = new SentenceChunkingStrategy();

        /// <summary>
        /// Текст из нескольких предложений разбивается на ≥1 чанк,
        /// каждый чанк содержит целые предложения.
        /// </summary>
        [Fact]
        public void Chunk_SplitsBySentences()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 20; i++)
                sb.Append($"This is sentence number {i} with several words. ");
            var text = sb.ToString();

            var options = new ChunkingOptions
            {
                ChunkSize = 30,
                ChunkOverlap = 0,
                MinChunkSize = 1
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.True(chunks.Count > 1, "Длинный текст должен дать несколько чанков");

            // Каждый чанк содержит хотя бы одно законченное предложение (точка).
            foreach (var chunk in chunks)
            {
                Assert.Contains(".", chunk);
            }
        }

        /// <summary>
        /// Несколько коротких предложений, укладывающихся в ChunkSize,
        /// объединяются в один чанк.
        /// </summary>
        [Fact]
        public void Chunk_GroupsUntilLimit()
        {
            // 3 коротких предложения — суммарно меньше ChunkSize.
            var text = "First short. Second short. Third short.";

            var options = new ChunkingOptions
            {
                ChunkSize = 100,   // заведомо больше суммарного размера
                ChunkOverlap = 0,
                MinChunkSize = 1
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.Single(chunks);
            Assert.Contains("First short", chunks[0]);
            Assert.Contains("Third short", chunks[0]);
        }
    }
}