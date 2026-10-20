using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IIChatTools.Services.DTO.Orchestrator;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Orchestrator
{
    /// <summary>
    /// Провайдер актуальных настроек Orchestrator-Worker паттерна
    /// (v2.2.2, KI-245, DESIGN_ORCHESTRATOR_WORKER § 7).
    /// <para>
    /// Хранит <b>baseline</b> из <c>appsettings.json</c> (секция
    /// <c>SubAgents:orchestrate_task</c>, читается один раз в конструкторе)
    /// и <b>runtime-overrides</b>, применяемые из админки
    /// (<c>AdminOrchestratorService</c>) или при старте
    /// (<c>Program.LoadOrchestratorOverridesAsync</c>).
    /// </para>
    /// <para>
    /// <b>Потокобезопасность:</b> baseline иммутабелен, runtime-override хранится
    /// в <see cref="ConcurrentDictionary{TKey,TValue}"/> как единый неизменяемый
    /// снимок (ключ <c>current</c>). Замена снимка атомарна, поэтому
    /// <see cref="Get"/> никогда не видит «полуобновлённые» настройки.
    /// </para>
    /// <para>
    /// <b>RULES § 4.56:</b> конфигурация читается только через indexer
    /// (<c>_configuration[key]</c>) + <c>TryParse</c>; <c>GetValue&lt;T&gt;</c>
    /// не используется. Для массива воркеров — <c>GetSection(...).GetChildren()</c>.
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Singleton. Потребители читают актуальные значения
    /// при каждом вызове (не кэшируют).
    /// </para>
    /// </summary>
    public class OrchestratorOptionsProvider : IOrchestratorOptionsProvider
    {
        /// <summary>Путь секции конфигурации с baseline-настройками.</summary>
        private const string SectionPath = "SubAgents:orchestrate_task";

        /// <summary>Ключ единственного runtime-снимка в словаре override'ов.</summary>
        private const string RuntimeOverrideKey = "current";

        /// <summary>Минимальное число подзадач.</summary>
        public const int MinMaxSubtasks = 1;

        /// <summary>Максимальное число подзадач (hard limit, DESIGN § 2.4).</summary>
        public const int MaxMaxSubtasks = 10;

        /// <summary>Минимальный параллелизм.</summary>
        public const int MinMaxParallel = 1;

        /// <summary>Максимальный параллелизм.</summary>
        public const int MaxMaxParallel = 10;

        /// <summary>Минимальный бюджет токенов.</summary>
        public const int MinTokenBudget = 1_000;

        /// <summary>Максимальный бюджет токенов.</summary>
        public const int MaxTokenBudget = 1_000_000;

        /// <summary>Максимальное число агентов в whitelist (защита от мусора).</summary>
        public const int MaxWorkerAgents = 50;

        /// <summary>Значение по умолчанию для <c>MaxSubtasks</c>.</summary>
        public const int DefaultMaxSubtasks = 5;

        /// <summary>Значение по умолчанию для <c>MaxParallel</c>.</summary>
        public const int DefaultMaxParallel = 3;

        /// <summary>Значение по умолчанию для <c>TokenBudget</c>.</summary>
        public const int DefaultTokenBudget = 50_000;

        /// <summary>Значение по умолчанию для <c>HumanApproval</c>.</summary>
        public const string DefaultHumanApproval = "Never";

        /// <summary>Допустимые режимы Human-in-the-loop (канонический вид).</summary>
        private static readonly string[] AllowedHumanApprovalValues =
        {
            "Never",
            "BetweenSubtasks",
            "AtEnd"
        };

        /// <summary>
        /// Whitelist воркеров по умолчанию (совпадает с fallback из
        /// <c>OrchestrateTaskTool.ReadConfig</c> до v2.2.2).
        /// </summary>
        private static readonly string[] DefaultAllowedWorkerAgents =
        {
            "code_agent",
            "web_agent",
            "file_system_agent",
            "database_agent",
            "browser_agent",
            "external_llm_agent"
        };

        private readonly OrchestratorSettingsDto _baseline;
        private readonly ILogger<OrchestratorOptionsProvider> _logger;

        /// <summary>
        /// Runtime-override: единый неизменяемый снимок настроек.
        /// Замена ссылки атомарна; отсутствие ключа = работать по baseline.
        /// </summary>
        private readonly ConcurrentDictionary<string, OrchestratorSettingsDto> _runtimeOverride =
            new ConcurrentDictionary<string, OrchestratorSettingsDto>(StringComparer.Ordinal);

        /// <summary>
        /// Создаёт провайдер, читает baseline из конфигурации и нормализует его.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если любой параметр = null</exception>
        public OrchestratorOptionsProvider(
            IConfiguration configuration,
            ILogger<OrchestratorOptionsProvider> logger)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseline = ReadBaseline(configuration);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Возвращает <c>false</c>, если сохранённый override совпадает с baseline
        /// по всем полям (например, админ нажал «Сохранить», не изменив значений).
        /// </remarks>
        public bool IsOverridden => HasEffectiveOverride();

        /// <inheritdoc />
        public OrchestratorSettingsDto Get()
        {
            if (_runtimeOverride.TryGetValue(RuntimeOverrideKey, out var runtime))
            {
                var copy = Clone(runtime);
                // v2.2.2-fix (verification): «изменено» = отличается от baseline,
                // а не «override-снимок существует».
                copy.IsOverridden = !AreEqual(runtime, _baseline);
                return copy;
            }

            var baseline = Clone(_baseline);
            baseline.IsOverridden = false;
            return baseline;
        }

        /// <inheritdoc />
        public void Update(OrchestratorSettingsDto settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // Нормализация с жёсткой валидацией: некорректные значения,
            // пришедшие из админки, должны вернуть ArgumentException.
            var normalized = NormalizeForUpdate(settings);
            normalized.IsOverridden = true;

            // Атомарная замена снимка: читатели видят либо старый, либо новый.
            _runtimeOverride[RuntimeOverrideKey] = normalized;

            _logger.LogInformation(
                "Orchestrator override обновлён: maxSubtasks={MaxSubtasks}, " +
                "maxParallel={MaxParallel}, tokenBudget={TokenBudget}, " +
                "humanApproval={HumanApproval}, workers={Workers}",
                normalized.MaxSubtasks, normalized.MaxParallel, normalized.TokenBudget,
                normalized.HumanApproval, normalized.AllowedWorkerAgents.Count);
        }

        /// <inheritdoc />
        public void Reset()
        {
            _runtimeOverride.TryRemove(RuntimeOverrideKey, out _);
            _logger.LogInformation("Orchestrator override сброшен к baseline.");
        }

        // ============ Private: baseline ============

        /// <summary>
        /// Читает baseline из секции <c>SubAgents:orchestrate_task</c>.
        /// Отсутствующие/некорректные значения заменяются дефолтами,
        /// числовые — зажимаются (clamp) в допустимый диапазон.
        /// </summary>
        private OrchestratorSettingsDto ReadBaseline(IConfiguration configuration)
        {
            var maxSubtasks = ReadInt(configuration,
                $"{SectionPath}:MaxSubtasks", DefaultMaxSubtasks);
            if (maxSubtasks < MinMaxSubtasks || maxSubtasks > MaxMaxSubtasks)
            {
                var clamped = Math.Clamp(maxSubtasks, MinMaxSubtasks, MaxMaxSubtasks);
                _logger.LogWarning(
                    "Orchestrator:MaxSubtasks={Old} вне [{Min}..{Max}] — clamp до {New}.",
                    maxSubtasks, MinMaxSubtasks, MaxMaxSubtasks, clamped);
                maxSubtasks = clamped;
            }

            var maxParallel = ReadInt(configuration,
                $"{SectionPath}:MaxParallel", DefaultMaxParallel);
            if (maxParallel < MinMaxParallel || maxParallel > MaxMaxParallel)
            {
                var clamped = Math.Clamp(maxParallel, MinMaxParallel, MaxMaxParallel);
                _logger.LogWarning(
                    "Orchestrator:MaxParallel={Old} вне [{Min}..{Max}] — clamp до {New}.",
                    maxParallel, MinMaxParallel, MaxMaxParallel, clamped);
                maxParallel = clamped;
            }

            var tokenBudget = ReadInt(configuration,
                $"{SectionPath}:TokenBudget", DefaultTokenBudget);
            if (tokenBudget < MinTokenBudget || tokenBudget > MaxTokenBudget)
            {
                var clamped = Math.Clamp(tokenBudget, MinTokenBudget, MaxTokenBudget);
                _logger.LogWarning(
                    "Orchestrator:TokenBudget={Old} вне [{Min}..{Max}] — clamp до {New}.",
                    tokenBudget, MinTokenBudget, MaxTokenBudget, clamped);
                tokenBudget = clamped;
            }

            var humanApprovalRaw = configuration[$"{SectionPath}:HumanApproval"];
            var humanApproval = NormalizeHumanApproval(humanApprovalRaw, throwOnInvalid: false);

            var allowed = ReadWorkerAgents(configuration);
            if (allowed.Count == 0)
            {
                allowed = DefaultAllowedWorkerAgents.ToList();
                _logger.LogWarning(
                    "Orchestrator:AllowedWorkerAgents пуст или отсутствует — " +
                    "использован whitelist по умолчанию ({Count} агентов).",
                    allowed.Count);
            }

            return new OrchestratorSettingsDto
            {
                MaxSubtasks = maxSubtasks,
                MaxParallel = maxParallel,
                TokenBudget = tokenBudget,
                HumanApproval = humanApproval,
                AllowedWorkerAgents = allowed,
                IsOverridden = false
            };
        }

        /// <summary>
        /// Читает массив whitelist из конфигурации через
        /// <c>GetSection(...).GetChildren()</c> (RULES § 4.56: без <c>Get&lt;T&gt;</c>).
        /// </summary>
        private static List<string> ReadWorkerAgents(IConfiguration configuration)
        {
            var section = configuration.GetSection($"{SectionPath}:AllowedWorkerAgents");
            var values = section.GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return NormalizeAgentList(values);
        }

        /// <summary>
        /// Читает целочисленное значение через indexer + <c>TryParse</c>
        /// (RULES § 4.56).
        /// </summary>
        private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
        {
            var raw = configuration[key];
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var value)
                ? value
                : defaultValue;
        }

        // ============ Private: normalization / validation ============

        /// <summary>
        /// Строгая нормализация для runtime-override: некорректные значения
        /// приводят к <see cref="ArgumentException"/> (их отвергает админка).
        /// </summary>
        private OrchestratorSettingsDto NormalizeForUpdate(
            OrchestratorSettingsDto source)
        {
            if (source.MaxSubtasks < MinMaxSubtasks || source.MaxSubtasks > MaxMaxSubtasks)
                throw new ArgumentException(
                    $"MaxSubtasks должен быть в диапазоне {MinMaxSubtasks}–{MaxMaxSubtasks}.",
                    nameof(source));

            if (source.MaxParallel < MinMaxParallel || source.MaxParallel > MaxMaxParallel)
                throw new ArgumentException(
                    $"MaxParallel должен быть в диапазоне {MinMaxParallel}–{MaxMaxParallel}.",
                    nameof(source));

            if (source.TokenBudget < MinTokenBudget || source.TokenBudget > MaxTokenBudget)
                throw new ArgumentException(
                    $"TokenBudget должен быть в диапазоне {MinTokenBudget}–{MaxTokenBudget}.",
                    nameof(source));

            var humanApproval = NormalizeHumanApproval(
                source.HumanApproval, throwOnInvalid: true);

            var agents = NormalizeAgentList(source.AllowedWorkerAgents);
            if (agents.Count == 0)
                throw new ArgumentException(
                    "AllowedWorkerAgents не может быть пустым: укажите хотя бы одного воркера.",
                    nameof(source));

            if (agents.Count > MaxWorkerAgents)
                throw new ArgumentException(
                    $"AllowedWorkerAgents: не более {MaxWorkerAgents} агентов.",
                    nameof(source));

            return new OrchestratorSettingsDto
            {
                MaxSubtasks = source.MaxSubtasks,
                MaxParallel = source.MaxParallel,
                TokenBudget = source.TokenBudget,
                HumanApproval = humanApproval,
                AllowedWorkerAgents = agents,
                IsOverridden = false
            };
        }

        /// <summary>
        /// Нормализует режим Human-in-the-loop к каноническому виду.
        /// </summary>
        /// <param name="raw">Сырое значение из конфигурации/запроса</param>
        /// <param name="throwOnInvalid">
        /// true — бросить <see cref="ArgumentException"/> при неизвестном значении
        /// (для Update); false — залогировать и вернуть <c>Never</c> (для baseline).
        /// </param>
        private string NormalizeHumanApproval(string raw, bool throwOnInvalid)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (throwOnInvalid)
                    throw new ArgumentException(
                        $"HumanApproval обязателен. Допустимые значения: " +
                        $"{string.Join(", ", AllowedHumanApprovalValues)}.",
                        nameof(raw));
                return DefaultHumanApproval;
            }

            var match = AllowedHumanApprovalValues.FirstOrDefault(
                value => string.Equals(value, raw.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;

            if (throwOnInvalid)
            {
                throw new ArgumentException(
                    $"HumanApproval='{raw}' недопустим. Допустимые значения: " +
                    $"{string.Join(", ", AllowedHumanApprovalValues)}.",
                    nameof(raw));
            }

            _logger.LogWarning(
                "Orchestrator:HumanApproval='{Raw}' недопустим — использован '{Default}'.",
                raw, DefaultHumanApproval);
            return DefaultHumanApproval;
        }

        /// <summary>
        /// Нормализует список агентов: trim, удаление пустых, дедупликация
        /// без учёта регистра с сохранением исходного порядка.
        /// </summary>
        private static List<string> NormalizeAgentList(IEnumerable<string> source)
        {
            var result = new List<string>();
            if (source == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in source)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var name = raw.Trim();
                if (!seen.Add(name)) continue;
                result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// Создаёт глубокую копию DTO (список агентов копируется).
        /// </summary>
        private static OrchestratorSettingsDto Clone(OrchestratorSettingsDto source)
        {
            return new OrchestratorSettingsDto
            {
                MaxSubtasks = source.MaxSubtasks,
                MaxParallel = source.MaxParallel,
                TokenBudget = source.TokenBudget,
                HumanApproval = source.HumanApproval,
                AllowedWorkerAgents = source.AllowedWorkerAgents == null
                    ? new List<string>()
                    : new List<string>(source.AllowedWorkerAgents),
                IsOverridden = source.IsOverridden
            };
        }

        /// <summary>
        /// Признак наличия <b>эффективного</b> override: снимок существует
        /// и хотя бы одно поле отличается от baseline.
        /// </summary>
        private bool HasEffectiveOverride()
        {
            return _runtimeOverride.TryGetValue(RuntimeOverrideKey, out var runtime)
                   && !AreEqual(runtime, _baseline);
        }

        /// <summary>
        /// Сравнивает два снимка настроек по всем полям
        /// (whitelist — без учёта регистра, с сохранением порядка).
        /// </summary>
        private static bool AreEqual(
            OrchestratorSettingsDto left, OrchestratorSettingsDto right)
        {
            if (left == null || right == null) return ReferenceEquals(left, right);

            return left.MaxSubtasks == right.MaxSubtasks
                   && left.MaxParallel == right.MaxParallel
                   && left.TokenBudget == right.TokenBudget
                   && string.Equals(left.HumanApproval, right.HumanApproval,
                       StringComparison.Ordinal)
                   && SequenceEqualAgents(
                       left.AllowedWorkerAgents, right.AllowedWorkerAgents);
        }

        /// <summary>
        /// Сравнивает whitelist-агентов (регистронезависимо, по порядку).
        /// </summary>
        private static bool SequenceEqualAgents(
            IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var leftList = left ?? Array.Empty<string>();
            var rightList = right ?? Array.Empty<string>();

            if (leftList.Count != rightList.Count) return false;

            for (var i = 0; i < leftList.Count; i++)
            {
                if (!string.Equals(leftList[i], rightList[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
