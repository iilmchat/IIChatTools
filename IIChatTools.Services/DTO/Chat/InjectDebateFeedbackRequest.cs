namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Запрос Human-in-the-loop feedback для Actor-Critic сессии
    /// (v1.11.0, KI-126, Шаг 1E-part2).
    ///
    /// <para>
    /// Используется endpoint'ом
    /// <c>POST /api/chat/debate/{sessionId}/inject</c>.
    /// </para>
    /// </summary>
    public class InjectDebateFeedbackRequest
    {
        /// <summary>
        /// Текст feedback от пользователя. Передаётся в следующий раунд
        /// actor'а вместо (или вместо +) feedback критика.
        /// </summary>
        public string Feedback { get; set; }
    }
}