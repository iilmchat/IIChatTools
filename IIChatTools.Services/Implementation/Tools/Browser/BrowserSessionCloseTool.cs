using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Browser
{
    /// <summary>
    /// Инструмент: закрытие браузерной сессии.
    /// </summary>
    public class BrowserSessionCloseTool : ITool
    {
        private readonly IBrowserSessionManager _sessionManager;
        private readonly ILogger<BrowserSessionCloseTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="sessionManager">Менеджер сессий</param>
        /// <param name="logger">Логгер</param>
        public BrowserSessionCloseTool(IBrowserSessionManager sessionManager, ILogger<BrowserSessionCloseTool> logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "browser_session_close";

        /// <inheritdoc />
        public string Description => "Закрывает браузерную сессию по её идентификатору.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "sessionId", Type = "string", Description = "Идентификатор сессии.", Required = true }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var sessionId = arguments.GetString("sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
                return ToolResult.Fail("Не указан sessionId");

            try
            {
                var closed = await _sessionManager.CloseAsync(sessionId, context.UserId);
                if (!closed)
                    return ToolResult.Fail("Сессия не найдена или недоступна");

                return ToolResult.Ok(new { closed = true, sessionId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка закрытия сессии {SessionId}", sessionId);
                return ToolResult.Fail($"Ошибка закрытия сессии: {ex.Message}");
            }
        }
    }
}