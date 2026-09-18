using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.HealthChecks
{
    /// <summary>
    /// Health-check доступности LM Studio (OpenAI-совместимый API).
    /// Делает GET-запрос к <c>{LmStudio:BaseUrl}/v1/models</c> с настраиваемым таймаутом.
    /// Возвращает <see cref="HealthStatus.Degraded"/> при недоступности — LM Studio внешний
    /// сервис, его отсутствие не делает приложение неработоспособным.
    /// Регистрируется с тегами <c>external</c> и <c>lmstudio</c> — участвует только в <c>/health</c>.
    /// </summary>
    public class LmStudioHealthCheck : IHealthCheck
    {
        private const string HttpClientName = "LmStudioHealth";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LmStudioHealthCheck> _logger;

        /// <summary>
        /// Создаёт health-check LM Studio.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HttpClient (учитывает прокси из KI-002)</param>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        public LmStudioHealthCheck(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<LmStudioHealthCheck> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_configuration.GetValue("HealthChecks:LmStudio:Enabled", true))
            {
                return HealthCheckResult.Healthy("Проверка LM Studio отключена (HealthChecks:LmStudio:Enabled = false)");
            }

            var baseUrl = _configuration["LmStudio:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return HealthCheckResult.Degraded("LmStudio:BaseUrl не задан в конфигурации");
            }

            var url = baseUrl.TrimEnd('/') + "/v1/models";

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Healthy($"LM Studio доступен: {baseUrl}");
                }

                _logger.LogWarning("LM Studio вернул {StatusCode}", response.StatusCode);
                return HealthCheckResult.Degraded($"LM Studio ответил {(int)response.StatusCode} {response.StatusCode}");
            }
            catch (TaskCanceledException)
            {
                // Timeout (HttpClient.Timeout) — не логируем как ошибку, это ожидаемо
                return HealthCheckResult.Degraded($"LM Studio не отвечает (timeout): {baseUrl}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "LM Studio недоступен: {Url}", url);
                return HealthCheckResult.Degraded($"LM Studio недоступен: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при проверке LM Studio");
                return HealthCheckResult.Degraded("Ошибка проверки LM Studio", ex);
            }
        }
    }
}