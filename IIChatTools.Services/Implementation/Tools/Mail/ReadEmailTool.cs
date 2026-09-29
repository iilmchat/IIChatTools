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
    /// Инструмент LLM: прочитать письмо по UID
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// Read-only. Возвращает полное письмо: from, to, cc, subject, date, bodyText,
    /// bodyHtml, attachments. Если <c>saveAttachments = true</c> — вложения
    /// сохраняются в <c>{workspace}/mail-attachments/{uid}/</c> (Фаза 4).
    /// В Фазе 3A — вложения возвращаются в DTO, но НЕ сохраняются на диск
    /// (IMailAttachmentService — Фаза 4).
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только uid, mailbox, длина body, количество вложений.
    /// Никогда — Subject / From / Body.
    /// </para>
    /// </summary>
    public sealed class ReadEmailTool : ITool
    {
        private readonly IMailClient _mailClient;
        private readonly ILogger<ReadEmailTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ReadEmailTool(
            IMailClient mailClient,
            ILogger<ReadEmailTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "read_email";

        /// <inheritdoc />
        public string Description =>
            "Читает письмо по UID (получить uid можно через list_emails или search_emails). " +
            "Возвращает: from, to, cc, subject, date, bodyText, bodyHtml, attachments. " +
            "Используй для чтения конкретного письма: «прочитай письмо от Иванова», " +
            "«что в письме с темой X». UID уникален в пределах папки.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

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
            },
            new ToolParameterDescriptor
            {
                Name = "saveAttachments",
                Type = "boolean",
                Description = "Сохранять вложения в workspace (по умолчанию true). " +
                              "Если false — только метаданные вложений.",
                Required = false,
                Default = true
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
                    return ToolResult.Fail("Не указан или некорректен параметр 'uid' (должен быть положительным числом).");

                var mailbox = ParseString(arguments, "mailbox", "INBOX");
                var saveAttachments = ParseBool(arguments, "saveAttachments", true);

                var message = await _mailClient.ReadAsync(
                    uid, mailbox, saveAttachments,
                    context.UserId, context.CancellationToken);

                if (message == null)
                    return ToolResult.Fail($"Письмо с UID {uid} не найдено в папке '{mailbox}'.");

                // Privacy: длина body и количество вложений, без самих данных.
                _logger.LogInformation(
                    "Mail: read_email uid={Uid} mailbox={Mailbox} bodyLen={BodyLen} attachments={Attachments}",
                    uid, mailbox,
                    (message.BodyText?.Length ?? 0) + (message.BodyHtml?.Length ?? 0),
                    message.Attachments?.Count ?? 0);

                var attachmentNote = (message.Attachments?.Count ?? 0) > 0 && saveAttachments
                    ? $" Вложения ({message.Attachments!.Count} шт.) — Фаза 4 (пока не сохранены в workspace)."
                    : string.Empty;

                return ToolResult.Ok(
                    message,
                    $"Письмо {uid} из '{mailbox}' прочитано.{attachmentNote}");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                // Privacy: лог без Subject / From / Body.
                _logger.LogError(ex,
                    "ReadEmailTool: ошибка (mailbox={Mailbox}, uid={Uid})",
                    arguments?["mailbox"]?.ToString(),
                    arguments?["uid"]?.ToString());
                return ToolResult.Fail($"Ошибка чтения письма: {ex.Message}");
            }
        }

        // ============ Парсеры JObject ============

        private static string ParseString(JObject args, string key, string defaultValue)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;
            var s = raw.ToString();
            return string.IsNullOrWhiteSpace(s) ? defaultValue : s;
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

        private static bool ParseBool(JObject args, string key, bool defaultValue)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;
            if (raw.Type == JTokenType.Boolean) return raw.Value<bool>();
            return bool.TryParse(raw.ToString(), out var value) ? value : defaultValue;
        }
    }
}