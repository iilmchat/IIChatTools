using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ExternalLlm.Formats
{
    /// <summary>
    /// Парсер ответа Anthropic Messages API
    /// (v1.9.0, KI-110a, DESIGN_ANTHROPIC_GEMINI § 3.5).
    ///
    /// <para>
    /// <b>Static helper.</b> Извлекает текст ответа из массива
    /// <c>content[]</c> (склеивает блоки <c>type=="text"</c> через <c>\n</c>),
    /// токены из <c>usage.input_tokens</c> / <c>usage.output_tokens</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Не падает</b> при отсутствии полей — возвращает пустую строку / 0 токенов.
    /// Блоки <c>type=="tool_use"</c> игнорируются (v1.9.0 без function calling).
    /// <c>stop_reason</c> не возвращается — вызывающий код может прочитать
    /// его напрямую из <see cref="JObject"/> для info-лога.
    /// </para>
    /// </summary>
    public static class AnthropicResponseParser
    {
        /// <summary>
        /// Парсит JSON-ответ Anthropic.
        /// </summary>
        /// <param name="response">Корневой <see cref="JObject"/> ответа.</param>
        /// <returns>
        /// Кортеж: склеенный текст (<c>Content</c>), токены prompt
        /// (<c>PromptTokens</c> = <c>usage.input_tokens</c>),
        /// токены completion (<c>CompletionTokens</c> = <c>usage.output_tokens</c>).
        /// </returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="response"/> — <c>null</c>.</exception>
        public static (string Content, int PromptTokens, int CompletionTokens) Parse(JObject response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            // content[] — массив блоков. Склеиваем только type=="text".
            // type=="tool_use" — игнорируем (v1.9.0 non-tools).
            // Пустые блоки пропускаем, чтобы не добавлять лишний \n.
            var text = new StringBuilder();
            var contentArray = response["content"] as JArray;
            if (contentArray != null)
            {
                foreach (var block in contentArray)
                {
                    var blockType = block?["type"]?.ToString();
                    if (!string.Equals(blockType, "text", StringComparison.Ordinal))
                        continue;

                    var blockText = block["text"]?.ToString();
                    if (string.IsNullOrEmpty(blockText))
                        continue;

                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(blockText);
                }
            }

            // usage.input_tokens / usage.output_tokens.
            // Пустой usage / отсутствие поля → 0.
            var promptTokens = response["usage"]?["input_tokens"]?.Value<int>() ?? 0;
            var completionTokens = response["usage"]?["output_tokens"]?.Value<int>() ?? 0;

            return (text.ToString(), promptTokens, completionTokens);
        }
    }
}