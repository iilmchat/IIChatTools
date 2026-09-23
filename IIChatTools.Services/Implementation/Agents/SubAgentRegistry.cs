using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Agents
{
    /// <summary>
    /// Реализация реестра специализированных суб-агентов (v1.4.0, KI-052).
    ///
    /// Источник данных — секция <c>SubAgents</c> в appsettings.json.
    /// Ключи секции — технические имена агентов (<c>file_system_agent</c> и т.п.).
    ///
    /// Реестр неизменяем после инициализации в Фазе 1 (без AppSettings).
    /// В Фазе 6 — <see cref="Update"/> и <see cref="Reset"/> будут ходить в БД.
    ///
    /// Singleton (см. регистрацию в <c>Startup.cs</c>).
    /// </summary>
    public class SubAgentRegistry : ISubAgentRegistry
    {
        // Immutable после конструктора (в Фазе 1). В Фазе 6 — перейдём на concurrent.
        private readonly Dictionary<string, SubAgentDescriptor> _agents;

        // Значения по умолчанию (appsettings.json) — для Reset() в Фазе 6.
        // Пока не используется, но храним — пригодится.
        private readonly Dictionary<string, SubAgentDescriptor> _defaults;

        private readonly ILogger<SubAgentRegistry> _logger;

        /// <summary>
        /// Создаёт реестр, читая секцию <c>SubAgents</c> из конфигурации.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр равен null</exception>
        public SubAgentRegistry(
            IConfiguration configuration,
            ILogger<SubAgentRegistry> logger)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _agents = new Dictionary<string, SubAgentDescriptor>(StringComparer.OrdinalIgnoreCase);
            _defaults = new Dictionary<string, SubAgentDescriptor>(StringComparer.OrdinalIgnoreCase);

            LoadFromConfiguration(configuration);

            _logger.LogInformation(
                "SubAgentRegistry инициализирован. Зарегистрировано агентов: {Count} ({Names})",
                _agents.Count,
                string.Join(", ", _agents.Keys.OrderBy(k => k)));
        }

        /// <inheritdoc />
        public IReadOnlyList<SubAgentDescriptor> GetAll()
            => _agents.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <inheritdoc />
        public IReadOnlyList<SubAgentDescriptor> GetEnabled()
            => _agents.Values
                .Where(d => !d.Disabled)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <inheritdoc />
        public SubAgentDescriptor Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _agents.TryGetValue(name, out var d) ? d : null;
        }

        /// <inheritdoc />
        /// <remarks>
        /// В Фазе 1 — обновляет только in-memory. В Фазе 6 — будет сохранять в AppSettings.
        /// </remarks>
        public void Update(SubAgentDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Name))
                throw new ArgumentException("Имя агента обязательно.", nameof(descriptor));

            _agents[descriptor.Name] = descriptor;
            _logger.LogInformation("Агент {Name} обновлён (in-memory)", descriptor.Name);
        }

        /// <inheritdoc />
        /// <remarks>
        /// В Фазе 1 — восстанавливает из _defaults. В Фазе 6 — снимет override в БД.
        /// </remarks>
        public void Reset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_defaults.TryGetValue(name, out var defaultDescriptor))
            {
                _agents[name] = defaultDescriptor;
                _logger.LogInformation("Агент {Name} сброшен к значению по умолчанию", name);
            }
        }

        /// <summary>
        /// Читает секцию <c>SubAgents</c> из конфигурации и наполняет словари.
        /// Ключ секции = <see cref="SubAgentDescriptor.Name"/>.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        private void LoadFromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection("SubAgents");
            if (!section.Exists())
            {
                _logger.LogWarning(
                    "Секция SubAgents не найдена в конфигурации. Реестр пуст. " +
                    "Chat будет использовать только consult_secondary_agent.");
                return;
            }

            foreach (var child in section.GetChildren())
            {
                var name = child.Key;
                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogWarning("SubAgents: пропущена секция с пустым именем");
                    continue;
                }

                var descriptor = new SubAgentDescriptor
                {
                    Name = name,
                    DisplayName = child["DisplayName"] ?? name,
                    Description = child["Description"] ?? string.Empty,
                    SystemPrompt = child["SystemPrompt"],
                    Model = child["Model"],
                    MaxSteps = ParseInt(child["MaxSteps"], defaultValue: 10, min: 1, max: 30),
                    RequiresApprovalByDefault = ParseBool(child["RequiresApproval"], defaultValue: true),
                    Disabled = !ParseBool(child["Enabled"], defaultValue: true),
                    AllowedTools = ParseAllowedTools(child.GetSection("AllowedTools"))
                };

                _agents[name] = descriptor;
                _defaults[name] = Clone(descriptor);
            }
        }

        /// <summary>
        /// Читает массив <c>AllowedTools</c> из секции.
        /// </summary>
        /// <param name="section">Секция конфигурации</param>
        /// <returns>Список имён инструментов (пустой, если нет)</returns>
        private static IReadOnlyList<string> ParseAllowedTools(IConfigurationSection section)
        {
            if (section == null || !section.Exists())
                return Array.Empty<string>();

            return section.GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        /// <summary>
        /// Безопасный парсинг int с ограничением диапазона.
        /// </summary>
        private static int ParseInt(string raw, int defaultValue, int min, int max)
        {
            if (!int.TryParse(raw, out var v)) return defaultValue;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>
        /// Безопасный парсинг bool.
        /// </summary>
        private static bool ParseBool(string raw, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            return bool.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Создаёт независимую копию дескриптора (для _defaults).
        /// </summary>
        private static SubAgentDescriptor Clone(SubAgentDescriptor source)
        {
            return new SubAgentDescriptor
            {
                Name = source.Name,
                DisplayName = source.DisplayName,
                Description = source.Description,
                SystemPrompt = source.SystemPrompt,
                Model = source.Model,
                MaxSteps = source.MaxSteps,
                RequiresApprovalByDefault = source.RequiresApprovalByDefault,
                Disabled = source.Disabled,
                AllowedTools = source.AllowedTools == null
                    ? Array.Empty<string>()
                    : source.AllowedTools.ToList()
            };
        }
    }
}