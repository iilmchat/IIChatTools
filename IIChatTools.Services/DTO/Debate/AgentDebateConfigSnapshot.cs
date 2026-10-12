using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Debate
{
    /// <summary>
    /// Снимок конфига сессии Actor-Critic для воспроизводимости
    /// (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// Сериализуется в <c>AgentDebateSession.ConfigSnapshotJson</c> при старте
    /// сессии. Позволяет впоследствии понять, с какими настройками
    /// выполнялся конкретный review (например, изменился ли
    /// <c>MaxRounds</c> в appsettings после запуска).
    /// </para>
    /// </summary>
    public class AgentDebateConfigSnapshot
    {
        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна.
        /// <list type="bullet">
        ///   <item><c>"ActorCritic"</c> (default) — Actor + Critic (Ф1, code_agent);</item>
        ///   <item><c>"Debate"</c> — Pro + Contra + Judge (Ф2, planner_agent).</item>
        /// </list>
        /// Default <c>"ActorCritic"</c> — backward-compat со старыми сессиями,
        /// где поля не сериализовались.
        /// </summary>
        public string PatternType { get; set; } = "ActorCritic";

        /// <summary>
        /// Максимум раундов actor-critic (default 3, из DESIGN § 3.1).
        /// </summary>
        public int MaxRounds { get; set; } = 3;

        // ============================================================
        // v2.0.0 (KI-126, Фаза 3): поля Orchestrator-Worker.
        // Заполняются только при PatternType = "Orchestrator".
        // Для ActorCritic / Debate — null / default.
        // ============================================================

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): максимум подзадач для Orchestrator
        /// (default 5, из DESIGN_ORCHESTRATOR_WORKER § 7).
        /// Если декомпозитор вернул больше — обрезаем до MaxSubtasks.
        /// </summary>
        public int MaxSubtasks { get; set; } = 5;

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): флаг параллельного выполнения подзадач.
        /// Определяется декомпозитором (orchestrator_planner_agent) —
        /// default false (последовательно).
        /// </summary>
        public bool Parallel { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): whitelist агентов-воркеров на момент старта
        /// сессии. Orchestrator может вызывать только агентов из этого списка
        /// (DESIGN_ORCHESTRATOR_WORKER § 2.4).
        /// </summary>
        public List<string> AllowedWorkerAgents { get; set; }

        /// <summary>
        /// Token budget сессии (default 50000, из DESIGN § 3.1).
        /// При превышении — Failed с частичным результатом.
        /// </summary>
        public int TokenBudget { get; set; } = 50000;

        /// <summary>Модель actor'а (обычно совпадает с chat.Model).</summary>
        public string ActorModel { get; set; }

        /// <summary>
        /// Модель критика. Default = actor (гетерогенная — только через эскалацию).
        /// Заполняется для <c>PatternType = "ActorCritic"</c>.
        /// </summary>
        public string CriticModel { get; set; }

        // ============================================================
        // v1.14.0 (KI-126, Фаза 2): модели ролей Debate.
        // Заполняются только при PatternType = "Debate".
        // Для ActorCritic — null.
        // ============================================================

        /// <summary>
        /// Модель Pro-агента (защитник предложения).
        /// Заполняется только при <c>PatternType = "Debate"</c>.
        /// </summary>
        public string ProModel { get; set; }

        /// <summary>
        /// Модель Contra-агента (скептик).
        /// Заполняется только при <c>PatternType = "Debate"</c>.
        /// </summary>
        public string ContraModel { get; set; }

        /// <summary>
        /// Модель Judge-агента (арбитр).
        /// Заполняется только при <c>PatternType = "Debate"</c>.
        /// </summary>
        public string JudgeModel { get; set; }

        /// <summary>
        /// Разрешить эскалацию на <c>ask_external_llm</c> при
        /// <c>verdict = Uncertain</c> (DESIGN § 3.3).
        /// </summary>
        public bool AllowEscalation { get; set; } = true;

        /// <summary>
        /// Провайдер эскалации из <c>ExternalLlm:Providers</c>
        /// (default — <c>deepseek</c>).
        /// </summary>
        public string EscalationProvider { get; set; } = "deepseek";

        /// <summary>
        /// Режим Human-in-the-loop:
        /// <c>BetweenRounds</c> (default) | <c>AtEnd</c> | <c>Never</c>.
        /// </summary>
        public string HumanApproval { get; set; } = "BetweenRounds";
    }
}