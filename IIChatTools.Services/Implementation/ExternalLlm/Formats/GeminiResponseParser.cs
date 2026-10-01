using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm.Formats
{
    /// <summary>
    /// Парсер ответа Google Gemini API
    /// (v1.10.0, KI-110b, DESIGN_GEMINI § 3.2).
    ///
    /// <para>
    /// <b>Static helper.</b> Извлекает текст ответа из
    /// <c>candidates[0].content.parts[]</c> (склеивает блоки с <c>text</c>
    /// через <c>\n</c>), токены из <c>usageMetadata.promptTokenCount</c> /
    /// <c>usageMetadata.candidatesTokenCount</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Не падает</b> при отсутствии полей — возвращает пустую строку / 0 токенов.
    /// Блоки <c>thought: true</c> (v1.10.0 без thinking), <c>functionCall</c>,
    /// <c>functionResponse</c>, <c>inlineData</c>, <c>codeExecutionResult</c> —
    /// игнорируются (не поддерживаются).
    /// </para>
    ///
    /// <para>
    /// <c>candidates[0].finishReason</c> (<c>STOP</c> / <c>MAX_TOKENS</c> /
    /// <c>SAFETY</c> / <c>RECITATION</c> / <c>OTHER</c>) не возвращается —
    /// вызывающий код может прочитать его напрямую из <see cref="JObject"/>
    /// для info-лога.
    /// </para>
    ///
    /// <para>
    /// <b>Safety block:</b> если <c>promptFeedback.blockReason</c> установлен,
    /// <c>candidates[]</c> может быть пуст — возвращаем пустую строку (не падаем).
    /// Клиент сам решает, что делать с пустым ответом (DESIGN § 3.4).
    /// </para>
    /// </summary>
    public static class GeminiResponseParser
    {
        /// <summary>
        /// Парсит JSON-ответ Gemini.
        /// </summary>
        /// <param name="response">Корневой <see cref="JObject"/> ответа.</param>
        /// <returns>
        /// Кортеж: склеенный текст (<c>Content</c>), токены prompt
        /// (<c>PromptTokens</c> = <c>usageMetadata.promptTokenCount</c>),
        /// токены completion (<c>CompletionTokens</c> = <c>usageMetadata.candidatesTokenCount</c>).
        /// </returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="response"/> — <c>null</c>.</exception>
        public static (string Content, int PromptTokens, int CompletionTokens) Parse(JObject response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            // usageMetadata — токены. Отсутствие / пустой usage → 0.
            // Google использует promptTokenCount / candidatesTokenCount.
            var promptTokens = response["usageMetadata"]?["promptTokenCount"]?.Value<int>() ?? 0;
            var completionTokens = response["usageMetadata"]?["candidatesTokenCount"]?.Value<int>() ?? 0;

            // candidates[] — массив вариантов ответа. Берём первый.
            // Если candidates пуст (например, promptFeedback.blockReason) —
            // возвращаем пустую строку + токены (обычно 0).
            var candidates = response["candidates"] as JArray;
            if (candidates == null || candidates.Count == 0)
            {
                return (string.Empty, promptTokens, completionTokens);
            }

            var firstCandidate = candidates[0];
            if (firstCandidate == null)
            {
                return (string.Empty, promptTokens, completionTokens);
            }

            // candidates[0].content.parts[] — блоки контента.
            // Склеиваем блоки с "text" через \n. Остальные типы (thought,
            // functionCall, functionResponse, inlineData, codeExecutionResult)
            // игнорируются — не поддерживаются в v1.10.0.
            var text = new StringBuilder();
            var parts = firstCandidate["content"]?["parts"] as JArray;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part == null) continue;

                    // Пропускаем thinking-блоки (v1.10.0 без thinking).
                    var isThought = part["thought"]?.Value<bool>() ?? false;
                    if (isThought) continue;

                    var partText = part["text"]?.ToString();
                    if (string.IsNullOrEmpty(partText)) continue;

                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(partText);
                }
            }

            return (text.ToString(), promptTokens, completionTokens);
        }
    }
}