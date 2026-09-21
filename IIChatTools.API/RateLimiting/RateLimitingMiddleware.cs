using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.RateLimiting
{
    /// <summary>
    /// Собственная реализация rate-limiting middleware.
    /// Использует <c>System.Threading.RateLimiting</c> напрямую, обходя
    /// <c>AddRateLimiter</c> (недоступен в SDK 10.0.401 из shared framework).
    ///
    /// Определяет политики:
    /// - <c>per-user</c>: 100 req/min по UserId (fallback: IP) — все API;
    /// - <c>tools-execute</c>: 30 req/min по UserId — <c>/api/tools/execute</c>;
    /// - <c>auth</c>: 5 req/min по IP — <c>/auth/*</c> (brute-force);
    /// - <c>/health/*</c> — без лимита (Docker healthcheck).
    ///
    /// Настраивается секцией <c>RateLimiting</c> в appsettings.json.
    /// </summary>
    public sealed class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;
        private readonly RateLimitingOptions _options;

        // Кеш лимитеров по partition-key.
        // Внимание: для production с несколькими инстансами нужен распределённый лимитер.
        private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters =
            new ConcurrentDictionary<string, FixedWindowRateLimiter>();

        // Кэш JSON-опций — исправляет CA1869 (см. KI-051).
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        
        /// <summary>
        /// Создаёт middleware.
        /// </summary>
        /// <param name="next">Следующий middleware в конвейере</param>
        /// <param name="configuration">Конфигурация приложения (секция <c>RateLimiting</c>)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public RateLimitingMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            ILogger<RateLimitingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _options = new RateLimitingOptions();
            configuration.GetSection("RateLimiting").Bind(_options);
        }

        /// <summary>
        /// Обрабатывает HTTP-запрос: определяет политику, проверяет лимит, вызывает следующий middleware.
        /// </summary>
        /// <param name="context">HTTP-контекст</param>
        /// <returns>Асинхронная задача</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (!_options.Enabled)
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Устанавливаем текущий контекст для helpers
            _currentContext.Value = context;
            try
            {
                var policy = SelectPolicy(
                    path,
                    context.Request.Method,
                    out var policyOptions,
                    out var partitionKey);

                // null = политика не применяется (например, GET /auth/login)
                if (policy == null)
                {
                    await _next(context);
                    return;
                }

                var limiter = _limiters.GetOrAdd($"{policy}:{partitionKey}", _ =>
                    new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = policyOptions.PermitLimit,
                        Window = TimeSpan.FromSeconds(policyOptions.WindowSeconds),
                        QueueLimit = policyOptions.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

                using var lease = limiter.AttemptAcquire(1);

                if (!lease.IsAcquired)
                {
                    _logger.LogWarning(
                        "Rate limit exceeded: policy={Policy}, key={Key}, method={Method}, path={Path}, ip={Ip}",
                        policy, partitionKey, context.Request.Method, path,
                        context.Connection.RemoteIpAddress);

                    var retryAfter = policyOptions.WindowSeconds;

                    // Проверяем Accept: HTML-браузер или API-клиент.
                    // Браузер (форма) → 303 redirect на форму с баннером.
                    // API (JSON) → 429 с JSON-ответом.
                    var accept = context.Request.Headers["Accept"].ToString();
                    var isHtmlRequest = accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);

                    if (isHtmlRequest)
                    {
                        // Редиректим на GET-версию того же пути + маркеры ошибки.
                        // 303 See Other — корректно превращает POST в GET.
                        var currentPath = context.Request.Path.Value ?? "/";
                        var redirectUrl = $"{currentPath}?error=ratelimit&retryAfter={retryAfter}";

                        context.Response.StatusCode = StatusCodes.Status303SeeOther;
                        context.Response.Headers["Location"] = redirectUrl;
                        return;
                    }

                    // API-клиент — JSON
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.Headers["Retry-After"] = retryAfter.ToString();

                    var payload = new
                    {
                        success = false,
                        message = "Слишком много запросов. Попробуйте позже.",
                        retryAfterSeconds = retryAfter
                    };
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(payload, JsonOptions));
                    return;
                }

                await _next(context);
            }
            finally
            {
                _currentContext.Value = null;
            }
        }
        
        /// <summary>
        /// Определяет политику для пути и вычисляет ключ партиции.
        /// </summary>
        /// <param name="path">Путь запроса</param>
        /// <param name="httpMethod">HTTP-метод (GET, POST и т. д.)</param>
        /// <param name="policyOptions">Возвращаемые параметры выбранной политики</param>
        /// <param name="partitionKey">Возвращаемый ключ партиции</param>
        /// <returns>Имя политики или <c>null</c>, если лимит не применяется</returns>
        private string SelectPolicy(
            string path,
            string httpMethod,
            out RateLimitPolicyOptions policyOptions,
            out string partitionKey)
        {
            var userId = GetUserIdFromHttpContext();
            var ip = GetIpFromHttpContext();

            // /auth/* — строгая, по IP.
            // ВАЖНО: только POST. GET-формы (login, register) не лимитируем,
            // иначе после redirect на ?error=ratelimit возникнет цикл редиректов.
            if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase))
            {
                if (HttpMethods.IsGet(httpMethod) || HttpMethods.IsHead(httpMethod))
                {
                    // Форма открывается без лимита
                    policyOptions = null;
                    partitionKey = null;
                    return null;
                }

                policyOptions = _options.Auth;
                partitionKey = $"ip:{ip}";
                return "auth";
            }

            // /api/tools/execute — строгая, по UserId
            if (path.StartsWith("/api/tools/execute", StringComparison.OrdinalIgnoreCase))
            {
                policyOptions = _options.ToolsExecute;
                partitionKey = userId != null ? $"user:{userId}" : $"ip:{ip}";
                return "tools-execute";
            }

            // Всё остальное — per-user
            policyOptions = _options.PerUser;
            partitionKey = userId != null ? $"user:{userId}" : $"ip:{ip}";
            return "per-user";
        }

        /// <summary>
        /// Извлекает UserId из claims (если пользователь аутентифицирован).
        /// </summary>
        /// <returns>UserId или null</returns>
        private string GetUserIdFromHttpContext()
        {
            // Пытаемся через HttpContext.Current — но у нас нет прямого доступа.
            // Используем AsyncLocal для передачи контекста (см. InvokeAsync).
            return _currentContext.Value?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        /// <summary>
        /// Извлекает IP из текущего HttpContext.
        /// </summary>
        private string GetIpFromHttpContext()
        {
            return _currentContext.Value?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }

        // AsyncLocal для доступа к HttpContext в методах SelectPolicy/GetUserId
        private static readonly AsyncLocal<HttpContext> _currentContext = new AsyncLocal<HttpContext>();
    }
}