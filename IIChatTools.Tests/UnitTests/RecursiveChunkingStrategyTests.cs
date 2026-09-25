using System.Linq;
using System.Text;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты рекурсивной стратегии чанкинга
    /// (v1.5.0, KI-083, Шаг 3B).
    ///
    /// <para>
    /// DESIGN § 4.4.5 — 6 тестов. TokenCounter (cl100k_base) даёт
    /// приближение (±5-10%), тесты используют диапазоны, а не точные числа.
    /// </para>
    /// </summary>
    public class RecursiveChunkingStrategyTests
    {
        private static readonly TokenCounter Counter = new TokenCounter();
        private static readonly RecursiveChunkingStrategy Strategy = new RecursiveChunkingStrategy();

        /// <summary>
        /// Короткий текст → один чанк без изменений.
        /// </summary>
        [Fact]
        public void Chunk_SmallText_ReturnsOneChunk()
        {
            var text = "Hello, world!";
            var options = new ChunkingOptions { ChunkSize = 500, ChunkOverlap = 64 };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.Single(chunks);
            Assert.Equal(text, chunks[0]);
        }

        /// <summary>
        /// Пустой / null / пробельный текст → пустой список.
        /// </summary>
        [Fact]
        public void Chunk_EmptyText_ReturnsEmpty()
        {
            var options = new ChunkingOptions();

            Assert.Empty(Strategy.Chunk("", options, Counter));
            Assert.Empty(Strategy.Chunk(null, options, Counter));
            Assert.Empty(Strategy.Chunk("   \n\n\t  ", options, Counter));
        }

        /// <summary>
        /// Длинный текст → несколько чанков; каждый укладывается
        /// в ChunkSize (с небольшим запасом на overlap/merge).
        /// </summary>
        [Fact]
        public void Chunk_LongText_RespectsChunkSize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 30; i++)
                sb.Append($"Sentence number {i} contains several words. ");
            var text = sb.ToString();

            var options = new ChunkingOptions
            {
                ChunkSize = 50,
                ChunkOverlap = 10,
                MinChunkSize = 5
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.True(chunks.Count > 1, "Длинный текст должен разбиться на несколько чанков");

            // Верхняя граница: ChunkSize с запасом 50% (overlap/merge могут
            // слегка «переполнить» последний чанк).
            foreach (var chunk in chunks)
            {
                var tokens = Counter.CountTokens(chunk);
                Assert.True(
                    tokens <= options.ChunkSize * 3 / 2,
                    $"Чанк содержит {tokens} токенов — превышает {options.ChunkSize * 3 / 2}");
            }
        }

        /// <summary>
        /// При разбиении с overlap суммарное число токенов по чанкам
        /// больше, чем в исходном тексте (эффект перекрытия).
        /// </summary>
        [Fact]
        public void Chunk_OverlapIsApplied()
        {
            // Текст из коротких «абзацев» по одному слову.
            var sb = new StringBuilder();
            for (int i = 0; i < 40; i++)
                sb.Append($"word{i}\n");
            var text = sb.ToString();

            var options = new ChunkingOptions
            {
                ChunkSize = 20,
                ChunkOverlap = 5,
                MinChunkSize = 3,
                Separators = new[] { "\n", " " }
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.True(chunks.Count > 1, "Должно быть несколько чанков");

            var totalChunkTokens = chunks.Sum(c => Counter.CountTokens(c));
            var originalTokens = Counter.CountTokens(text);

            Assert.True(
                totalChunkTokens > originalTokens,
                $"Total chunk tokens ({totalChunkTokens}) должно быть больше " +
                $"original ({originalTokens}) из-за overlap");
        }

        /// <summary>
        /// Разбиение предпочитает абзацы (по <c>\n\n</c>): каждый абзац
        /// образует отдельный чанк (без overlap — для точной проверки).
        ///
        /// <para>
        /// ChunkSize подобран динамически: <c>pTokens * 1.2</c>. Тогда
        /// один абзац (&lt;= pTokens) влезает, а два (= 2·pTokens) — нет.
        /// Так тест не зависит от точной токенизации cl100k на английском
        /// (детерминированно, без «магических» констант).
        /// </para>
        /// </summary>
        [Fact]
        public void Chunk_ParagraphsSplitPreferred()
        {
            // Один и тот же текст — три абзаца (одинаковые размеры → детерминизм).
            const string p =
                "Lorem ipsum dolor sit amet consectetur adipiscing elit " +
                "sed do eiusmod tempor incididunt ut labore et dolore " +
                "magna aliqua Ut enim ad minim veniam quis nostrud";

            var text = p + "\n\n" + p + "\n\n" + p;

            var pTokens = Counter.CountTokens(p);
            // ChunkSize = pTokens + 20% → 1×p абзац влезает, 2×p — нет.
            var chunkSize = pTokens + (pTokens / 5);

            var options = new ChunkingOptions
            {
                ChunkSize = chunkSize,
                ChunkOverlap = 0,
                MinChunkSize = 1,
                Separators = new[] { "\n\n", "\n", ". ", " " }
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            Assert.Equal(3, chunks.Count);

            // Каждый чанк — отдельный абзац (без «\n\n» внутри).
            Assert.All(chunks, c =>
            {
                Assert.Contains("Lorem ipsum", c);
                Assert.DoesNotContain("\n\n", c);
            });
        }

        /// <summary>
        /// Мелкие чанки (&lt; MinChunkSize) присоединяются к предыдущему.
        /// </summary>
        [Fact]
        public void Chunk_MinChunkSize_MergesSmall()
        {
            // 3 абзаца: p1 (28 токенов), p2 (1 токен — «tiny»), p3 (28 токенов).
            // ChunkSize = 30 → p2 не влезает ни к p1, ни к p3 → отдельный чанк.
            // MinChunkSize = 15 → p2 должен быть присоединён к p1 (после merge).
            var p1 = "word01 word02 word03 word04 word05 word06 word07 word08 word09 word10 word11 word12 word13 word14";
            var p2 = "tiny";
            var p3 = "word21 word22 word23 word24 word25 word26 word27 word28 word29 word30 word31 word32 word33 word34";

            var text = p1 + "\n\n" + p2 + "\n\n" + p3;

            var options = new ChunkingOptions
            {
                ChunkSize = 30,
                ChunkOverlap = 0,
                MinChunkSize = 15,
                Separators = new[] { "\n\n" }
            };

            var chunks = Strategy.Chunk(text, options, Counter);

            // p2 не должен стать отдельным чанком — он мелкий и присоединён.
            Assert.True(chunks.Count < 3, "Мелкий чанк должен быть присоединён к предыдущему");
            Assert.Contains(chunks, c => c.Contains("tiny"));
        }
    }
}