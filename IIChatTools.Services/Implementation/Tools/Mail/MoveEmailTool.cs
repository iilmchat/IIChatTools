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
    /// Инструмент LLM: перемещение письма между папками
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// <b>Mutating. <see cref="RequiresApprovalByDefault"/> = true</b> —
    /// пользователь подтверждает перемещение.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только uid и имена папок.
    /// </para>
    /// </summary>
    public sealed class MoveEmailTool : ITool
    {
        private readonly IMailClient _mailClient;
        private readonly ILogger<MoveEmailTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public MoveEmailTool(
            IMailClient mailClient,
            ILogger<MoveEmailTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "move_email";

        /// <inheritdoc />
        public string Description =>
            "Перемещает письмо из одной папки в другую. ТРЕБУЕТ ПОДТВЕРЖДЕНИЯ. " +
            "Параметры: uid (число), from (исходная папка), to (целевая папка). " +
            "Стандартные папки: INBOX, Sent, Drafts, Trash, Archive, Junk. " +
            "Используй для вопросов вида «перемести письмо в архив», " +
            "«перенеси в Trash».";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "uid",
                Type = "integer",
                Description = "IMAP UID письма (получить через list_emails / search_emails).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "from",
                Type = "string",
                Description = "Исходная папка (по умолчанию INBOX).",
                Required = false,
                Default = "INBOX"
            },
            new ToolParameterDescriptor
            {
                Name = "to",
                Type = "string",
                Description = "Целевая папка (INBOX, Sent, Drafts, Trash, Archive, Junk).",
                Required = true
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

                var fromMailbox = ParseString(arguments, "from", "INBOX");
                var toMailbox = ParseString(arguments, "to", null);

                if (string.IsNullOrWhiteSpace(toMailbox))
                    return ToolResult.Fail("Не указан параметр 'to' (целевая папка).");

                if (string.Equals(fromMailbox, toMailbox, StringComparison.OrdinalIgnoreCase))
                    return ToolResult.Fail("Исходная и целевая папки совпадают.");

                await _mailClient.MoveAsync(
                    uid, fromMailbox, toMailbox,
                    context.UserId, context.CancellationToken);

                _logger.LogInformation(
                    "Mail: move_email uid={Uid} from={From} to={To}",
                    uid, fromMailbox, toMailbox);

                return ToolResult.Ok(
                    new { uid, from = fromMailbox, to = toMailbox },
                    $"Письмо {uid} перемещено из '{fromMailbox}' в '{toMailbox}'.");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MoveEmailTool: ошибка (uid={Uid}, from={From}, to={To})",
                    arguments?["uid"]?.ToString(),
                    arguments?["from"]?.ToString(),
                    arguments?["to"]?.ToString());
                return ToolResult.Fail($"Ошибка перемещения письма: {ex.Message}");
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