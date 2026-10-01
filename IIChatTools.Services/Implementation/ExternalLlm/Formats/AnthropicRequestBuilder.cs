using System;
using IIChatTools.Services.DTO.ExternalLlm;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm.Formats
{
    /// <summary>
    /// Сборщик тела запроса к Anthropic Messages API
    /// (v1.9.0, KI-110a, DESIGN_ANTHROPIC_GEMINI § 3.4).
    ///
    /// <para>
    /// <b>Static helper.</b> Формирует <see cref="JObject"/> для
    /// <c>POST {BaseUrl}/messages</c>. Отличия от OpenAI-формата:
    /// <list type="bullet">
    ///   <item><description><c>max_tokens</c> — <b>обязателен</b> (Anthropic вернёт 400 при отсутствии);</description></item>
    ///   <item><description><c>system</c> — отдельное поле (string), не роль в <c>messages[]</c>;</description></item>
    ///   <item><description><c>temperature</c> — clamp [0, 1] (не [0, 2], как в OpenAI);</description></item>
    ///   <item><description><c>stream</c> — не добавляется (v1.9.0 — non-stream).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Не логирует</b> — вызывающий код отвечает за privacy (DESIGN § 6.3).
    /// </para>
    /// </summary>
    public static class AnthropicRequestBuilder
    {
        /// <summary>Значение <c>max_tokens</c> по умолчанию, если ни request, ни provider не задали валидное.</summary>
        private const int DefaultMaxTokens = 4096;

        /// <summary>Значение <c>temperature</c> по умолчанию (при отсутствии override).</summary>
        private const double DefaultTemperature = 0.7;

        /// <summary>
        /// Собирает тело запроса для <c>POST {BaseUrl}/messages</c>.
        /// </summary>
        /// <param name="provider">Настройки провайдера (<c>Model</c>, <c>MaxTokens</c>).</param>
        /// <param name="request">Запрос (<c>Prompt</c>, <c>System</c>, <c>Temperature</c>, <c>MaxTokens</c>).</param>
        /// <returns><see cref="JObject"/> готовый к сериализации в JSON.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="provider"/> или <paramref name="request"/> — <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Если <see cref="ExternalLlmRequest.Prompt"/> пуст.</exception>
        public static JObject Build(ExternalProviderOptions provider, ExternalLlmRequest request)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                throw new InvalidOperationException(
                    "AnthropicRequestBuilder: Prompt не может быть пустым.");
            }

            // max_tokens — ОБЯЗАТЕЛЬНОЕ поле. Override из request имеет приоритет
            // над дефолтом провайдера.
            var maxTokens = request.MaxTokens ?? provider.MaxTokens;
            if (maxTokens <= 0)
            {
                // Защита от некорректной конфигурации: Anthropic вернёт 400
                // при max_tokens ≤ 0. Fallback — 4096 (как в примере DESIGN § 3.4).
                maxTokens = DefaultMaxTokens;
            }

            var payload = new JObject
            {
                ["model"] = provider.Model,
                ["max_tokens"] = maxTokens,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = request.Prompt
                    }
                }
            };

            // system — отдельное поле (не роль в messages[]).
            // Добавляется только если задан (не null, не whitespace).
            if (!string.IsNullOrWhiteSpace(request.System))
            {
                payload["system"] = request.System;
            }

            // temperature — clamp [0, 1] (Anthropic range). Дефолт 0.7.
            var temperature = request.Temperature ?? DefaultTemperature;
            if (temperature < 0.0) temperature = 0.0;
            if (temperature > 1.0) temperature = 1.0;
            payload["temperature"] = temperature;

            // stream НЕ добавляем: v1.9.0 — non-stream (DESIGN § 1.4).
            // Anthropic default stream=false. Явное указание не обязательно.

            return payload;
        }
    }
}