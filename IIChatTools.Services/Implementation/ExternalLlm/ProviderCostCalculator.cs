using System;
using IIChatTools.Services.DTO.ExternalLlm;

namespace IIChatTools.Services.Implementation.ExternalLlm
{
    /// <summary>
    /// Расчёт стоимости запроса к внешней LLM (v1.8.1, KI-109, Фаза 2.4).
    ///
    /// <para>
    /// Формула: <c>(promptTokens / 1000) * CostPer1kInput + (completionTokens / 1000) * CostPer1kOutput</c>.
    /// Отрицательные тарифы и токены — clamp к 0.
    /// </para>
    ///
    /// <para>
    /// <b>Static helper.</b> Не Singleton, не инжектится — вызывается
    /// из <c>ExternalLlmClient</c> после получения ответа.
    /// </para>
    /// </summary>
    public static class ProviderCostCalculator
    {
        /// <summary>
        /// Считает стоимость запроса в USD.
        /// </summary>
        /// <param name="promptTokens">Токенов в prompt</param>
        /// <param name="completionTokens">Токенов в completion</param>
        /// <param name="options">Тарифы провайдера</param>
        /// <returns>Стоимость в USD (0, если провайдер null или тарифы нулевые)</returns>
        public static decimal Calculate(
            int promptTokens,
            int completionTokens,
            ExternalProviderOptions options)
        {
            if (options == null)
                return 0m;

            // Clamp отрицательных значений (DESIGN § 5.6: CostPer1k* ≥ 0).
            var inputTokens = Math.Max(0, promptTokens);
            var outputTokens = Math.Max(0, completionTokens);

            var inputCostPer1k = options.CostPer1kInputUsd < 0m ? 0m : options.CostPer1kInputUsd;
            var outputCostPer1k = options.CostPer1kOutputUsd < 0m ? 0m : options.CostPer1kOutputUsd;

            return (inputTokens / 1000m) * inputCostPer1k
                 + (outputTokens / 1000m) * outputCostPer1k;
        }
    }
}