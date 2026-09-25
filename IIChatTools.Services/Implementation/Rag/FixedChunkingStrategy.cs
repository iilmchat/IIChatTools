using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Жёсткая стратегия чанкинга по токенам (v1.5.0, KI-083, Шаг 3C).
    ///
    /// <para>
    /// Режет текст ровно по <see cref="ChunkingOptions.ChunkSize"/> токенов
    /// (без учёта границ предложений/абзацев). Overlap реализован шагом
    /// <c>ChunkSize - ChunkOverlap</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Плюсы:</b> предсказуемый размер, O(N). <br/>
    /// <b>Минусы:</b> может разрезать слово/предложение посередине.
    /// Используется как fallback, когда <c>Recursive</c> не подходит
    /// (например, для очень однородных текстов вроде логов).
    /// </para>
    /// </summary>
    public sealed class FixedChunkingStrategy : IChunkingStrategy
    {
        /// <inheritdoc />
        public string Name => "fixed";

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

            var tokens = tokenCounter.Encode(text);
            if (tokens.Count == 0)
                return Array.Empty<string>();

            // Шаг: ChunkSize - Overlap. Минимум 1, чтобы не зациклиться.
            var step = Math.Max(1, options.ChunkSize - options.ChunkOverlap);

            var result = new List<string>();

            for (int start = 0; start < tokens.Count; start += step)
            {
                var take = Math.Min(options.ChunkSize, tokens.Count - start);
                if (take <= 0)
                    break;

                var slice = new int[take];
                for (int i = 0; i < take; i++)
                    slice[i] = tokens[start + i];

                result.Add(tokenCounter.Decode(slice));

                // Если дошли до конца — больше не нужно.
                if (start + options.ChunkSize >= tokens.Count)
                    break;
            }

            return result;
        }
    }
}