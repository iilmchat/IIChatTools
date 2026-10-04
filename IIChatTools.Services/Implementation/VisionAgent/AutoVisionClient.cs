using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Vision LLM с fallback chain — перебирает провайдеров из
    /// <see cref="VisionLlmOptions.FallbackChain"/>, пока один не ответит.
    /// Singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф5.4). См. DESIGN § 3.2, § 5.1.
    /// </para>
    /// <para>
    /// <b>Резолв цепочки:</b>
    /// <list type="bullet">
    ///   <item><c>"lmstudio"</c> → результат <c>lmStudioFactory</c>;</item>
    ///   <item><c>"external:{name}"</c> → результат <c>externalFactory</c>
    ///   (все external:* идут в один экземпляр — он сам резолвит провайдера).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Почему Func, а не конкретные типы:</b> паттерн ADR-002 — разрывает
    /// жёсткую зависимость на конкретные классы, позволяет подменить
    /// реализацию в тестах (fake-фабрики вместо IHttpClientFactory mock).
    /// </para>
    /// <para>
    /// <b>Критерий успеха:</b> <see cref="IVisionLlmClient.DescribeAsync"/>
    /// вернул непустое <c>Description</c> ИЛИ непустой список <c>UiElements</c>.
    /// Пустой результат → пробуем следующего в цепочке.
    /// </para>
    /// </remarks>
    public sealed class AutoVisionClient : IVisionLlmClient
    {
        private readonly VisionLlmOptions _options;
        private readonly Func<IVisionLlmClient> _lmStudioFactory;
        private readonly Func<IVisionLlmClient> _externalFactory;
        private readonly ILogger<AutoVisionClient> _logger;

        // Lazy-резолв: клиенты создаются при первом обращении.
        private IVisionLlmClient _lmStudioInstance;
        private IVisionLlmClient _externalInstance;

        /// <summary>
        /// Создаёт клиент.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent:VisionLlm</c>).</param>
        /// <param name="lmStudioFactory">Фабрика LmStudio-клиента (lazy).</param>
        /// <param name="externalFactory">Фабрика External-клиента (lazy).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public AutoVisionClient(
            IOptions<VisionAgentOptions> options,
            Func<IVisionLlmClient> lmStudioFactory,
            Func<IVisionLlmClient> externalFactory,
            ILogger<AutoVisionClient> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value.VisionLlm
                ?? throw new InvalidOperationException("Секция VisionAgent:VisionLlm не задана.");
            _lmStudioFactory = lmStudioFactory ?? throw new ArgumentNullException(nameof(lmStudioFactory));
            _externalFactory = externalFactory ?? throw new ArgumentNullException(nameof(externalFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public bool IsReady => AnyReady();

        /// <inheritdoc />
        public async Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default)
        {
            var chain = BuildChain();
            if (chain.Count == 0)
            {
                throw new InvalidOperationException(
                    "AutoVisionClient: FallbackChain пуст. Настройте VisionAgent:VisionLlm:FallbackChain.");
            }

            Exception lastException = null;

            for (int i = 0; i < chain.Count; i++)
            {
                var entry = chain[i];

                if (!entry.Client.IsReady)
                {
                    _logger.LogDebug(
                        "AutoVisionClient: [{Index}/{Total}] '{Name}' не готов — пропуск",
                        i + 1, chain.Count, entry.EntryName);
                    continue;
                }

                try
                {
                    var result = await entry.Client
                        .DescribeAsync(screenshotPng, cancellationToken)
                        .ConfigureAwait(false);

                    if (IsMeaningful(result))
                    {
                        _logger.LogDebug(
                            "AutoVisionClient: успех на '{Name}' (desc={DescLen}, ui={UiCount})",
                            entry.EntryName,
                            result.Description?.Length ?? 0,
                            result.UiElements?.Count ?? 0);
                        return result;
                    }

                    _logger.LogDebug(
                        "AutoVisionClient: '{Name}' вернул пустой результат — fallback",
                        entry.EntryName);
                }
                catch (NotSupportedException ex)
                {
                    // External Vision сейчас — скелет, ожидаемо.
                    _logger.LogDebug(
                        "AutoVisionClient: '{Name}' не поддерживается — fallback ({Msg})",
                        entry.EntryName, ex.Message);
                    lastException = ex;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogWarning(ex,
                        "AutoVisionClient: '{Name}' упал — fallback", entry.EntryName);
                    lastException = ex;
                }
            }

            if (lastException != null)
            {
                throw new InvalidOperationException(
                    $"AutoVisionClient: все {chain.Count} провайдеров упали. " +
                    $"Последняя ошибка: {lastException.Message}", lastException);
            }

            throw new InvalidOperationException(
                $"AutoVisionClient: ни один из {chain.Count} провайдеров не готов. " +
                "Проверьте VisionAgent:VisionLlm:FallbackChain и доступность LM Studio / External API-ключей.");
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Резолвит FallbackChain в список пар (EntryName, Client).
        /// Клиенты резолвятся лениво (кешируются).
        /// </summary>
        private List<(string EntryName, IVisionLlmClient Client)> BuildChain()
        {
            var result = new List<(string, IVisionLlmClient)>();
            var chain = _options.FallbackChain ?? new List<string>();

            foreach (var entry in chain)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var trimmed = entry.Trim();

                if (trimmed.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((trimmed, ResolveLmStudio()));
                }
                else if (trimmed.StartsWith("external:", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((trimmed, ResolveExternal()));
                }
                else
                {
                    _logger.LogWarning(
                        "AutoVisionClient: неизвестный элемент FallbackChain '{Entry}' — пропуск",
                        entry);
                }
            }
            return result;
        }

        private bool AnyReady()
        {
            var chain = BuildChain();
            foreach (var e in chain)
            {
                if (e.Client.IsReady) return true;
            }
            return false;
        }

        private IVisionLlmClient ResolveLmStudio()
        {
            return _lmStudioInstance ??= _lmStudioFactory();
        }

        private IVisionLlmClient ResolveExternal()
        {
            return _externalInstance ??= _externalFactory();
        }

        /// <summary>
        /// «Осмысленный» результат: непустое Description или непустой UiElements.
        /// </summary>
        private static bool IsMeaningful(ScreenDescriptionDto result)
        {
            if (result == null) return false;
            if (!string.IsNullOrWhiteSpace(result.Description)) return true;
            if (result.UiElements != null && result.UiElements.Count > 0) return true;
            return false;
        }
    }
}