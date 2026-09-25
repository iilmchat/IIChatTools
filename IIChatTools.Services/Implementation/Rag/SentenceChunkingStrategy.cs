using System;
using System.Collections.Generic;
using System.Text;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Стратегия чанкинга по предложениям (v1.5.0, KI-083, Шаг 3C).
    ///
    /// <para>
    /// Разбивает текст на предложения (по <c>. ! ? \n</c>), затем группирует
    /// их в чанки до <see cref="ChunkingOptions.ChunkSize"/> токенов.
    /// Overlap — последнее предложение предыдущего чанка повторяется в начале
    /// следующего (сохраняет связность).
    /// </para>
    ///
    /// <para>
    /// Отличие от <see cref="RecursiveChunkingStrategy"/>: не пытается
    /// «уложить абзац целиком» — работает на уровне предложений.
    /// Полезно для текстов с короткими предложениями (диалоги, FAQ).
    /// </para>
    /// </summary>
    public sealed class SentenceChunkingStrategy : IChunkingStrategy
    {
        /// <inheritdoc />
        public string Name => "sentence";

        /// <inheritdoc />
        public IReadOnlyList<string> Chunk(
            string text,
            ChunkingOptions options,
            ITokenCounter tokenCounter)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (tokenCounter == null) throw new ArgumentNullException(nameof(tokenCounter));

            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            var sentences = SplitIntoSentences(text);
            if (sentences.Count == 0)
                return Array.Empty<string>();

            return ChunkSentences(sentences, options, tokenCounter);
        }

        /// <summary>
        /// Разбивает текст на предложения. Границы: <c>. ! ? \n</c>.
        /// Разделитель остаётся в конце предложения.
        /// </summary>
        /// <param name="text">Текст</param>
        /// <returns>Список предложений (без пустых)</returns>
        private static List<string> SplitIntoSentences(string text)
        {
            var result = new List<string>();
            var current = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                current.Append(ch);

                if (ch == '.' || ch == '!' || ch == '?' || ch == '\n')
                {
                    var sentence = current.ToString().Trim();
                    if (sentence.Length > 0)
                        result.Add(sentence);
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                    result.Add(sentence);
            }

            return result;
        }

        /// <summary>
        /// Группирует предложения в чанки до <see cref="ChunkingOptions.ChunkSize"/> токенов.
        /// Overlap: последнее предложение предыдущего чанка идёт в начало следующего.
        /// </summary>
        /// <param name="sentences">Список предложений</param>
        /// <param name="options">Параметры чанкинга</param>
        /// <param name="counter">Счётчик токенов</param>
        /// <returns>Список чанков</returns>
        private static List<string> ChunkSentences(
            List<string> sentences,
            ChunkingOptions options,
            ITokenCounter counter)
        {
            var chunks = new List<string>();
            var current = new List<string>();
            var currentText = string.Empty;

            foreach (var sentence in sentences)
            {
                var candidate = currentText.Length == 0
                    ? sentence
                    : currentText + " " + sentence;

                if (counter.CountTokens(candidate) <= options.ChunkSize)
                {
                    current.Add(sentence);
                    currentText = candidate;
                    continue;
                }

                // Превышение: закрываем текущий чанк.
                if (current.Count > 0)
                {
                    chunks.Add(currentText);

                    // Начинаем новый чанк с последнего предложения (overlap) + текущего.
                    var lastSentence = current[current.Count - 1];
                    current.Clear();
                    current.Add(lastSentence);
                    current.Add(sentence);
                    currentText = lastSentence + " " + sentence;

                    // Если даже с одним предложением-хвостом не влезает — отдаём как есть.
                    if (counter.CountTokens(currentText) > options.ChunkSize)
                    {
                        chunks.Add(currentText);
                        current.Clear();
                        currentText = string.Empty;
                    }
                }
                else
                {
                    // Одно предложение больше ChunkSize — кладём как есть.
                    chunks.Add(sentence);
                }
            }

            if (current.Count > 0)
                chunks.Add(currentText);

            return chunks;
        }
    }
}