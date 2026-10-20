using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Debate
{
    /// <summary>
    /// Статус сессии Actor-Critic / Debate (v1.11.0, KI-126, Шаг 1B).
    ///
    /// <para>
    /// Возвращается из <c>IAgentDebateSessionService.GetStatusAsync</c>.
    /// Включает метаданные сессии + список завершённых раундов.
    /// </para>
    /// </summary>
    public class AgentDebateStatusDto
    {
        /// <summary>Идентификатор сессии.</summary>
        public int SessionId { get; set; }

        /// <summary>Идентификатор чата, к которому привязана сессия.</summary>
        public int ChatId { get; set; }

        /// <summary>
        /// Исходная задача review (для заголовка блока в UI).
        /// v1.11.0 (KI-129).
        /// </summary>
        public string Task { get; set; }

        /// <summary>
        /// Статус сессии: <c>Pending</c> | <c>InProgress</c> | <c>Completed</c>
        /// | <c>Failed</c> | <c>Cancelled</c>.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Финальный вердикт: <c>Approved</c> | <c>Rejected</c>
        /// | <c>NeedsHuman</c> | <c>MaxRoundsReached</c>.
        /// <c>null</c>, пока сессия не завершена.
        /// </summary>
        public string FinalVerdict { get; set; }

        /// <summary>
        /// Финальный артефакт (JSON из <c>AgentDebateSession.FinalArtifactJson</c>).
        /// UI парсит поле <c>finalArtifact</c>. v1.11.0 (KI-129).
        /// </summary>
        public string FinalArtifactJson { get; set; }

        /// <summary>Количество завершённых раундов.</summary>
        public int TotalRounds { get; set; }

        /// <summary>Суммарная стоимость сессии в USD (включая эскалации).</summary>
        public decimal TotalCostUsd { get; set; }

        /// <summary>Дата старта сессии (UTC).</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>Дата завершения сессии (UTC). <c>null</c> — сессия активна.</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Максимум раундов — парсится из <c>ConfigSnapshotJson</c>.
        /// Нужно для UI-заголовка «Max N rounds». v1.11.0 (KI-129).
        /// </summary>
        public int MaxRounds { get; set; }

        /// <summary>
        /// Политика Human-in-the-loop (<c>BetweenRounds</c> | <c>AtEnd</c> | <c>Never</c>) —
        /// парсится из <c>ConfigSnapshotJson</c>. UI использует для восстановления
        /// orphaned-сессий. v1.11.0 (KI-129).
        /// </summary>
        public string HumanApproval { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): тип паттерна.
        /// <c>"ActorCritic"</c> (Ф1) | <c>"Debate"</c> (Ф2).
        /// UI ветвится по этому полю для рендера 2- или 3-ролевого блока.
        /// </summary>
        public string PatternType { get; set; }

        /// <summary>
        /// Имя actor-агента. Для Ф1 (Actor-Critic) — константа <c>code_agent</c>.
        /// Заполняется только при <c>PatternType = "ActorCritic"</c>.
        /// v1.11.0 (KI-129).
        /// </summary>
        public string ActorAgent { get; set; }

        /// <summary>
        /// Имя critic-агента. Для Ф1 (Actor-Critic) — константа
        /// <c>code_reviewer_agent</c>. Заполняется только при
        /// <c>PatternType = "ActorCritic"</c>. v1.11.0 (KI-129).
        /// </summary>
        public string CriticAgent { get; set; }

        // ============================================================
        // v1.14.0 (KI-126, Фаза 2): роли Debate.
        // Заполняются только при PatternType = "Debate".
        // ============================================================

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Pro-агента (защитник).
        /// </summary>
        public string ProAgent { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Contra-агента (скептик).
        /// </summary>
        public string ContraAgent { get; set; }

        /// <summary>
        /// v1.14.0 (KI-126, Фаза 2): имя Judge-агента (арбитр).
        /// </summary>
        public string JudgeAgent { get; set; }

        // ============================================================
        // v2.0.0 (KI-126, Фаза 3): поля Orchestrator-Worker.
        // Заполняются только при PatternType = "Orchestrator".
        // ============================================================

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): имя агента-декомпозитора
        /// (обычно <c>orchestrator_planner_agent</c>).
        /// Только для <c>PatternType = "Orchestrator"</c>.
        /// </summary>
        public string OrchestratorAgent { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): общее число подзадач.
        /// Только для <c>PatternType = "Orchestrator"</c>.
        /// </summary>
        public int TotalSubtasks { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): число успешно выполненных подзадач
        /// (status = "completed").
        /// Только для <c>PatternType = "Orchestrator"</c>.
        /// </summary>
        public int CompletedSubtasks { get; set; }

        /// <summary>
        /// v2.0.0 (KI-126, Фаза 3): число проваленных подзадач
        /// (status = "failed" или "rejected").
        /// Только для <c>PatternType = "Orchestrator"</c>.
        /// </summary>
        public int FailedSubtasks { get; set; }

        /// <summary>
        /// Раунды сессии (сортировка по <c>RoundNumber</c> asc).
        /// Пустой список, если раундов ещё нет.
        /// </summary>
        public IReadOnlyList<AgentDebateRoundDto> Rounds { get; set; }
            = new List<AgentDebateRoundDto>();

        /// <summary>
        /// Reflection: имя агента (reflector_agent). null для других паттернов.
        /// </summary>
        public string ReflectionAgent { get; set; }            
    }
}