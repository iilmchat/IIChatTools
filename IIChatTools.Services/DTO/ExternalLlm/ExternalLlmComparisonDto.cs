namespace IIChatTools.Services.DTO.ExternalLlm
{
    /// <summary>
    /// Результат сравнения двух внешних LLM (v1.8.1, KI-109, DESIGN § 3.2 сценарий D).
    ///
    /// <para>
    /// Возвращается инструментом <c>ask_external_llm</c>, если в
    /// <see cref="ExternalLlmRequest.CompareWith"/> указан второй провайдер.
    /// Оба HTTP-запроса выполняются параллельно (<c>Task.WhenAll</c>).
    /// </para>
    /// </summary>
    public class ExternalLlmComparisonDto
    {
        /// <summary>
        /// Ответ основного провайдера (<see cref="ExternalLlmRequest.Provider"/>).
        /// </summary>
        public ExternalLlmResponse Primary { get; set; }

        /// <summary>
        /// Ответ второго провайдера (<see cref="ExternalLlmRequest.CompareWith"/>).
        /// </summary>
        public ExternalLlmResponse Secondary { get; set; }

        /// <summary>
        /// Опциональный вывод-сравнение (заполняется верхним уровнем —
        /// инструментом или Chat LLM, — а не самим <c>IExternalLlmClient</c>).
        /// </summary>
        public string Comparison { get; set; }

        /// <summary>
        /// Суммарная стоимость обоих запросов в USD
        /// (<c>Primary.CostUsd + Secondary.CostUsd</c>).
        /// </summary>
        public decimal TotalCostUsd { get; set; }
    }
}