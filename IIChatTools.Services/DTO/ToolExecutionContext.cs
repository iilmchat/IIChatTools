using System.Threading;
using System.Threading.Channels;
using IIChatTools.Services.DTO.Chat;

namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Контекст выполнения инструмента (передаётся на каждый вызов).
    /// </summary>
    public class ToolExecutionContext
    {
        /// <summary>
        /// Идентификатор пользователя, инициировавшего вызов.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Абсолютный путь к рабочему пространству пользователя.
        /// </summary>
        public string WorkspaceRoot { get; set; }

        /// <summary>
        /// IP-адрес клиента (для аудита).
        /// </summary>
        public string ClientIp { get; set; }

        /// <summary>
        /// Токен отмены операции.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Идентификатор чата, из которого вызван инструмент
        /// (v1.11.0, KI-126, Шаг 1E).
        ///
        /// <para>
        /// <c>null</c>, если вызов вне чата (например,
        /// <c>/api/tools/execute</c> напрямую). Используется
        /// <c>CodeAgentWithReviewTool</c> для привязки
        /// <c>AgentDebateSession</c> к чату.
        /// </para>
        /// </summary>
        public int? ChatId { get; set; }

        /// <summary>
        /// Краткий снимок последних сообщений чата (KI-111). Передаётся
        /// субагентам через <c>SubAgentTaskRequest.Context</c>, чтобы
        /// mail_agent и др. не делали лишние вызовы для поиска UID/темы
        /// из уже показанных пользователю сообщений.
        /// </summary>
        public string RecentChatContext { get; set; }

        /// <summary>
        /// Опциональный writer для SSE-событий, порождаемых внутри tool'а
        /// (v1.11.0, KI-126, Шаг 1E-part2).
        ///
        /// <para>
        /// Устанавливается <c>ChatStreamService</c> перед вызовом tool'а;
        /// <c>CodeAgentWithReviewTool</c> пишет в него события
        /// <c>debate_started</c> / <c>debate_round</c> / <c>debate_escalated</c>
        /// / <c>debate_completed</c> параллельно с основным выполнением.
        /// </para>
        ///
        /// <para>
        /// <c>null</c> для вызовов вне Chat (например, <c>/api/tools/execute</c>).
        /// Tool'ы должны проверять на <c>null</c> перед записью.
        /// </para>
        /// </summary>
        public ChannelWriter<ChatStreamEvent> EventWriter { get; set; }
    }
}
