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

        /// <summary>Вывод actor'а (код / текст).</summary>
        public string ActorOutput { get; set; }

        /// <summary>
        /// Вердикт критика: <c>Approved</c> | <c>Rejected</c> | <c>Uncertain</c>.
        /// </summary>
        public string CriticVerdict { get; set; }

        /// <summary>
        /// JSON со списком issues от критика (формат из DESIGN § 2.3:
        /// <c>[{ severity, location, description, suggestion }]</c>).
        /// <c>null</c>, если критик вернул Approved без замечаний.
        /// </summary>
        public string CriticFeedbackJson { get; set; }

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