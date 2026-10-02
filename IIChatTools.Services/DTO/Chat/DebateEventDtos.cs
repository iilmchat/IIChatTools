namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Payload SSE-события <c>debate_started</c> — Actor-Critic сессия началась
    /// (v1.11.0, KI-126, Шаг 1E).
    /// </summary>
    public class ChatDebateStartedDto
    {
        /// <summary>Идентификатор сессии (из <c>AgentDebateSession</c>).</summary>
        public int SessionId { get; set; }

        /// <summary>Исходная задача для review.</summary>
        public string Task { get; set; }

        /// <summary>Имя actor-агента (обычно <c>code_agent</c>).</summary>
        public string ActorAgent { get; set; }

        /// <summary>Имя critic-агента (обычно <c>code_reviewer_agent</c>).</summary>
        public string CriticAgent { get; set; }

        /// <summary>Максимум раундов (из снимка конфига).</summary>
        public int MaxRounds { get; set; }

        /// <summary>
        /// Политика Human-in-the-loop: <c>BetweenRounds</c> | <c>AtEnd</c> | <c>Never</c>
        /// (v1.11.0, KI-126, Шаг 1G.3).
        ///
        /// <para>
        /// UI использует это поле, чтобы решить, показывать ли inline-блок feedback
        /// после <c>debate_round</c> с <c>criticVerdict = "Rejected"</c>.
        /// </para>
        /// </summary>
        public string HumanApproval { get; set; }
    }

    /// <summary>
    /// Payload SSE-события <c>debate_round</c> — завершён один раунд
    /// (actor + critic).
    /// </summary>
    public class ChatDebateRoundDto
    {
        /// <summary>Идентификатор сессии.</summary>
        public int SessionId { get; set; }

        /// <summary>Порядковый номер раунда (1..N).</summary>
        public int RoundNumber { get; set; }

        /// <summary>Вывод actor'а (код / текст).</summary>
        public string ActorOutput { get; set; }

        /// <summary>Вердикт критика: <c>Approved</c> | <c>Rejected</c> | <c>Uncertain</c>.</summary>
        public string CriticVerdict { get; set; }

        /// <summary>
        /// Feedback критика (JSON со списком issues или текст).
        /// <c>null</c>, если вердикт Approved без замечаний.
        /// </summary>
        public string CriticFeedback { get; set; }

        /// <summary>Был ли раунд с эскалацией на внешнюю LLM.</summary>
        public bool WasEscalated { get; set; }
    }

    /// <summary>
    /// Payload SSE-события <c>debate_escalated</c> — критик вернул Uncertain,
    /// вызов перенаправлен на внешнюю LLM (Шаг 1F).
    /// </summary>
    public class ChatDebateEscalatedDto
    {
        /// <summary>Идентификатор сессии.</summary>
        public int SessionId { get; set; }

        /// <summary>Причина эскалации (обычно текст вопроса критика).</summary>
        public string Reason { get; set; }

        /// <summary>Провайдер внешней LLM (deepseek / openai / ...).</summary>
        public string ExternalProvider { get; set; }

        /// <summary>Стоимость запроса в USD (0 для локальных моделей).</summary>
        public decimal CostUsd { get; set; }
    }

    /// <summary>
    /// Payload SSE-события <c>debate_completed</c> — сессия завершена.
    /// </summary>
    public class ChatDebateCompletedDto
    {
        /// <summary>Идентификатор сессии.</summary>
        public int SessionId { get; set; }

        /// <summary>
        /// Финальный вердикт: <c>Approved</c> | <c>Rejected</c> |
        /// <c>MaxRoundsReached</c> | <c>Uncertain</c> | <c>Cancelled</c>.
        /// </summary>
        public string Verdict { get; set; }

        /// <summary>Общее число завершённых раундов.</summary>
        public int TotalRounds { get; set; }

        /// <summary>Финальный артефакт (код от actor'а последнего раунда).</summary>
        public string FinalArtifact { get; set; }

        /// <summary>Суммарная стоимость сессии в USD.</summary>
        public decimal TotalCostUsd { get; set; }
    }
}