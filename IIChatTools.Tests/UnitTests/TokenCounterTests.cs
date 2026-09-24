using System.Collections.Generic;
using IIChatTools.Services.Implementation;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты счётчика токенов (v1.4.x, KI-049).
    /// Точные значения зависят от cl100k_base — используем диапазоны.
    /// </summary>
    public class TokenCounterTests
    {
        private static TokenCounter Create() => new TokenCounter();

        /// <summary>Пустая строка → 0 токенов.</summary>
        [Fact]
        public void CountTokens_Empty_ReturnsZero()
        {
            var counter = Create();
            Assert.Equal(0, counter.CountTokens(""));
            Assert.Equal(0, counter.CountTokens(null));
        }

        /// <summary>Одно слово → 1 токен.</summary>
        [Fact]
        public void CountTokens_SingleWord_ReturnsOne()
        {
            var counter = Create();
            var result = counter.CountTokens("hello");
            Assert.Equal(1, result);
        }

        /// <summary>Короткая фраза → 2-4 токена.</summary>
        [Fact]
        public void CountTokens_ShortPhrase_ReturnsReasonable()
        {
            var counter = Create();
            var result = counter.CountTokens("Hello, world!");
            Assert.InRange(result, 2, 5);
        }

        /// <summary>Английский текст → ~1.3 токена на слово.</summary>
        [Fact]
        public void CountTokens_LongText_ReturnsReasonableRatio()
        {
            var counter = Create();
            var text = "The quick brown fox jumps over the lazy dog. " +
                       "This is a longer sentence with more words to test the tokenizer.";
            var result = counter.CountTokens(text);

            // ~20 слов → ~25-35 токенов.
            Assert.InRange(result, 20, 40);
        }

        /// <summary>Кириллица → больше токенов на символ (BPE обучен на английском).</summary>
        [Fact]
        public void CountTokens_Cyrillic_ReturnsNonZero()
        {
            var counter = Create();
            var result = counter.CountTokens("Привет, мир!");
            Assert.True(result > 0);
            // Кириллица обычно даёт больше токенов, чем латиница.
            Assert.InRange(result, 4, 15);
        }

        /// <summary>CountConversation добавляет overhead на каждое сообщение.</summary>
        [Fact]
        public void CountConversation_MultipleMessages_AddsOverhead()
        {
            var counter = Create();
            var messages = new List<(string Role, string Content)>
            {
                ("system", "You are a helpful assistant."),
                ("user", "Hello!"),
                ("assistant", "Hi there!")
            };

            var result = counter.CountConversation(messages);

            // Минимум: 3 * (4 overhead) + токены контента.
            Assert.True(result > 15);
        }

        /// <summary>CountConversation с null → только базовый overhead (3).</summary>
        [Fact]
        public void CountConversation_Null_ReturnsOverheadOnly()
        {
            var counter = Create();
            var result = counter.CountConversation(null);
            Assert.Equal(3, result);
        }
    }
}