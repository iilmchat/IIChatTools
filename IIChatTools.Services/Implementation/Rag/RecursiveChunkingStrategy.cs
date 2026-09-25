using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Рекурсивная стратегия чанкинга (v1.5.0, KI-083, Шаг 3B) — default.
    ///
    /// <para>
    /// Алгоритм (по мотивам LangChain RecursiveCharacterTextSplitter):
    /// <list type="number">
    ///   <item>Если текст укладывается в <c>ChunkSize</c> — вернуть одним чанком.</item>
    ///   <item>Разбить по первому разделителю (<c>\n\n</c> — абзацы).</item>
    ///   <item>Склеивать фрагменты, пока сумма ≤ <c>ChunkSize</c>.</item>
    ///   <item>При превышении — «закрыть» чанк и начать новый с <c>ChunkOverlap</c>
    ///     токенов из предыдущего.</item>
    ///   <item>Если фрагмент сам больше <c>ChunkSize</c> — рекурсивно разбить следующим
    ///     разделителем (<c>\n</c> → <c>. </c> → … → <c> </c>).</item>
    ///   <item>Если разделители закончились — жёсткий разрез по токенам
    ///     (<see cref="SplitByTokens"/>).</item>
    ///   <item>Финально: чанки &lt; <c>MinChunkSize</c> — присоединить к предыдущему
    ///     (<see cref="MergeSmallChunks"/>).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Не является <c>Singleton</c> на данном шаге — стратегия без состояния,
    /// но регистрация в DI будет в Шаге 3C (вместе с resolver'ом).
    /// </para>
    /// </summary>
    public sealed class RecursiveChunkingStrategy : IChunkingStrategy
    {
        /// <inheritdoc />
        public string Name => "recursive";

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

            var separators = options.Separators ?? Array.Empty<string>();
            var chunks = ChunkRecursive(text, options, tokenCounter, separators);
            return MergeSmallChunks(chunks, options, tokenCounter);
        }

        /// <summary>
        /// Рекурсивное разбиение текста на чанки.
        /// </summary>
        /// <param name="text">Исходный текст (не пустой)</param>
        /// <param name="options">Параметры чанкинга</param>
        /// <param name="counter">Счётчик токенов</param>
        /// <param name="separators">Разделители в порядке убывания «грубости»</param>
        /// <returns>Список чанков (до MergeSmallChunks)</returns>
        private static List<string> ChunkRecursive(
            string text,
            ChunkingOptions options,
            ITokenCounter counter,
            IReadOnlyList<string> separators)
        {
            // Base case: текст уже укладывается в ChunkSize.
            if (counter.CountTokens(text) <= options.ChunkSize)
                return new List<string> { text };

            // Разделители закончились — жёсткий разрез по токенам.
            if (separators == null || separators.Count == 0)
                return SplitByTokens(text, options, counter);

            var sep = separators[0];
            if (string.IsNullOrEmpty(sep))
            {
                // Пустой разделитель — пропускаем, идём к следующему.
                return separators.Count > 1
                    ? ChunkRecursive(text, options, counter, Skip(separators, 1))
                    : SplitByTokens(text, options, counter);
            }

            var remaining = separators.Count > 1 ? Skip(separators, 1) : Array.Empty<string>();
            var parts = text.Split(new[] { sep }, StringSplitOptions.None);

            var chunks = new List<string>();
            var current = string.Empty;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                var candidate = string.IsNullOrEmpty(current)
                    ? part
                    : current + sep + part;

                if (counter.CountTokens(candidate) <= options.ChunkSize)
                {
                    current = candidate;
                    continue;
                }

                // Превышение: закрываем текущий чанк.
                if (!string.IsNullOrEmpty(current))
                {
                    chunks.Add(current);

                    // Новый чанк начинается с overlap-хвоста предыдущего + part.
                    var overlapText = TakeLastTokens(current, options.ChunkOverlap, counter);
                    current = string.IsNullOrEmpty(overlapText)
                        ? part
                        : overlapText + sep + part;

                    // Если overlap + part всё ещё больше ChunkSize — рекурсивно
                    // разбиваем это следующим разделителем.
                    if (counter.CountTokens(current) > options.ChunkSize)
                    {
                        chunks.AddRange(ChunkRecursive(current, options, counter, remaining));
                        current = string.Empty;
                    }
                }
                else
                {
                    // part сам больше ChunkSize — рекурсия со следующим разделителем.
                    chunks.AddRange(ChunkRecursive(part, options, counter, remaining));
                }
            }

            if (!string.IsNullOrEmpty(current))
                chunks.Add(current);

            return chunks;
        }

        /// <summary>
        /// Жёсткий разрез текста по границам токенов (fallback, когда разделители
        /// закончились или не помогают). Режет ровно по <c>ChunkSize</c> токенов
        /// с перекрытием <c>ChunkOverlap</c>.
        /// </summary>
        /// <param name="text">Текст</param>
        /// <param name="options">Параметры</param>
        /// <param name="counter">Счётчик токенов</param>
        /// <returns>Список чанков</returns>
        private static List<string> SplitByTokens(
            string text,
            ChunkingOptions options,
            ITokenCounter counter)
        {
            var tokens = counter.Encode(text);
            if (tokens.Count == 0)
                return new List<string>();

            // Шаг: ChunkSize - Overlap. Минимум 1, чтобы не зациклиться.
            var step = Math.Max(1, options.ChunkSize - options.ChunkOverlap);

            var result = new List<string>();
            var start = 0;

            while (start < tokens.Count)
            {
                var take = Math.Min(options.ChunkSize, tokens.Count - start);
                var slice = new List<int>(take);
                for (int i = 0; i < take; i++)
                    slice.Add(tokens[start + i]);

                result.Add(counter.Decode(slice));
                start += step;
            }

            return result;
        }

        /// <summary>
        /// Возвращает последние <paramref name="count"/> токенов текста
        /// (декодированные обратно в строку). Если в тексте меньше токенов —
        /// возвращает текст целиком.
        /// </summary>
        /// <param name="text">Текст</param>
        /// <param name="count">Сколько токенов взять с конца</param>
        /// <param name="counter">Счётчик токенов</param>
        /// <returns>Хвост текста (пустая строка при count ≤ 0)</returns>
        private static string TakeLastTokens(string text, int count, ITokenCounter counter)
        {
            if (count <= 0 || string.IsNullOrEmpty(text))
                return string.Empty;

            var tokens = counter.Encode(text);
            if (tokens.Count <= count)
                return text;

            var slice = new List<int>(count);
            for (int i = tokens.Count - count; i < tokens.Count; i++)
                slice.Add(tokens[i]);

            return counter.Decode(slice);
        }

        /// <summary>
        /// Присоединяет чанки &lt; <c>MinChunkSize</c> к предыдущему.
        /// Пустые/пробельные чанки отбрасываются (в отличие от мелких).
        ///
        /// <para>
        /// Слияние — «best effort»: если предыдущий чанк уже ~<c>ChunkSize</c>,
        /// результат может слегка превысить <c>ChunkSize</c>. Это осознанный
        /// компромисс (лучше сохранить весь контент, чем потерять данные).
        /// </para>
        /// </summary>
        /// <param name="chunks">Чанки после рекурсивного разбиения</param>
        /// <param name="options">Параметры (MinChunkSize)</param>
        /// <param name="counter">Счётчик токенов</param>
        /// <returns>Список чанков с «дотянутыми» мелкими</returns>
        private static List<string> MergeSmallChunks(
            List<string> chunks,
            ChunkingOptions options,
            ITokenCounter counter)
        {
            if (chunks == null || chunks.Count == 0)
                return new List<string>();

            if (chunks.Count == 1)
                return chunks;

            var result = new List<string>();

            foreach (var chunk in chunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                var tokens = counter.CountTokens(chunk);

                if (tokens < options.MinChunkSize && result.Count > 0)
                {
                    // Присоединяем к предыдущему через \n\n (сохраняем абзацную семантику).
                    result[result.Count - 1] = result[result.Count - 1] + "\n\n" + chunk;
                }
                else
                {
                    result.Add(chunk);
                }
            }

            return result;
        }

        /// <summary>
        /// Возвращает «хвост» списка, начиная с индекса <paramref name="start"/>.
        /// Без LINQ — горячий путь.
        /// </summary>
        private static IReadOnlyList<string> Skip(IReadOnlyList<string> source, int start)
        {
            if (start >= source.Count)
                return Array.Empty<string>();

            var result = new string[source.Count - start];
            for (int i = start; i < source.Count; i++)
                result[i - start] = source[i];
            return result;
        }
    }
}