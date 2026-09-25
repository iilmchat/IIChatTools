namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Тип источника для индексации документа
    /// (v1.5.0, KI-083, Шаг 4C.1).
    ///
    /// <para>
    /// Определяет, какое из полей <c>IngestionRequest</c>
    /// (<c>FilePath</c> / <c>Text</c> / <c>Url</c>) будет использоваться
    /// сервисом <c>IDocumentIngestionService</c>.
    /// </para>
    /// </summary>
    public enum IngestionSourceType
    {
        /// <summary>
        /// Локальный файл: парсится через <c>IRagDocumentParserRegistry</c>
        /// по расширению. Использует поле <c>IngestionRequest.FilePath</c>.
        /// </summary>
        File = 0,

        /// <summary>
        /// Готовый текст (без парсинга). Использует поле <c>IngestionRequest.Text</c>.
        /// Полезно для тестов и для случаев, когда текст уже извлечён
        /// (например, из БД или из истории чатов).
        /// </summary>
        Text = 1,

        /// <summary>
        /// Веб-URL: контент скачивается через <c>FetchWebContentTool</c>
        /// или аналог. Использует поле <c>IngestionRequest.Url</c>.
        ///
        /// <para>
        /// <b>Не реализовано в MVP</b> — заглушка для будущего расширения.
        /// </para>
        /// </summary>
        Url = 2
    }
}