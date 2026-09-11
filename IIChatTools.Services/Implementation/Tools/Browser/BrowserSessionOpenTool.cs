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
    /// Инструмент: открытие долгоживущей браузерной сессии.
    /// </summary>
    public class BrowserSessionOpenTool : ITool
    {
        private readonly IBrowserSessionManager _sessionManager;
        private readonly ILogger<BrowserSessionOpenTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="sessionManager">Менеджер сессий</param>
        /// <param name="logger">Логгер</param>
        public BrowserSessionOpenTool(IBrowserSessionManager sessionManager, ILogger<BrowserSessionOpenTool> logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "browser_session_open";

        /// <inheritdoc />
        public string Description => "Открывает долгоживущую браузерную сессию. Возвращает sessionId для последующих вызовов browser_session_control.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "initialUrl",
                Type = "string",
                Description = "Опциональный начальный URL (http/https).",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var url = arguments.GetString("initialUrl");

            try
            {
                var info = await _sessionManager.OpenAsync(context.UserId, url, context.CancellationToken);
                return ToolResult.Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка открытия браузерной сессии для пользователя {UserId}", context.UserId);
                return ToolResult.Fail($"Ошибка открытия сессии: {ex.Message}");
            }
        }
    }
}