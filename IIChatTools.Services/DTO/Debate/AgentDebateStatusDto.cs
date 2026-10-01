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

        /// <summary>Количество завершённых раундов.</summary>
        public int TotalRounds { get; set; }

        /// <summary>Суммарная стоимость сессии в USD (включая эскалации).</summary>
        public decimal TotalCostUsd { get; set; }

        /// <summary>Дата старта сессии (UTC).</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>Дата завершения сессии (UTC). <c>null</c> — сессия активна.</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Раунды сессии (сортировка по <c>RoundNumber</c> asc).
        /// Пустой список, если раундов ещё нет.
        /// </summary>
        public IReadOnlyList<AgentDebateRoundDto> Rounds { get; set; }
            = new List<AgentDebateRoundDto>();
    }
}