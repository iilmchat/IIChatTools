namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Унифицированный результат веб-поиска (Wikipedia / DuckDuckGo /
    /// FetchWebContent) для построения citations
    /// (v1.6.1, KI-086-post).
    ///
    /// <para>
    /// Промежуточная структура между Web-инструментами и
    /// <c>WebSourceBuilder</c> — избегаем дублирования логики преобразования
    /// в <see cref="DTO.Chat.ChatSourceDto"/> в каждом из трёх инструментов.
    /// </para>
    /// </summary>
    public class RetrievedWebResult
    {
        /// <summary>
        /// Заголовок (название статьи Wikipedia, title из DuckDuckGo, <c>&lt;title&gt;</c> страницы).
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// URL источника (для кликабельной ссылки в UI).
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Краткое описание (≤ 200 символов при передаче в UI).
        /// </summary>
        public string Snippet { get; set; }
    }
}