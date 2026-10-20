using System;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Debate
{
    /// <summary>
    /// Парсер ответа самокритики для Reflection (v2.2.0, KI-244).
    /// Ожидает JSON {verdict, issues[], summary}.
    /// </summary>
    public static class ReflectionCritiqueParser
    {
        public static bool TryParse(string raw, out string verdict, out JArray issues, out string summary)
        {
            verdict = "Uncertain";
            issues = new JArray();
            summary = null;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            try
            {
                // Снимаем markdown-обёртку, если есть
                var jsonText = raw.Trim();
                if (jsonText.StartsWith("```"))
                {
                    var start = jsonText.IndexOf('\n') + 1;
                    var end = jsonText.LastIndexOf("```", StringComparison.Ordinal);
                    if (start > 0 && end > start)
                        jsonText = jsonText.Substring(start, end - start).Trim();
                }

                var obj = JObject.Parse(jsonText);

                var v = obj["verdict"]?.ToString();
                if (!string.IsNullOrEmpty(v))
                {
                    var lower = v.ToLowerInvariant();
                    if (lower == "approved") verdict = "Approved";
                    else if (lower == "rejected") verdict = "Rejected";
                    else if (lower == "uncertain") verdict = "Uncertain";
                    else verdict = "Uncertain"; // fallback
                }

                var arr = obj["issues"] as JArray;
                if (arr != null) issues = arr;

                summary = obj["summary"]?.ToString();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}