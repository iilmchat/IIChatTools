using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Mail
{
    /// <summary>
    /// Инструмент LLM: список писем из папки
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// Read-only. Возвращает последние N писем (метаданные: uid, from, subject,
    /// date, isRead, hasAttachments). Содержимое писем — не читается
    /// (для этого <c>read_email</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только количество. Никогда — Subject / From.
    /// </para>
    /// </summary>
    public sealed class ListEmailsTool : ITool
    {
        /// <summary>Лимит по умолчанию (совпадает с DESIGN § 4.3).</summary>
        private const int DefaultCount = 20;

        /// <summary>Верхняя граница count (защита от гигантских запросов).</summary>
        private const int MaxCount = 100;

        private readonly IMailClient _mailClient;
        private readonly ILogger<ListEmailsTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент (Singleton)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public ListEmailsTool(
            IMailClient mailClient,
            ILogger<ListEmailsTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "list_emails";

        /// <inheritdoc />
        public string Description =>
            "Возвращает список последних писем из папки (по умолчанию INBOX). " +
            "Используй для вопросов вида «покажи новые письма», «что пришло сегодня», " +
            "«последние 5 писем». Возвращает метаданные (uid, from, subject, date, " +
            "isRead, hasAttachments), но НЕ содержимое — для чтения используй read_email.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
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
                Name = "count",
                Type = "integer",
                Description = "Сколько писем вернуть (1–100, по умолчанию 20).",
                Required = false,
                Default = DefaultCount
            },
            new ToolParameterDescriptor
            {
                Name = "unseenOnly",
                Type = "boolean",
                Description = "Только непрочитанные (по умолчанию false).",
                Required = false,
                Default = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте.");

                var mailbox = ParseString(arguments, "mailbox", "INBOX");
                var count = ParseInt(arguments, "count", DefaultCount, min: 1, max: MaxCount);
                var unseenOnly = ParseBool(arguments, "unseenOnly", false);

                var messages = await _mailClient.ListAsync(
                    mailbox, count, unseenOnly,
                    context.UserId, context.CancellationToken);

                _logger.LogInformation(
                    "Mail: list_emails mailbox={Mailbox} unseenOnly={Unseen} count={Count}",
                    mailbox, unseenOnly, messages.Count);

                if (messages.Count == 0)
                {
                    return ToolResult.Ok(
                        new { mailbox, count = 0, messages = Array.Empty<object>() },
                        $"Писем не найдено в папке '{mailbox}'" +
                        (unseenOnly ? " (только непрочитанные)" : string.Empty) + ".");
                }

                var message = unseenOnly
                    ? $"Непрочитанных писем: {messages.Count}."
                    : $"Писем получено: {messages.Count}.";

                return ToolResult.Ok(new { mailbox, count = messages.Count, messages }, message);
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                // Privacy: лог без Subject / From / Body.
                _logger.LogError(ex, "ListEmailsTool: ошибка (mailbox={Mailbox})",
                    arguments?["mailbox"]?.ToString());
                return ToolResult.Fail($"Ошибка получения списка писем: {ex.Message}");
            }
        }

        // ============ Парсеры JObject (без extension, по образцу SearchKnowledgeBaseTool) ============

        private static string ParseString(JObject args, string key, string defaultValue)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;
            var s = raw.ToString();
            return string.IsNullOrWhiteSpace(s) ? defaultValue : s;
        }

        private static int ParseInt(JObject args, string key, int defaultValue, int min, int max)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;

            int value;
            if (raw.Type == JTokenType.Integer)
                value = raw.Value<int>();
            else if (!int.TryParse(raw.ToString(), out value))
                return defaultValue;

            return Math.Clamp(value, min, max);
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