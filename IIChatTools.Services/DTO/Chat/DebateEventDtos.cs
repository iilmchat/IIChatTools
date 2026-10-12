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

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна — <c>"ActorCritic"</c> | <c>"Debate"</c>.
        /// UI использует для ветвления рендера (2 или 3 роли).
        /// </summary>
        public string PatternType { get; set; }

        /// <summary>Имя actor-агента (обычно <c>code_agent</c>). Только для ActorCritic.</summary>
        public string ActorAgent { get; set; }

        /// <summary>Имя critic-агента (обычно <c>code_reviewer_agent</c>). Только для ActorCritic.</summary>
        public string CriticAgent { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Pro-агента. Только для Debate.
        /// </summary>
        public string ProAgent { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Contra-агента. Только для Debate.
        /// </summary>
        public string ContraAgent { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Judge-агента. Только для Debate.
        /// </summary>
        public string JudgeAgent { get; set; }

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

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна — <c>"ActorCritic"</c> | <c>"Debate"</c>.
        /// UI использует для ветвления рендера раунда.
        /// </summary>
        public string PatternType { get; set; }

        // ----- Поля ActorCritic -----

        /// <summary>Вывод actor'а. Только для ActorCritic.</summary>
        public string ActorOutput { get; set; }

        /// <summary>Вердикт критика. Только для ActorCritic.</summary>
        public string CriticVerdict { get; set; }

        /// <summary>Feedback критика. Только для ActorCritic.</summary>
        public string CriticFeedback { get; set; }

        // ----- Поля Debate (v1.14.0, KI-126, Фаза 2) -----

        /// <summary>v1.14.0 (KI-126, Фаза 2): вывод Pro-агента. Только для Debate.</summary>
        public string ProOutput { get; set; }

        /// <summary>v1.14.0 (KI-126, Фаза 2): вывод Contra-агента. Только для Debate.</summary>
        public string ContraOutput { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вердикт Judge'а.
        /// <c>Approved</c> | <c>Rejected</c> | <c>Uncertain</c>. Только для Debate.
        /// </summary>
        public string JudgeVerdict { get; set; }

        /// <summary>v1.14.0 (KI-126, Фаза 2): JSON обоснования Judge'а. Только для Debate.</summary>
        public string JudgeFeedback { get; set; }

        // ----- Общее -----

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

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна.
        /// <c>"ActorCritic"</c> | <c>"Debate"</c>.
        /// </summary>
        public string PatternType { get; set; }

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
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна.
        /// <c>"ActorCritic"</c> | <c>"Debate"</c>.
        /// </summary>
        public string PatternType { get; set; }

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