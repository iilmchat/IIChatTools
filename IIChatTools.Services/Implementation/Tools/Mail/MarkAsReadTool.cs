using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Mail
{
    /// <summary>
    /// Инструмент LLM: пометить письмо прочитанным
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// <b>Без approval</b> — мелкое действие. Approval уже был получен
    /// на уровне <c>mail_agent</c> (<c>RequiresApprovalByDefault = true</c>
    /// у агента-обёртки). DESIGN § 6.1: <c>mark_as_read</c> — <c>❌</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только uid и mailbox.
    /// </para>
    /// </summary>
    public sealed class MarkAsReadTool : ITool
    {
        private readonly IMailClient _mailClient;
        private readonly ILogger<MarkAsReadTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public MarkAsReadTool(
            IMailClient mailClient,
            ILogger<MarkAsReadTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "mark_as_read";

        /// <inheritdoc />
        public string Description =>
            "Помечает письмо как прочитанное. Параметры: uid (число), mailbox (по умолчанию INBOX). " +
            "UID можно получить через list_emails или search_emails. " +
            "Не требует подтверждения — мелкое действие (approval уже был на уровне агента).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "uid",
                Type = "integer",
                Description = "IMAP UID письма.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "mailbox",
                Type = "string",
                Description = "Папка: INBOX (по умолчанию).",
                Required = false,
                Default = "INBOX"
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте.");

                var uid = ParseUInt(arguments, "uid");
                if (uid == 0)
                    return ToolResult.Fail("Не указан или некорректен параметр 'uid'.");

                var mailbox = ParseString(arguments, "mailbox", "INBOX");

                await _mailClient.MarkAsReadAsync(
                    uid, mailbox, context.UserId, context.CancellationToken);

                _logger.LogInformation(
                    "Mail: mark_as_read uid={Uid} mailbox={Mailbox}",
                    uid, mailbox);

                return ToolResult.Ok(
                    new { uid, mailbox },
                    $"Письмо {uid} помечено как прочитанное.");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MarkAsReadTool: ошибка (uid={Uid}, mailbox={Mailbox})",
                    arguments?["uid"]?.ToString(),
                    arguments?["mailbox"]?.ToString());
                return ToolResult.Fail($"Ошибка пометки письма: {ex.Message}");
            }
        }

        // ============ Парсеры ============

        private static string ParseString(JObject args, string key, string defaultValue)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;
            var s = raw.ToString();
            return string.IsNullOrWhiteSpace(s) ? defaultValue : s.Trim();
        }

        private static uint ParseUInt(JObject args, string key)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return 0;

            if (raw.Type == JTokenType.Integer)
            {
                var v = raw.Value<long>();
                return v > 0 && v <= uint.MaxValue ? (uint)v : 0;
            }

            return uint.TryParse(raw.ToString(), out var uid) ? uid : 0;
        }
    }
}