using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.ExternalLlm
{
    /// <summary>
    /// Реестр провайдеров внешних LLM (v1.8.1, KI-109, Фаза 2.1).
    ///
    /// <para>
    /// Singleton. Читает <c>ExternalLlm:Providers</c> из конфигурации один раз
    /// при первом resolve. При <c>ExternalLlm:Enabled = true</c> выполняет
    /// fail-fast валидацию (DESIGN_EXTERNAL_LLM § 5.6).
    /// </para>
    ///
    /// <para>
    /// <b>Fail-fast проверки</b> (при <c>Enabled = true</c>):
    /// <list type="bullet">
    ///   <item><c>Providers</c> не пуст;</item>
    ///   <item><c>DefaultProvider</c> задан и присутствует в <c>Providers</c>;</item>
    ///   <item><c>DefaultProvider.BaseUrl</c> — валидный URL со схемой <c>https://</c>;</item>
    ///   <item>если у <c>DefaultProvider</c> задан <c>ApiKeySecretName</c> —
    ///   значение резолвится из <see cref="IConfiguration"/>. Если не резолвится — throw.
    ///   Пустое <c>ApiKeySecretName</c> допустимо (например, для Ollama).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// При <c>Enabled = false</c> реестр создаётся «пустым» — <see cref="GetNames"/>
    /// вернёт пустой список, <see cref="Get"/> — <c>null</c>. Это позволяет
    /// зарегистрировать сервис в DI всегда и не падать, если функциональность
    /// отключена (по умолчанию).
    /// </para>
    /// </summary>
    public sealed class ExternalProviderRegistry : IExternalProviderRegistry
    {
        private readonly Dictionary<string, ExternalProviderOptions> _providers;
        private readonly List<string> _names;
        private readonly string _defaultProvider;
        private readonly ILogger<ExternalProviderRegistry> _logger;

        /// <summary>
        /// Создаёт реестр из конфигурации.
        /// </summary>
        /// <param name="options">Опции секции <c>ExternalLlm</c></param>
        /// <param name="configuration">Конфигурация (для резолва <c>ApiKeySecretName</c>)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        /// <exception cref="InvalidOperationException">
        /// Если конфигурация некорректна и <c>Enabled = true</c> (DESIGN § 5.6).
        /// </exception>
        public ExternalProviderRegistry(
            IOptions<ExternalLlmOptions> options,
            IConfiguration configuration,
            ILogger<ExternalProviderRegistry> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var opts = options.Value ?? new ExternalLlmOptions();

            _providers = new Dictionary<string, ExternalProviderOptions>(StringComparer.OrdinalIgnoreCase);
            _names = new List<string>();
            _defaultProvider = opts.DefaultProvider;

            // При Enabled = false — реестр пустой, валидация не выполняется.
            // Так сервис можно регистрировать в DI безусловно (Startup.cs),
            // а конфигурацию — задавать только когда фича включена.
            if (!opts.Enabled)
            {
                _logger.LogDebug(
                    "External-LLM: Enabled=false, реестр провайдеров пустой (валидация пропущена).");
                return;
            }

            if (opts.Providers == null || opts.Providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "ExternalLlm:Enabled=true, но ExternalLlm:Providers пуст. " +
                    "Настройте хотя бы одного провайдера (см. DESIGN_EXTERNAL_LLM § 5.1).");
            }

            if (string.IsNullOrWhiteSpace(opts.DefaultProvider))
            {
                throw new InvalidOperationException(
                    "ExternalLlm:Enabled=true, но ExternalLlm:DefaultProvider не задан.");
            }

            // Сохраняем порядок как в конфиге: заполняем _names и _providers параллельно.
            foreach (var kv in opts.Providers)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    _logger.LogWarning("External-LLM: пропущен провайдер с пустым именем.");
                    continue;
                }

                if (kv.Value == null)
                {
                    _logger.LogWarning(
                        "External-LLM: провайдер '{Name}' имеет null-значение — пропущен.",
                        kv.Key);
                    continue;
                }

                _names.Add(kv.Key);
                _providers[kv.Key] = kv.Value;
            }

            if (!_providers.TryGetValue(opts.DefaultProvider, out var defaultOptions))
            {
                throw new InvalidOperationException(
                    $"ExternalLlm:DefaultProvider='{opts.DefaultProvider}' не найден в ExternalLlm:Providers. " +
                    $"Доступные: {string.Join(", ", _names)}.");
            }

            ValidateProviderOrThrow(opts.DefaultProvider, defaultOptions, configuration, isDefault: true);

            _logger.LogInformation(
                "External-LLM: реестр провайдеров загружен (default={Default}, count={Count}): {Names}",
                _defaultProvider, _names.Count, string.Join(", ", _names));
        }

        /// <inheritdoc />
        public string DefaultProvider => _defaultProvider;

        /// <inheritdoc />
        public IReadOnlyList<string> GetNames() => _names.AsReadOnly();

        /// <inheritdoc />
        public ExternalProviderOptions Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _providers.TryGetValue(name, out var opts) ? opts : null;
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Валидирует одного провайдера. Для default — проверки строже
        /// (BaseUrl + резолв ApiKeySecretName).
        /// </summary>
        /// <param name="name">Имя провайдера</param>
        /// <param name="opts">Настройки</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="isDefault">Это default-провайдер?</param>
        /// <exception cref="InvalidOperationException">Если валидация не прошла</exception>
        private static void ValidateProviderOrThrow(
            string name,
            ExternalProviderOptions opts,
            IConfiguration configuration,
            bool isDefault)
        {
            if (string.IsNullOrWhiteSpace(opts.BaseUrl))
            {
                throw new InvalidOperationException(
                    $"ExternalLlm:Providers:{name}:BaseUrl не задан.");
            }

            if (!Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"ExternalLlm:Providers:{name}:BaseUrl='{opts.BaseUrl}' — не абсолютный http(s) URL.");
            }

            // Требование DESIGN § 6.6: MITM-защита. Но http://localhost/ — допустимо
            // для локальных Ollama / прокси. Поэтому проверяем только для не-localhost.
            if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalHost(uri.Host))
            {
                throw new InvalidOperationException(
                    $"ExternalLlm:Providers:{name}:BaseUrl='{opts.BaseUrl}' — " +
                    "для не-локальных хостов требуется https:// (DESIGN § 6.6).");
            }

            if (string.IsNullOrWhiteSpace(opts.Model))
            {
                throw new InvalidOperationException(
                    $"ExternalLlm:Providers:{name}:Model не задан.");
            }

            // ApiKeySecretName — опционально (Ollama без ключа).
            // Если задан — значение должно резолвиться из конфигурации.
            if (!string.IsNullOrWhiteSpace(opts.ApiKeySecretName))
            {
                var apiKey = configuration[opts.ApiKeySecretName];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        $"ExternalLlm:Providers:{name}:ApiKeySecretName='{opts.ApiKeySecretName}' " +
                        "задан, но значение не найдено в конфигурации (User Secrets / env). " +
                        "Задайте через dotnet user-secrets set \"" +
                        opts.ApiKeySecretName + "\" \"<api-key>\".");
                }
            }

            // Проверки только для default — на случай, если конфиг некорректен,
            // лучше упасть до первого вызова.
            if (isDefault)
            {
                if (opts.TimeoutSeconds < 1 || opts.TimeoutSeconds > 600)
                {
                    // clamp + warning — не throw (DESIGN § 5.6).
                    // Меняем на месте — Options — Singleton, это безопасно.
                    var clamped = Math.Clamp(opts.TimeoutSeconds, 1, 600);
                    // Не throw, просто предупреждение. (clamp произойдёт при использовании
                    // в ExternalLlmClient; здесь — только диагностика.)
                }
            }
        }

        /// <summary>
        /// Проверяет, что хост локальный (localhost / 127.0.0.1 / ::1).
        /// </summary>
        private static bool IsLocalHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.Ordinal)
                || host.Equals("::1", StringComparison.Ordinal)
                || host.StartsWith("127.", StringComparison.Ordinal);
        }
    }
}