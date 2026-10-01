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
        public string ActorOutput { get; set; }

        /// Approved | Rejected | Uncertain
        public string CriticVerdict { get; set; }

        public string CriticFeedbackJson { get; set; }
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
