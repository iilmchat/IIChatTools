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
    /// Инструмент LLM: удаление письма (перемещение в Trash)
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// <b>Mutating. <see cref="RequiresApprovalByDefault"/> = true</b> —
    /// пользователь подтверждает удаление.
    /// </para>
    ///
    /// <para>
    /// <b>Поведение:</b> письмо помечается <c>Deleted</c> и expunge'ится
    /// в текущей папке. В большинстве IMAP-серверов (Yandex, Gmail) это
    /// означает «перемещение в Trash» через серверную логику.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только uid и mailbox.
    /// </para>
    /// </summary>
    public sealed class DeleteEmailTool : ITool
    {
        private readonly IMailClient _mailClient;
        private readonly ILogger<DeleteEmailTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public DeleteEmailTool(
            IMailClient mailClient,
            ILogger<DeleteEmailTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "delete_email";

        /// <inheritdoc />
        public string Description =>
            "Удаляет письмо (перемещает в Trash). ТРЕБУЕТ ПОДТВЕРЖДЕНИЯ. " +
            "Параметры: uid (число), mailbox (по умолчанию INBOX). " +
            "UID можно получить через list_emails или search_emails. " +
            "Действие необратимо — используй только по явной просьбе пользователя.";

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
                Name = "mailbox",
                Type = "string",
                Description = "Папка: INBOX (по умолчанию), Sent, Trash, Drafts, Archive, Junk.",
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

                await _mailClient.DeleteAsync(
                    uid, mailbox, context.UserId, context.CancellationToken);

                _logger.LogInformation(
                    "Mail: delete_email uid={Uid} mailbox={Mailbox}",
                    uid, mailbox);

                return ToolResult.Ok(
                    new { uid, mailbox },
                    $"Письмо {uid} удалено из '{mailbox}' (перемещено в Trash).");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DeleteEmailTool: ошибка (uid={Uid}, mailbox={Mailbox})",
                    arguments?["uid"]?.ToString(),
                    arguments?["mailbox"]?.ToString());
                return ToolResult.Fail($"Ошибка удаления письма: {ex.Message}");
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