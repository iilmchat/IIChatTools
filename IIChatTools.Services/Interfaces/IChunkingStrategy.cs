using System.Collections.Generic;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Стратегия разбиения текста на чанки для RAG
    /// (v1.5.0, KI-083, Шаг 3A).
    ///
    /// <para>
    /// Реализации (Шаги 3B / 3C):
    /// <list type="bullet">
    ///   <item><c>RecursiveChunkingStrategy</c> — по абзацам/предложениям (default).</item>
    ///   <item><c>SentenceChunkingStrategy</c> — по предложениям.</item>
    ///   <item><c>FixedChunkingStrategy</c> — жёстко по токенам (быстро, но режет посреди фразы).</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IChunkingStrategy
    {
        /// <summary>
        /// Имя стратегии (<c>recursive</c> | <c>sentence</c> | <c>fixed</c>).
        /// Совпадает со значением <see cref="ChunkingOptions.Strategy"/>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Разбивает текст на чанки.
        ///
        /// <para>
        /// Возвращает список чанков в исходном порядке. Пустые чанки не возвращаются.
        /// Если текст короче <see cref="ChunkingOptions.ChunkSize"/> — возвращается
        /// один чанк (весь текст).
        /// </para>
        /// </summary>
        /// <param name="text">Исходный текст (не пустой)</param>
        /// <param name="options">Параметры чанкинга (size, overlap, ...)</param>
        /// <param name="tokenCounter">Счётчик токенов (tiktoken)</param>
        /// <returns>Список чанков</returns>
        /// <exception cref="System.ArgumentNullException">
        /// Если <paramref name="text"/>, <paramref name="options"/> или
        /// <paramref name="tokenCounter"/> равны null
        /// </exception>
        IReadOnlyList<string> Chunk(
            string text,
            ChunkingOptions options,
            ITokenCounter tokenCounter);
    }
}