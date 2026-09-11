using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace IIChatTools.Services.Implementation.Tools.Browser
{
    /// <summary>
    /// Инструмент: управление браузерной сессией (навигация, клики, ввод, evaluate).
    /// </summary>
    public class BrowserSessionControlTool : ITool
    {
        private readonly IBrowserSessionManager _sessionManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BrowserSessionControlTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="sessionManager">Менеджер сессий</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        public BrowserSessionControlTool(
            IBrowserSessionManager sessionManager,
            IConfiguration configuration,
            ILogger<BrowserSessionControlTool> logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "browser_session_control";

        /// <inheritdoc />
        public string Description =>
            "Управляет сессией браузера. Команды: goto, click, type, wait_for_selector, evaluate, get_content, screenshot, get_url, go_back, go_forward, reload.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "sessionId", Type = "string", Description = "Идентификатор сессии.", Required = true },
            new ToolParameterDescriptor { Name = "command", Type = "string", Description = "Команда: goto|click|type|wait_for_selector|evaluate|get_content|screenshot|get_url|go_back|go_forward|reload.", Required = true },
            new ToolParameterDescriptor { Name = "url", Type = "string", Description = "URL (для команды goto).", Required = false },
            new ToolParameterDescriptor { Name = "selector", Type = "string", Description = "CSS-селектор (для click/type/wait_for_selector).", Required = false },
            new ToolParameterDescriptor { Name = "text", Type = "string", Description = "Текст для ввода (для команды type).", Required = false },
            new ToolParameterDescriptor { Name = "script", Type = "string", Description = "JS-код для evaluate.", Required = false },
            new ToolParameterDescriptor { Name = "timeoutMs", Type = "integer", Description = "Таймаут операции (мс).", Required = false, Default = 15000 }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var sessionId = arguments.GetString("sessionId");
            var command = arguments.GetString("command");

            if (string.IsNullOrWhiteSpace(sessionId))
                return ToolResult.Fail("Не указан sessionId");
            if (string.IsNullOrWhiteSpace(command))
                return ToolResult.Fail("Не указана команда");

            // Получаем сессию через менеджер (проверяет владельца)
            var info = await _sessionManager.GetInfoAsync(sessionId, context.UserId);
            if (info == null)
                return ToolResult.Fail("Сессия не найдена или недоступна");

            // Достаём IPage через рефлексию невозможно — используем публичный API менеджера.
            // Поэтому нужен отдельный метод для выполнения команд. Возьмём его из менеджера ниже.
            try
            {
                var result = await ExecuteCommandAsync(context, sessionId, command.ToLowerInvariant(), arguments);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка управления браузерной сессией {SessionId}, команда {Command}",
                    sessionId, command);
                return ToolResult.Fail($"Ошибка выполнения команды: {ex.Message}");
            }
        }

        /// <summary>
        /// Диспетчер команд управления сессией.
        /// </summary>
        /// <param name="context">Контекст</param>
        /// <param name="sessionId">Идентификатор сессии</param>
        /// <param name="command">Команда</param>
        /// <param name="arguments">Аргументы</param>
        /// <returns>Результат</returns>
        private async Task<ToolResult> ExecuteCommandAsync(
            ToolExecutionContext context, string sessionId, string command, JObject arguments)
        {
            var timeoutMs = arguments.GetInt("timeoutMs", 15000);
            if (timeoutMs <= 0 || timeoutMs > 120000) timeoutMs = 15000;

            // Делегируем менеджеру через расширенный интерфейс.
            // Для простоты используем dynamic-доступ к внутреннему методу (см. ниже).
            var manager = _sessionManager as BrowserSessionManager;
            if (manager == null)
                return ToolResult.Fail("Менеджер сессий не поддерживает управление командой");

            return await manager.ExecuteCommandAsync(context, sessionId, command, arguments, timeoutMs);
        }
    }
}