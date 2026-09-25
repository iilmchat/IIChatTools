namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Параметры разбиения текста на чанки для RAG
    /// (v1.5.0, KI-083, Шаг 3A).
    ///
    /// <para>
    /// Значения по умолчанию подобраны эмпирически (см. DESIGN § 4.4.1):
    /// <c>ChunkSize=500</c> токенов — оптимум для nomic-embed-text-v1.5
    /// (макс. вход 8192); <c>ChunkOverlap=64</c> — сохраняет семантическую
    /// связность на границах чанков.
    /// </para>
    /// </summary>
    public class ChunkingOptions
    {
        /// <summary>
        /// Целевой размер чанка в токенах (по умолчанию 500).
        /// </summary>
        public int ChunkSize { get; set; } = 500;

        /// <summary>
        /// Перекрытие между соседними чанками в токенах (по умолчанию 64).
        /// Помогает LLM видеть связность при поиске по границе.
        /// </summary>
        public int ChunkOverlap { get; set; } = 64;

        /// <summary>
        /// Минимальный размер чанка в токенах (по умолчанию 100).
        /// Чанки мельче — присоединяются к предыдущему
        /// (см. RecursiveChunkingStrategy.MergeSmallChunks).
        /// </summary>
        public int MinChunkSize { get; set; } = 100;

        /// <summary>
        /// Разделители в порядке приоритета (для Recursive-стратегии).
        /// Первый — самый «грубый» (\n\n — абзацы), последний — «мелкий» (пробел).
        /// </summary>
        public string[] Separators { get; set; } = new[]
        {
            "\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " "
        };

        /// <summary>
        /// Имя стратегии чанкинга (<c>recursive</c> | <c>sentence</c> | <c>fixed</c>).
        /// Читается из конфига (<c>Rag:Chunking:Strategy</c>).
        /// </summary>
        public string Strategy { get; set; } = "recursive";
    }
}