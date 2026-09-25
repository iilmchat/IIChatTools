namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Запрос на индексацию документа для RAG
    /// (v1.5.0, KI-083, Шаг 4C.1).
    ///
    /// <para>
    /// Поля <see cref="FilePath"/>, <see cref="Text"/>, <see cref="Url"/>
    /// взаимоисключающие: используется то, которое соответствует
    /// <see cref="SourceType"/>.
    /// </para>
    /// </summary>
    public class IngestionRequest
    {
        /// <summary>
        /// Имя индекса (<c>project_docs</c>, <c>my_rag_docs</c>,
        /// <c>chat_history</c>, <c>workspace</c>). Обязательное.
        /// </summary>
        public string IndexName { get; set; }

        /// <summary>
        /// Абсолютный путь к локальному файлу.
        /// Используется при <see cref="SourceType"/> = <see cref="IngestionSourceType.File"/>.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Готовый текст (без парсинга).
        /// Используется при <see cref="SourceType"/> = <see cref="IngestionSourceType.Text"/>.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// URL веб-страницы (не реализовано в MVP).
        /// Используется при <see cref="SourceType"/> = <see cref="IngestionSourceType.Url"/>.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Идентификатор чата для индексов <c>my_rag_docs</c> / <c>chat_history</c>.
        /// <c>null</c> — для глобальных (<c>project_docs</c>) и per-user
        /// (<c>chat_history</c>, <c>workspace</c>) индексов.
        /// </summary>
        public int? ChatId { get; set; }

        /// <summary>
        /// Идентификатор пользователя-владельца.
        /// <c>0</c> — для глобальных индексов (<c>project_docs</c>).
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Тип источника. Определяет, какое из полей
        /// (<see cref="FilePath"/>, <see cref="Text"/>, <see cref="Url"/>) использовать.
        /// </summary>
        public IngestionSourceType SourceType { get; set; } = IngestionSourceType.File;

        /// <summary>
        /// Принудительная переиндексация: даже если hash содержимого совпадает
        /// с уже проиндексированным — удалить старые чанки и вставить заново.
        /// <c>false</c> (по умолчанию) — при совпадении hash операция пропускается.
        /// </summary>
        public bool ForceReindex { get; set; }

        /// <summary>
        /// Опциональный «человекочитаемый» источник для citations (KI-086).
        /// Например, <c>docs/development/RULES.md § 4.33</c>.
        /// Если не задан — используется <see cref="FilePath"/> или <see cref="Url"/>.
        /// </summary>
        public string Source { get; set; }
    }
}