using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Скелет External Vision LLM — мультимодальный вызов внешних провайдеров
    /// (OpenAI GPT-4o, Anthropic Claude, Google Gemini).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.3). См. DESIGN § 3.2, § 5.1.
    /// </para>
    /// <para>
    /// <b>НЕ реализован в v1.12.0.</b> Причина: <see cref="IExternalLlmClient"/>
    /// (v1.8.1, KI-109) — <b>text-only</b>. Поддержка изображений требует
    /// расширения <c>ExternalLlmRequest</c> полем <c>ImageBase64DataUrl</c> +
    /// <b>трёх разных билдеров</b> в <c>ExternalLlmClient</c>
    /// (у OpenAI / Anthropic / Gemini — свои форматы multimodal content).
    /// Это отдельная фича → заведён <b>KI-141</b> (Planned, v1.12.x).
    /// </para>
    /// <para>
    /// <b>Поведение сейчас:</b> <see cref="DescribeAsync"/> бросает
    /// <see cref="NotSupportedException"/>. <c>IsReady = false</c>, чтобы
    /// <c>AutoVisionClient</c> (Ф5.4) не тратил время на попытку.
    /// </para>
    /// </remarks>
    public sealed class ExternalVisionClient : IVisionLlmClient
    {
        private readonly VisionLlmOptions _visionOptions;
        private readonly IExternalLlmClient _externalClient;
        private readonly IExternalProviderRegistry _registry;
        private readonly ILogger<ExternalVisionClient> _logger;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent.</param>
        /// <param name="externalClient">Клиент внешних LLM (v1.8.1, KI-109).</param>
        /// <param name="registry">Реестр провайдеров (для проверки SupportsVision).</param>
        /// <param name="logger">Логгер.</param>
        public ExternalVisionClient(
            IOptions<VisionAgentOptions> options,
            IExternalLlmClient externalClient,
            IExternalProviderRegistry registry,
            ILogger<ExternalVisionClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _visionOptions = options.Value.VisionLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:VisionLlm не задана.");
            _externalClient = externalClient ?? throw new ArgumentNullException(nameof(externalClient));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsReady
        {
            get
            {
                // Готов, если FallbackChain содержит хотя бы один external:*.
                // Реальная проверка доступности провайдера — в DescribeAsync.
                var chain = _visionOptions.FallbackChain ?? new List<string>();
                return chain.Any(e => !string.IsNullOrWhiteSpace(e)
                    && e.Trim().StartsWith("external:", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <inheritdoc />
        public async Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            if (screenshotPng == null || screenshotPng.Length == 0)
                throw new ArgumentException("PNG-скриншот пуст.", nameof(screenshotPng));

            var providerName = ResolveProviderName();
            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new InvalidOperationException(
                    "ExternalVisionClient: FallbackChain не содержит external:*.");
            }

            var provider = _registry.Get(providerName);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"ExternalVisionClient: провайдер '{providerName}' не зарегистрирован.");
            }

            if (!provider.SupportsVision)
            {
                throw new InvalidOperationException(
                    $"ExternalVisionClient: провайдер '{providerName}' имеет " +
                    $"SupportsVision = false. Включите в appsettings.json.");
            }

            // v1.13.9 (KI-141): формируем multimodal-запрос.
            var image = new ExternalLlmImage
            {
                MimeType = "image/png",
                Base64Data = Convert.ToBase64String(screenshotPng)
            };

            var request = new ExternalLlmRequest
            {
                Provider = providerName,
                System = VisionSystemPrompt.VisionUiDescribe,
                Prompt = "Опиши UI на скриншоте. Ответ — строго JSON, без markdown-обёртки.",
                Images = new[] { image },
                MaxTokens = _visionOptions.MaxTokens,
                Temperature = _visionOptions.Temperature
            };

            _logger.LogDebug(
                "ExternalVisionClient: DescribeAsync — provider={Provider}, pngBytes={Bytes}",
                providerName, screenshotPng.Length);

            var response = await _externalClient.CompleteAsync(
                userId: 0,                       // 0 = системный (budget tracker)
                request,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogWarning(
                    "ExternalVisionClient: провайдер '{Provider}' вернул пустой content.",
                    providerName);
                return new ScreenDescriptionDto
                {
                    Description = string.Empty,
                    UiElements = new List<UiElementDto>()
                };
            }

            var result = ScreenDescriptionParser.Parse(response.Content);

            _logger.LogDebug(
                "ExternalVisionClient: '{Provider}' — descLen={DescLen}, ui={UiCount}, " +
                "tokens={Prompt}+{Completion}, cost=${Cost}",
                providerName, result.Description?.Length ?? 0,
                result.UiElements?.Count ?? 0,
                response.PromptTokens, response.CompletionTokens, response.CostUsd);

            return result;
        }

        /// <inheritdoc />
        /// <remarks>
        /// KI-141 (v1.13.9): VerifyTarget для external пока не реализован —
        /// требует второго multimodal-вызова с кропом. Отложено до
        /// v1.14 (когда будет протестирована точность Yandex VL на кропах).
        /// </remarks>
        public Task<VerifyTargetResultDto> VerifyTargetAsync(
            byte[] croppedPng,
            string targetDescription,
            UiElementBoundsDto originalBounds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new VerifyTargetResultDto
            {
                Found = false,
                Error = "External Vision LLM не поддерживает VerifyTarget " +
                        "(в работе — v1.14)."
            });
        }

        /// <summary>
        /// Извлекает имя провайдера из FallbackChain (первый <c>external:*</c>).
        /// </summary>
        private string ResolveProviderName()
        {
            var chain = _visionOptions.FallbackChain;
            if (chain == null) return null;

            foreach (var entry in chain)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var trimmed = entry.Trim();

                if (trimmed.StartsWith("external:", StringComparison.OrdinalIgnoreCase))
                {
                    // "external:yandex-vl" → "yandex-vl"
                    return trimmed.Substring("external:".Length).Trim();
                }
            }
            return null;
        }
    }
}