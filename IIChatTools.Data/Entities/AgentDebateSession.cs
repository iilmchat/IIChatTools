using System;
using System.Collections.Generic;

namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Сессия автономного взаимодействия агентов (Actor-Critic / Debate).
    /// Привязана к чату и инициирована пользователем.
    /// v1.11.0 (KI-126, Шаг 1A).
    /// </summary>
    public class AgentDebateSession : BaseEntity
    {
        public int ChatId { get; set; }
        public virtual Chat Chat { get; set; }

        public int InitiatedByUserId { get; set; }

        public string Task { get; set; }

        /// ActorCritic | Debate | Reflection
        public string PatternType { get; set; }

        /// Pending | InProgress | Completed | Failed | Cancelled
        public string Status { get; set; }

        /// Approved | Rejected | NeedsHuman | MaxRoundsReached
        public string FinalVerdict { get; set; }

        public string FinalArtifactJson { get; set; }
        public string ConfigSnapshotJson { get; set; }

        public decimal TotalCostUsd { get; set; }
        public int TotalTokensIn { get; set; }
        public int TotalTokensOut { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public virtual ICollection<AgentDebateRound> Rounds { get; set; }
    }
}
