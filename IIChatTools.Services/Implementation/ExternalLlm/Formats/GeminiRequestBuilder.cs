using System;
using IIChatTools.Services.DTO.ExternalLlm;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm.Formats
{
    /// <summary>
    /// Сборщик тела запроса к Google Gemini API
    /// (v1.10.0, KI-110b, DESIGN_GEMINI § 3.1).
    ///
    /// <para>
    /// <b>Static helper.</b> Формирует <see cref="JObject"/> для
    /// <c>POST {BaseUrl}/models/{model}:generateContent</c>.
    /// Отличия от OpenAI/Anthropic:
    /// <list type="bullet">
    ///   <item><description><c>contents[]</c> — массив Content-объектов
    ///   (<c>{role, parts:[{text}]}</c>), а не <c>messages[]</c> с role/content;</description></item>
    ///   <item><description><c>systemInstruction</c> — отдельный объект Content
    ///   (<c>role</c> не задаётся — Google игнорирует);</description></item>
    ///   <item><description><c>generationConfig.maxOutputTokens</c> — обязателен
    ///   (Gemini вернёт 400 без него);</description></item>
    ///   <item><description><c>temperature</c> — clamp [0, 2] (как в OpenAI);</description></item>
    ///   <item><description><b>модель в URL</b>, а не в body — builder её не возвращает;</description></item>
    ///   <item><description><c>stream</c> — не добавляется (v1.10.0 — non-stream).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Не логирует</b> — вызывающий код отвечает за privacy (DESIGN § 6.3).
    /// </para>
    /// </summary>
    public static class GeminiRequestBuilder
    {
        /// <summary>Значение <c>maxOutputTokens</c> по умолчанию, если ни request, ни provider не задали валидное.</summary>
        private const int DefaultMaxOutputTokens = 4096;

        /// <summary>Значение <c>temperature</c> по умолчанию (при отсутствии override).</summary>
        private const double DefaultTemperature = 0.7;

        /// <summary>
        /// Собирает тело запроса для <c>POST {BaseUrl}/models/{model}:generateContent</c>.
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
                    "GeminiRequestBuilder: Prompt не может быть пустым.");
            }

            // maxOutputTokens — ОБЯЗАТЕЛЬНОЕ поле. Override из request имеет приоритет
            // над дефолтом провайдера.
            var maxOutputTokens = request.MaxTokens ?? provider.MaxTokens;
            if (maxOutputTokens <= 0)
            {
                // Защита от некорректной конфигурации: Gemini вернёт 400 при
                // maxOutputTokens ≤ 0. Fallback — 4096 (как в примере DESIGN § 3.1).
                maxOutputTokens = DefaultMaxOutputTokens;
            }

            var payload = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JArray
                        {
                            new JObject { ["text"] = request.Prompt }
                        }
                    }
                }
            };

            // systemInstruction — отдельный Content-объект (не роль в contents[]).
            // role НЕ задаём: Google игнорирует role в systemInstruction,
            // и передача "user" может привести к ошибке валидации.
            // Добавляется только если задан (не null, не whitespace).
            if (!string.IsNullOrWhiteSpace(request.System))
            {
                payload["systemInstruction"] = new JObject
                {
                    ["parts"] = new JArray
                    {
                        new JObject { ["text"] = request.System }
                    }
                };
            }

            // generationConfig — всегда задан (v1.10.0: maxOutputTokens + temperature).
            var temperature = request.Temperature ?? DefaultTemperature;
            if (temperature < 0.0) temperature = 0.0;
            if (temperature > 2.0) temperature = 2.0;

            payload["generationConfig"] = new JObject
            {
                ["maxOutputTokens"] = maxOutputTokens,
                ["temperature"] = temperature
            };

            // stream НЕ добавляем: v1.10.0 — non-stream (DESIGN § 1.4).
            // Gemini default — non-stream (generateContent).
            // Модель в URL, не в body — builder её не возвращает.

            return payload;
        }
    }
}