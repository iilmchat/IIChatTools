using System;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Один раунд внутри сессии: actor-вывод + critic-вердикт.
    /// v1.11.0 (KI-126, Шаг 1A).
    /// </summary>
    public class AgentDebateRound : BaseEntity
    {
        public int SessionId { get; set; }
        public virtual AgentDebateSession Session { get; set; }

        public int RoundNumber { get; set; }

        // ============================================================
        // Роли Actor-Critic (PatternType = "ActorCritic").
        // Для PatternType = "Debate" — эти поля = null.
        // ============================================================

        /// <summary>
        /// Вывод Actor'а (код / артефакт).
        /// Заполняется только для Actor-Critic (PatternType = "ActorCritic").
        /// </summary>
        public string ActorOutput { get; set; }

        /// <summary>
        /// Вердикт Critic'а: Approved | Rejected | Uncertain.
        /// Заполняется только для Actor-Critic (PatternType = "ActorCritic").
        /// </summary>
        public string CriticVerdict { get; set; }

        /// <summary>
        /// JSON со списком issues / обоснованием Critic'а.
        /// Заполняется только для Actor-Critic (PatternType = "ActorCritic").
        /// </summary>
        public string CriticFeedbackJson { get; set; }

        // ============================================================
        // Роли Debate (PatternType = "Debate") — v1.14.0 (KI-126, Фаза 2).
        // Для PatternType = "ActorCritic" — эти поля = null.
        // ============================================================

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вывод Pro-агента (защитник предложения).
        /// Заполняется только для Debate (PatternType = "Debate").
        /// </summary>
        public string ProOutput { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вывод Contra-агента (критик предложения).
        /// Заполняется только для Debate (PatternType = "Debate").
        /// </summary>
        public string ContraOutput { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): вердикт Judge'а (синтез аргументов Pro/Contra).
        /// Approved | Rejected | Uncertain. Заполняется только для Debate
        /// (PatternType = "Debate").
        /// </summary>
        public string JudgeVerdict { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): JSON с обоснованием Judge'а
        /// (reasoning + список issues). Заполняется только для Debate
        /// (PatternType = "Debate").
        /// </summary>
        public string JudgeFeedbackJson { get; set; }

        // ============================================================
        // v2.0.0 (KI-126, Фаза 3): поля Orchestrator-Worker.
        // Заполняются только при PatternType = "Orchestrator".
        // Для ActorCritic / Debate — null.
        // ============================================================

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): идентификатор подзадачи ("1", "2", ...).
        /// Только для PatternType = "Orchestrator".
        /// </summary>
        public string SubtaskId { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): имя агента-исполнителя подзадачи
        /// (web_agent, code_agent, ...).
        /// </summary>
        public string SubtaskAgent { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): текст подзадачи.
        /// </summary>
        public string SubtaskTask { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): статус подзадачи:
        /// pending | running | completed | failed | rejected.
        /// </summary>
        public string SubtaskStatus { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): финальный ответ Worker'а.
        /// </summary>
        public string SubtaskResult { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): сообщение об ошибке
        /// при SubtaskStatus = "failed" / "rejected".
        /// </summary>
        public string SubtaskError { get; set; }

        // ============================================================
        // Общие поля для обоих паттернов.
        // ============================================================

        public string ActorModel { get; set; }
        public string CriticModel { get; set; }

        public bool WasEscalated { get; set; }
        public string EscalationProvider { get; set; }

        public int TokensIn { get; set; }
        public int TokensOut { get; set; }
        public decimal CostUsd { get; set; }
        public int DurationMs { get; set; }
    }
}
