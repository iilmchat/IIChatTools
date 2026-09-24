using System;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// KI-078B: результат поиска по чатам с превью совпадения.
    /// Используется для ⌘K-модалки (Ctrl+K) — отличается от <see cref="ChatListItemDto"/>
    /// наличием сниппета и метаданных о позиции совпадения.
    /// </summary>
    public class ChatSearchResultDto
    {
        /// <summary>Идентификатор чата.</summary>
        public int Id { get; set; }

        /// <summary>Заголовок чата.</summary>
        public string Title { get; set; }

        /// <summary>Идентификатор модели LM Studio.</summary>
        public string Model { get; set; }

        /// <summary>Дата последнего изменения (UTC).</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Поле, в котором найдено совпадение: <c>"title"</c> или <c>"content"</c>.
        /// </summary>
        public string MatchedField { get; set; }

        /// <summary>
        /// Превью совпадения (≈30 символов до + 100 после, с «…» по краям).
        /// <c>null</c>, если совпадение найдено в title (сниппет не нужен).
        /// </summary>
        public string Snippet { get; set; }

        /// <summary>
        /// Позиция начала совпадения в <see cref="Snippet"/> (0-based).
        /// <c>null</c>, если совпадение в title.
        /// </summary>
        public int? SnippetMatchStart { get; set; }

        /// <summary>
        /// Длина совпадения в <see cref="Snippet"/> (в символах).
        /// <c>null</c>, если совпадение в title.
        /// </summary>
        public int? SnippetMatchLength { get; set; }
    }
}