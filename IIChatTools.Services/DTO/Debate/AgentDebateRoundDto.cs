namespace IIChatTools.Services.DTO.Debate
{
    /// <summary>
    /// Один раунд сессии Actor-Critic (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// Содержит вывод actor'а + вердикт критика + статистику генерации.
    /// Используется в UI для отображения раундов (режим «диалог») и в
    /// финальном артефакте (режим «сворачиваемый»).
    /// </para>
    /// </summary>
    public class AgentDebateRoundDto
    {
        /// <summary>Порядковый номер раунда (1..N).</summary>
        public int RoundNumber { get; set; }

        /// <summary>
        /// Вывод actor'а (код / текст).
        /// Заполняется только при <c>PatternType = "ActorCritic"</c>.
        /// При <c>PatternType = "Debate"</c> = <c>null</c>.
        /// </summary>
        public string ActorOutput { get; set; }

        /// <summary>
        /// Вердикт критика: <c>Approved</c> | <c>Rejected</c> | <c>Uncertain</c>.
        /// </summary>
        public string CriticVerdict { get; set; }

        /// <summary>
        /// JSON со списком issues от критика (формат из DESIGN § 2.3:
        /// <c>[{ severity, location, description, suggestion }]</c>).
        /// <c>null</c>, если критик вернул Approved без замечаний.
        /// Заполняется только при <c>PatternType = "ActorCritic"</c>.
        /// </summary>
        public string CriticFeedbackJson { get; set; }

        // ============================================================
        // v1.14.0 (KI-126, Фаза 2): поля ролей Debate.
        // Заполняются только при PatternType = "Debate".
        // Для ActorCritic — null.
        // ============================================================

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вывод Pro-агента (защитник предложения).
        /// JSON <c>{arguments, coveredCases, conclusion}</c> (см. промпт
        /// <c>SubAgents:planner_pro_agent:SystemPrompt</c>).
        /// </summary>
        public string ProOutput { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вывод Contra-агента (скептик).
        /// JSON <c>{issues, risks, conclusion}</c>.
        /// </summary>
        public string ContraOutput { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вердикт Judge'а:
        /// <c>Approved</c> | <c>Rejected</c> | <c>Uncertain</c>.
        /// </summary>
        public string JudgeVerdict { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): JSON с обоснованием Judge'а
        /// (reasoning + список принятых issues). Формат из промпта
        /// <c>SubAgents:planner_judge_agent:SystemPrompt</c>:
        /// <c>{verdict, reasoning, issues, summary}</c>.
        /// </summary>
        public string JudgeFeedbackJson { get; set; }

        /// <summary>Модель, использованная для actor'а.</summary>
        public string ActorModel { get; set; }

        /// <summary>Модель, использованная для критика.</summary>
        public string CriticModel { get; set; }

        /// <summary>
        /// Признак эскалации на внешнюю LLM
        /// (verdict критика был <c>Uncertain</c>).
        /// </summary>
        public bool WasEscalated { get; set; }

        /// <summary>
        /// Провайдер эскалации (<c>deepseek</c> / <c>openai</c> / ...).
        /// <c>null</c>, если эскалации не было.
        /// </summary>
        public string EscalationProvider { get; set; }

        /// <summary>Токенов prompt, потрачено в раунде.</summary>
        public int TokensIn { get; set; }

        /// <summary>Токенов completion, потрачено в раунде.</summary>
        public int TokensOut { get; set; }

        /// <summary>Стоимость раунда в USD (0 для локальных моделей).</summary>
        public decimal CostUsd { get; set; }

        /// <summary>Длительность раунда (мс).</summary>
        public int DurationMs { get; set; }
    }
}