using System.Collections.Generic;

namespace IIChatTools.Services.DTO.LmStudio
{
    /// <summary>
    /// Ответ LM Studio на запрос <c>POST /v1/embeddings</c>
    /// (v1.5.0, KI-083, Фаза 1).
    ///
    /// <para>
    /// LM Studio возвращает эмбеддинги в формате, совместимом с OpenAI API:
    /// <code>
    /// {
    ///   "data": [
    ///     { "index": 0, "embedding": [0.123, -0.456, ...] },
    ///     { "index": 1, "embedding": [...] }
    ///   ],
    ///   "usage": { "prompt_tokens": 42, "total_tokens": 42 }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    public class EmbeddingResponse
    {
        /// <summary>
        /// Список эмбеддингов (по одному на каждый входной текст).
        /// Порядок соответствует <see cref="EmbeddingData.Index"/>.
        /// </summary>
        public List<EmbeddingData> Data { get; set; } = new List<EmbeddingData>();

        /// <summary>
        /// Информация о расходе токенов (может быть null, если LM Studio не вернул).
        /// </summary>
        public EmbeddingUsage Usage { get; set; }
    }

    /// <summary>
    /// Один эмбеддинг: вектор + позиция во входном списке.
    /// </summary>
    public class EmbeddingData
    {
        /// <summary>
        /// Индекс соответствующего входного текста (0-based).
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Вектор эмбеддинга (размерность = 768 для nomic-embed-text-v1.5).
        /// </summary>
        public float[] Embedding { get; set; }
    }

    /// <summary>
    /// Расход токенов при генерации эмбеддингов.
    /// </summary>
    public class EmbeddingUsage
    {
        /// <summary>
        /// Количество токенов во входных текстах (prompt).
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// Общее количество токенов.
        /// </summary>
        public int TotalTokens { get; set; }
    }
}