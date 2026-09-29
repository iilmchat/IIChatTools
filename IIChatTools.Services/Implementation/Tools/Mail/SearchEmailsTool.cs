using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Mail
{
    /// <summary>
    /// Инструмент LLM: поиск писем по фильтрам
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// Read-only. Параметры: <c>from</c>, <c>subject</c>, <c>since</c>,
    /// <c>before</c>, <c>unseenOnly</c>, <c>mailbox</c>, <c>limit</c>.
    /// Возвращает метаданные писем (uid, from, subject, date, isRead, hasAttachments).
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только количество фильтров и результатов.
    /// Никогда — сами фильтры (From/Subject) и результаты.
    /// </para>
    /// </summary>
    public sealed class SearchEmailsTool : ITool
    {
        /// <summary>Лимит результатов по умолчанию.</summary>
        private const int DefaultLimit = 20;

        /// <summary>Максимальный лимит результатов.</summary>
        private const int MaxLimit = 100;

        private readonly IMailClient _mailClient;
        private readonly ILogger<SearchEmailsTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public SearchEmailsTool(
            IMailClient mailClient,
            ILogger<SearchEmailsTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "search_emails";

        /// <inheritdoc />
        public string Description =>
            "Ищет письма по фильтрам. Все фильтры опциональны (комбинируются через AND). " +
            "Параметры: from (подстрока отправителя), subject (подстрока темы), " +
            "since (ISO-дата YYYY-MM-DD, включительно), before (ISO-дата), " +
            "unseenOnly (только непрочитанные), mailbox (папка), limit (1–100, по умолчанию 20). " +
            "Используй для вопросов: «найди письма от Иванова», «что пришло за неделю», " +
            "«письма с темой счёт».";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "from",
                Type = "string",
                Description = "Подстрока отправителя (регистронезависимо). Например: 'ivanov'.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "subject",
                Type = "string",
                Description = "Подстрока темы (регистронезависимо). Например: 'счёт'.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "since",
                Type = "string",
                Description = "Письма, доставленные после этой даты включительно. Формат: YYYY-MM-DD.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "before",
                Type = "string",
                Description = "Письма, доставленные до этой даты включительно. Формат: YYYY-MM-DD.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "unseenOnly",
                Type = "boolean",
                Description = "Только непрочитанные (по умолчанию false).",
                Required = false,
                Default = false
            },
            new ToolParameterDescriptor
            {
                Name = "mailbox",
                Type = "string",
                Description = "Папка (по умолчанию INBOX).",
                Required = false,
                Default = "INBOX"
            },
            new ToolParameterDescriptor
            {
                Name = "limit",
                Type = "integer",
                Description = "Максимум результатов (1–100, по умолчанию 20).",
                Required = false,
                Default = DefaultLimit
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте.");

                var from = ParseString(arguments, "from");
                var subject = ParseString(arguments, "subject");
                var mailbox = ParseString(arguments, "mailbox") ?? "INBOX";
                var unseenOnly = ParseBool(arguments, "unseenOnly", false);
                var limit = ParseInt(arguments, "limit", DefaultLimit, min: 1, max: MaxLimit);

                var since = ParseDate(arguments, "since");
                if (since == null && arguments?["since"] != null
                    && arguments["since"]!.Type != JTokenType.Null)
                {
                    return ToolResult.Fail(
                        "Некорректный формат 'since'. Ожидается YYYY-MM-DD.");
                }

                var before = ParseDate(arguments, "before");
                if (before == null && arguments?["before"] != null
                    && arguments["before"]!.Type != JTokenType.Null)
                {
                    return ToolResult.Fail(
                        "Некорректный формат 'before'. Ожидается YYYY-MM-DD.");
                }

                // Хотя бы один фильтр должен быть задан.
                if (string.IsNullOrWhiteSpace(from)
                    && string.IsNullOrWhiteSpace(subject)
                    && since == null
                    && before == null
                    && !unseenOnly)
                {
                    return ToolResult.Fail(
                        "Не задан ни один фильтр. Укажи хотя бы один: " +
                        "from, subject, since, before, unseenOnly.");
                }

                var request = new SearchMailRequest
                {
                    Mailbox = mailbox,
                    From = string.IsNullOrWhiteSpace(from) ? null : from,
                    Subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
                    Since = since,
                    Before = before,
                    UnseenOnly = unseenOnly,
                    Limit = limit
                };

                var messages = await _mailClient.SearchAsync(
                    request, context.UserId, context.CancellationToken);

                // Privacy: считаем количество фильтров, не пишем их значения.
                var filtersCount =
                    (string.IsNullOrWhiteSpace(from) ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(subject) ? 0 : 1)
                    + (since != null ? 1 : 0)
                    + (before != null ? 1 : 0)
                    + (unseenOnly ? 1 : 0);

                _logger.LogInformation(
                    "Mail: search_emails mailbox={Mailbox} filtersCount={Filters} results={Results}",
                    mailbox, filtersCount, messages.Count);

                if (messages.Count == 0)
                {
                    return ToolResult.Ok(
                        new { mailbox, count = 0, messages = Array.Empty<object>() },
                        "По заданным фильтрам писем не найдено.");
                }

                return ToolResult.Ok(
                    new { mailbox, count = messages.Count, messages },
                    $"Найдено писем: {messages.Count}.");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                // Privacy: в логе — только mailbox; сами фильтры (from/subject) не пишем.
                _logger.LogError(ex,
                    "SearchEmailsTool: ошибка (mailbox={Mailbox})",
                    arguments?["mailbox"]?.ToString());
                return ToolResult.Fail($"Ошибка поиска писем: {ex.Message}");
            }
        }

        // ============ Парсеры ============

        private static string ParseString(JObject args, string key)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return null;
            var s = raw.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
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

        /// <summary>
        /// Парсит дату в формате YYYY-MM-DD (UTC). Возвращает null, если не задана.
        /// Бросает ArgumentException при неверном формате, если значение задано.
        /// </summary>
        private static DateTime? ParseDate(JObject args, string key)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return null;
            var s = raw.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTime.TryParseExact(
                s.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
            {
                return dt;
            }

            // Некорректный формат — возвращаем null (caller проверяет через args).
            return null;
        }
    }
}