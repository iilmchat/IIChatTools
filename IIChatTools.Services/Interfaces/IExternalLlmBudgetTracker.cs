namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Дневной бюджет / лимит токенов для External-LLM Agent
    /// (v1.8.1, KI-109, DESIGN_EXTERNAL_LLM § 6.4).
    ///
    /// <para>
    /// <b>Per-user.</b> Защищает от «$1000 за ночь»:
    /// <list type="bullet">
    ///   <item><c>DailyBudgetUsd</c> — 5 USD по умолчанию;</item>
    ///   <item><c>DailyTokensLimit</c> — 500k токенов по умолчанию.</item>
    /// </list>
    /// Сбрасывается в 00:00 UTC.
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> Состояние — <c>ConcurrentDictionary&lt;int, DailyUsage&gt;</c>.
    /// Cleanup — <c>Timer</c> каждые 30 минут (по образцу KI-043).
    /// </para>
    /// </summary>
    public interface IExternalLlmBudgetTracker
    {
        /// <summary>
        /// Может ли пользователь сделать ещё один запрос в рамках дневного лимита.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>
        /// <c>true</c> — лимит не превышен;
        /// <c>false</c> — превышен (tool вернёт Fail с сообщением «Повторите завтра»).
        /// </returns>
        bool CanSpend(int userId);

        /// <summary>
        /// Записать фактическое потребление после успешного запроса.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="promptTokens">Токенов в prompt</param>
        /// <param name="completionTokens">Токенов в completion</param>
        /// <param name="costUsd">Стоимость запроса в USD</param>
        void RecordUsage(
            int userId,
            int promptTokens,
            int completionTokens,
            decimal costUsd);

        /// <summary>
        /// Текущий расход пользователя за сегодня (USD).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Сумма в USD (0, если запросов не было).</returns>
        decimal GetTodayCostUsd(int userId);

        /// <summary>
        /// Текущий расход пользователя за сегодня (токенов, prompt + completion).
        /// </summary>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <returns>Сумма токенов (0, если запросов не было).</returns>
        long GetTodayTokens(int userId);
    }
}