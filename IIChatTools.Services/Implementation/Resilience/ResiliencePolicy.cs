using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Resilience
{
    /// <summary>
    /// D03 / KI-265: единая политика устойчивости для внешних вызовов.
    /// Timeout привязан к запросу (CancellationTokenSource.CancelAfter),
    /// retry — exponential backoff с jitter, внешняя отмена всегда пробрасывается.
    /// <para>
    /// Соответствует RULES § 4.28 (явный timeout + 1 retry для HTTP) и
    /// § 4.48 (не меняем HttpClient.Timeout после первого запроса).
    /// </para>
    /// </summary>
    public static class ResiliencePolicy
    {
        /// <summary>Настройки политики.</summary>
        public sealed class Options
        {
            /// <summary>Максимум попыток (включая первую).</summary>
            public int MaxAttempts { get; set; } = 2;

            /// <summary>Таймаут одной попытки. 0 = без таймаута.</summary>
            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

            /// <summary>Базовая задержка перед повтором (удваивается каждый раз).</summary>
            public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Выполняет операцию с retry; transient-исключения повторяются,
        /// внешняя отмена не перехватывается.
        /// </summary>
        /// <typeparam name="T">Тип результата</typeparam>
        /// <param name="operation">Операция (принимает токен попытки)</param>
        /// <param name="options">Настройки политики</param>
        /// <param name="outerToken">Внешний токен отмены</param>
        /// <param name="logger">Логгер (опционально)</param>
        /// <returns>Результат операции</returns>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Options options,
            CancellationToken outerToken,
            ILogger logger = null)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            options ??= new Options();
            var attempts = Math.Max(1, options.MaxAttempts);

            Exception lastError = null;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                outerToken.ThrowIfCancellationRequested();

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
                if (options.Timeout > TimeSpan.Zero)
                    attemptCts.CancelAfter(options.Timeout);

                try
                {
                    return await operation(attemptCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (outerToken.IsCancellationRequested)
                {
                    throw;   // внешняя отмена (Stop) — не retry
                }
                catch (Exception ex)
                {
                    if (attempt >= attempts || !IsTransient(ex))
                        throw;   // последняя попытка или не-transient — пробрасываем

                    lastError = ex;
                    var delayMs = (int)(options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    var jitter = Random.Shared.Next(0, Math.Max(1, delayMs / 4));
                    logger?.LogWarning(ex,
                        "Resilience: transient error on attempt {Attempt}/{Max}, retry in {Delay}ms",
                        attempt, attempts, delayMs + jitter);
                    await Task.Delay(delayMs + jitter, outerToken).ConfigureAwait(false);
                }
            }

            throw lastError ?? new InvalidOperationException("Resilience: попытки исчерпаны.");
        }

        /// <summary>
        /// Является ли исключение transient (подлежит повтору).
        /// </summary>
        private static bool IsTransient(Exception ex)
        {
            return ex is System.Net.Http.HttpRequestException
                || ex is TimeoutException
                || ex is System.IO.IOException;
        }
    }
}
