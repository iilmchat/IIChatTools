using System;
using System.Collections.Generic;
using IIChatTools.Services.Interfaces;
using Microsoft.ML.Tokenizers;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация счётчика токенов на базе <see cref="TiktokenTokenizer"/>
    /// (v1.4.x, KI-049). Singleton — токенизатор тяжело инициализируется,
    /// потокобезопасен для чтения.
    /// </summary>
    public class TokenCounter : ITokenCounter
    {
        /// <summary>Overhead на одно сообщение в чат-формате (OpenAI-подобный).</summary>
        private const int MessageOverhead = 4;

        /// <summary>Overhead на весь запрос (роль ассистента + priming).</summary>
        private const int ConversationOverhead = 3;

        private readonly TiktokenTokenizer _tokenizer;

        /// <summary>
        /// Создаёт счётчик токенов на базе <c>cl100k_base</c>
        /// (используется GPT-4 / GPT-3.5 — ближайший аналог для Qwen).
        /// </summary>
        public TokenCounter()
        {
            // cl100k_base — универсальный BPE-словарь, встроен в пакет,
            // не требует загрузки внешних файлов.
            _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        }

        /// <inheritdoc />
        public int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return _tokenizer.CountTokens(text);
        }

        /// <inheritdoc />
        public int CountConversation(IEnumerable<(string Role, string Content)> messages)
        {
            if (messages == null)
            {
                return ConversationOverhead;
            }

            var total = ConversationOverhead;

            foreach (var (role, content) in messages)
            {
                total += MessageOverhead;
                total += CountTokens(role);
                total += CountTokens(content);
            }

            return total;
        }
    }
}