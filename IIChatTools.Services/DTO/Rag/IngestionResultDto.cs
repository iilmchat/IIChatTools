namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Результат индексации документа
    /// (v1.5.0, KI-083, Шаг 4C.1).
    /// </summary>
    public class IngestionResultDto
    {
        /// <summary>
        /// Имя индекса, в который произведена индексация.
        /// </summary>
        public string IndexName { get; set; }

        /// <summary>
        /// Количество созданных чанков (0 при <see cref="Skipped"/> = true).
        /// </summary>
        public int DocumentChunksCreated { get; set; }

        /// <summary>
        /// Суммарное количество токенов во всех чанках документа.
        /// </summary>
        public int TokensTotal { get; set; }

        /// <summary>
        /// Длительность операции (мс).
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// SHA256-хэш содержимого документа (hex, нижний регистр).
        /// Используется для skip при повторной индексации.
        /// </summary>
        public string DocumentHash { get; set; }

        /// <summary>
        /// Относительный путь к документу (или URL) — для идентификации.
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// <c>true</c>, если индексация была пропущена (совпадение hash
        /// при <c>ForceReindex = false</c>).
        /// </summary>
        public bool Skipped { get; set; }

        /// <summary>
        /// Причина пропуска (для логов/UI). <c>null</c>, если <see cref="Skipped"/> = false.
        /// </summary>
        public string SkipReason { get; set; }
    }
}