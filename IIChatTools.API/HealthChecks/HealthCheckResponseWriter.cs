using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Encodings.Web;

namespace IIChatTools.API.HealthChecks
{
    /// <summary>
    /// JSON-сериализатор отчёта health-check для endpoint'ов <c>/health</c>, <c>/health/live</c>, <c>/health/ready</c>.
    /// Использует <c>System.Text.Json</c> (не Newtonsoft) — независимо от MVC-сериализатора.
    /// </summary>
    public static class HealthCheckResponseWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Кириллица в описаниях health-check выводится как есть, а не \uXXXX.
            // UnsafeRelaxedJsonEscaping безопасен здесь: JSON не рендерится в HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Записывает <see cref="HealthReport"/> в HTTP-ответ как JSON.
        /// </summary>
        /// <param name="context">HTTP-контекст</param>
        /// <param name="report">Отчёт о состоянии сервисов</param>
        /// <returns>Асинхронная задача</returns>
        public static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    description = e.Value.Description,
                    error = e.Value.Exception?.Message,
                    tags = e.Value.Tags
                }).ToArray()
            };

            return context.Response.WriteAsJsonAsync(payload, JsonOptions);
        }
    }
}