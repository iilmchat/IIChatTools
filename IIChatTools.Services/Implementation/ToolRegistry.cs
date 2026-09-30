using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using IIChatTools.Services.Metrics;   // v1.8.2: ToolCache* метрики
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация реестра инструментов: инструменты внедряются через DI как IEnumerable{ITool}.
    /// </summary>
    public class ToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools;
        private readonly ILogger<ToolRegistry> _logger;

        /// <summary>
        /// v1.8.2: опциональный кэш результатов инструментов.
        /// <c>null</c> — кэш отключён (поведение v1.8.1: каждый вызов реальный).
        /// </summary>
        private readonly IToolResultCache _resultCache;

        /// <summary>
        /// Создаёт реестр на основе всех зарегистрированных в DI инструментов.
        /// </summary>
        /// <param name="tools">Коллекция инструментов</param>
        /// <param name="logger">Логгер</param>
        /// <param name="resultCache">
        /// Опциональный кэш результатов инструментов (v1.8.2).
        /// <c>null</c> — кэш отключён (поведение v1.8.1). По умолчанию — <c>null</c>
        /// для обратной совместимости с unit-тестами.
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="tools"/> или <paramref name="logger"/> равны null</exception>
        public ToolRegistry(
            IEnumerable<ITool> tools,
            ILogger<ToolRegistry> logger,
            IToolResultCache resultCache = null)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resultCache = resultCache;

            _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.Name))
                {
                    _logger.LogWarning("Пропущен инструмент {Type} без имени", tool.GetType().FullName);
                    continue;
                }

                if (_tools.ContainsKey(tool.Name))
                {
                    _logger.LogWarning("Дублирование инструмента {Name}: {Type1} и {Type2}",
                        tool.Name, _tools[tool.Name].GetType().FullName, tool.GetType().FullName);
                    continue;
                }

                _tools[tool.Name] = tool;
            }

            // KI-075: понижено до Debug — ToolRegistry scoped, лог спамит при каждом запросе.
            // Переключить на Information можно в appsettings.json: Logging:LogLevel:IIChatTools.Services.Implementation.ToolRegistry=Information
            _logger.LogDebug("ToolRegistry инициализирован. Зарегистрировано инструментов: {Count}", _tools.Count);
        }

        /// <inheritdoc />
        public IReadOnlyList<ToolDescriptor> GetAllDescriptors()
        {
            return _tools.Values
                .Select(t => new ToolDescriptor
                {
                    Name = t.Name,
                    Description = t.Description,
                    RequiresApprovalByDefault = t.RequiresApprovalByDefault,
                    Parameters = t.Parameters
                })
                .OrderBy(d => d.Name)
                .ToList();
        }

        /// <inheritdoc />
        public ToolDescriptor GetDescriptor(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (!_tools.TryGetValue(name, out var tool))
                return null;

            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = tool.Description,
                RequiresApprovalByDefault = tool.RequiresApprovalByDefault,
                Parameters = tool.Parameters
            };
        }

        /// <inheritdoc />
        public ITool GetTool(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _tools.TryGetValue(name, out var tool) ? tool : null;
        }

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(string toolName, ToolExecutionContext context, JObject arguments)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return ToolResult.Fail("Имя инструмента не указано");

            if (context == null)
                return ToolResult.Fail("Контекст выполнения не задан");

            if (!_tools.TryGetValue(toolName, out var tool))
                return ToolResult.Fail($"Инструмент '{toolName}' не зарегистрирован");

            var args = arguments ?? new JObject();

            // v1.8.2: Tool result cache. Кэшируем только инструменты из whitelist
            // (ToolResultCacheOptions.Tools). UserId в ключе — per-user изоляция.
            // Пропускаем анонимные вызовы (UserId == 0) — нечего изолировать.
            var cacheable = _resultCache != null
                && context.UserId > 0
                && _resultCache.IsCacheable(toolName);

            if (cacheable)
            {
                var cached = _resultCache.TryGet(toolName, context.UserId, args);
                if (cached != null)
                {
                    AppMetrics.ToolCacheHitsTotal.WithLabels(toolName).Inc();
                    _logger.LogDebug(
                        "Tool cache HIT: {Tool} (user={UserId})", toolName, context.UserId);
                    return cached;
                }

                AppMetrics.ToolCacheMissesTotal.WithLabels(toolName).Inc();
            }

            try
            {
                _logger.LogInformation("Выполнение инструмента {Tool} для пользователя {UserId}",
                    toolName, context.UserId);

                var result = await tool.ExecuteAsync(context, args);

                if (result == null)
                {
                    return ToolResult.Fail("Инструмент вернул пустой результат");
                }

                // v1.8.2: кэшируем только успешные результаты (Fail — не кэшируем).
                if (cacheable && result.Success)
                {
                    _resultCache.Set(toolName, context.UserId, args, result);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Инструмент {Tool} отменён", toolName);
                return ToolResult.Fail("Операция отменена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения инструмента {Tool}", toolName);
                return ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
            }
        }
    }
}