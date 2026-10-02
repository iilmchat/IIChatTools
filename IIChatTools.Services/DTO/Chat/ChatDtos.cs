using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Краткая информация о чате для списка в sidebar.
    /// KI-078B: при поиске (GET /api/chats?search=) заполняются дополнительные
    /// поля для превью совпадения в ⌘K-модалке (Ctrl+K).
    /// </summary>
    public class ChatListItemDto
    {
        /// <summary>Идентификатор чата.</summary>
        public int Id { get; set; }

        /// <summary>Заголовок чата.</summary>
        public string Title { get; set; }

        /// <summary>Идентификатор модели LM Studio.</summary>
        public string Model { get; set; }

        /// <summary>Дата последнего изменения.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Количество сообщений в чате.</summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// KI-078B: поле, в котором найдено совпадение (<c>"title"</c> / <c>"content"</c>).
        /// <c>null</c>, если <c>search</c> не был указан.
        /// </summary>
        public string MatchedField { get; set; }

        /// <summary>
        /// KI-078B: превью совпадения (≈150 символов, с «…» по краям).
        /// <c>null</c>, если поиск не выполнялся или совпадение в title.
        /// </summary>
        public string Snippet { get; set; }

        /// <summary>
        /// KI-078B: позиция совпадения в <see cref="Snippet"/> (0-based).
        /// <c>null</c>, если поиск не выполнялся или совпадение в title.
        /// </summary>
        public int? SnippetMatchStart { get; set; }

        /// <summary>
        /// KI-078B: длина совпадения в <see cref="Snippet"/>.
        /// <c>null</c>, если поиск не выполнялся или совпадение в title.
        /// </summary>
        public int? SnippetMatchLength { get; set; }
    }

    /// <summary>
    /// Детальная информация о чате с историей сообщений.
    /// </summary>
    public class ChatDetailDto
    {
        /// <summary>Идентификатор чата.</summary>
        public int Id { get; set; }

        /// <summary>Заголовок чата.</summary>
        public string Title { get; set; }

        /// <summary>Идентификатор модели LM Studio.</summary>
        public string Model { get; set; }

        /// <summary>Системный промпт (опционально).</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Дата последнего изменения.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>История сообщений (сортировка по CreatedAt asc).</summary>
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();

        /// <summary>
        /// v1.11.0 (KI-129): все сессии Actor-Critic этого чата —
        /// для восстановления блоков дебатов при F5. Сортировка
        /// <c>StartedAt asc</c>. Пустой список, если дебатов не было.
        /// </summary>
        public IReadOnlyList<DTO.Debate.AgentDebateStatusDto> DebateSessions { get; set; }
            = new List<DTO.Debate.AgentDebateStatusDto>();
    }

    /// <summary>
    /// Сообщение чата для API.
    /// </summary>
    public class ChatMessageDto
    {
        /// <summary>Идентификатор сообщения.</summary>
        public int Id { get; set; }

        /// <summary>Роль: <c>user</c>, <c>assistant</c>, <c>system</c>, <c>tool</c>.</summary>
        public string Role { get; set; }

        /// <summary>Текстовое содержимое.</summary>
        public string Content { get; set; }

        /// <summary>
        /// Tool calls (если assistant вызвал инструменты).
        /// JSON-строка формата OpenAI.
        /// </summary>
        public string ToolCallsJson { get; set; }

        /// <summary>ID tool call (для role="tool").</summary>
        public string ToolCallId { get; set; }

        /// <summary>Имя инструмента (для role="tool").</summary>
        public string ToolName { get; set; }

        /// <summary>Дата создания сообщения.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// KI-049b: количество входных токенов (prompt / контекст).
        /// <c>null</c> — не подсчитано.
        /// </summary>
        public int? TokensIn { get; set; }

        /// <summary>
        /// KI-049b: количество выходных токенов (completion).
        /// <c>null</c> — не подсчитано (для user/tool).
        /// </summary>
        public int? TokensOut { get; set; }

        /// <summary>
        /// KI-084a: общая длительность генерации (мс).
        /// </summary>
        public long? DurationMs { get; set; }

        /// <summary>
        /// KI-084a: время до первого delta (мс).
        /// </summary>
        public long? FirstTokenMs { get; set; }

        /// <summary>
        /// KI-084a: причина завершения (<c>stop</c>, <c>length</c>, <c>tool_calls</c>).
        /// </summary>
        public string FinishReason { get; set; }

        /// <summary>
        /// v1.6.0 (KI-086): источники под ответом ассистента (структурированный
        /// массив, а не сырой JSON). Парсится из <c>ChatMessage.MetadataJson</c>
        /// на бэкенде — в <c>ChatController.GetChatAsync</c> (API-слой).
        /// <c>null</c> — источников нет (user/tool/старые assistant-сообщения).
        /// </summary>
        public IReadOnlyList<ChatSourceDto> Sources { get; set; }

        /// <summary>
        /// v1.6.0 (KI-086): сырой JSON метаданных (<c>{ "sources": [...] }</c>).
        /// Оставлено для отладки / API-совместимости. UI использует <see cref="Sources"/>.
        /// <c>null</c> — метаданных нет.
        /// </summary>
        public string MetadataJson { get; set; }

        /// <summary>
        /// v1.11.0 (KI-129): для tool-сообщений с
        /// <c>toolName == "code_agent_with_review"</c> — идентификатор
        /// соответствующей debate-сессии (парсится из <c>MetadataJson</c>).
        /// UI использует для маппинга <c>toolCallId → sessionId</c>
        /// при восстановлении блоков дебатов после F5.
        /// <c>null</c> для остальных сообщений.
        /// </summary>
        public int? DebateSessionId { get; set; }
    }

    /// <summary>
    /// Запрос на создание нового чата.
    /// </summary>
    public class CreateChatRequest
    {
        /// <summary>Идентификатор модели LM Studio (обязательно).</summary>
        public string Model { get; set; }

        /// <summary>Опциональный заголовок чата. По умолчанию — «Новый чат».</summary>
        public string Title { get; set; }
    }

    /// <summary>
    /// Запрос на обновление метаданных чата.
    /// Поля с <c>null</c> не изменяются.
    /// </summary>
    public class UpdateChatRequest
    {
        /// <summary>Новый заголовок (null = не менять).</summary>
        public string Title { get; set; }

        /// <summary>Новая модель (null = не менять).</summary>
        public string Model { get; set; }

        /// <summary>Новый системный промпт (null = не менять).</summary>
        public string SystemPrompt { get; set; }
    }
}