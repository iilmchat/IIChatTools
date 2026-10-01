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
        /// Максимум раундов actor-critic (default 3, из DESIGN § 3.1).
        /// </summary>
        public int MaxRounds { get; set; } = 3;

        /// <summary>
        /// Token budget сессии (default 50000, из DESIGN § 3.1).
        /// При превышении — Failed с частичным результатом.
        /// </summary>
        public int TokenBudget { get; set; } = 50000;

        /// <summary>Модель actor'а (обычно совпадает с chat.Model).</summary>
        public string ActorModel { get; set; }

        /// <summary>
        /// Модель критика. Default = actor (гетерогенная — только через эскалацию).
        /// </summary>
        public string CriticModel { get; set; }

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