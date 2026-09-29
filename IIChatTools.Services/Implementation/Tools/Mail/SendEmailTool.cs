using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Mail
{
    /// <summary>
    /// Инструмент LLM: отправка письма через SMTP
    /// (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 4.3).
    ///
    /// <para>
    /// <b>Mutating. <see cref="RequiresApprovalByDefault"/> = true</b> — пользователь
    /// видит модалку с адресатами и темой перед отправкой.
    /// </para>
    ///
    /// <para>
    /// <b>Rate limiting</b> (20 писем/час) — Фаза 4. В Фазе 3A вызываем SendAsync
    /// напрямую, без лимита. TODO(Фаза 4): инжектнуть IMailRateLimiter.
    /// </para>
    ///
    /// <para>
    /// <b>Privacy:</b> в логах — только количество получателей и вложений.
    /// Никогда — адреса / Subject / Body.
    /// </para>
    /// </summary>
    public sealed class SendEmailTool : ITool
    {
        /// <summary>Максимум получателей (To + Cc + Bcc) в одном письме (DESIGN § 5.2).</summary>
        private const int MaxRecipientsTotal = 10;

        private readonly IMailClient _mailClient;
        private readonly ILogger<SendEmailTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="mailClient">IMAP/SMTP-клиент</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public SendEmailTool(
            IMailClient mailClient,
            ILogger<SendEmailTool> logger)
        {
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "send_email";

        /// <inheritdoc />
        public string Description =>
            "Отправляет письмо через SMTP. ТРЕБУЕТ ПОДТВЕРЖДЕНИЯ пользователя. " +
            "Параметры: to (массив адресов), cc/bcc (опционально), subject, body, " +
            "isHtml (по умолчанию false), attachments (опционально — относительные " +
            "пути в workspace). НИКОГДА не вызывай без явной просьбы пользователя. " +
            "ВСЕГДА подтверждай адресата и тему в финальном ответе. " +
            "Максимум получателей всего: 10. Максимум вложений: 5.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "to",
                Type = "array",
                Description = "Массив получателей (обязательно, ≥ 1). Формат: [\"user@example.com\"].",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "cc",
                Type = "array",
                Description = "Массив в копии (опционально).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "bcc",
                Type = "array",
                Description = "Массив в скрытой копии (опционально).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "subject",
                Type = "string",
                Description = "Тема письма.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "body",
                Type = "string",
                Description = "Тело письма (markdown-подобное, если isHtml=false — как plain text).",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "isHtml",
                Type = "boolean",
                Description = "Отправлять как HTML (по умолчанию false — plain text).",
                Required = false,
                Default = false
            },
            new ToolParameterDescriptor
            {
                Name = "attachments",
                Type = "array",
                Description = "Массив относительных путей вложений в workspace (Фаза 4). " +
                              "Сейчас не поддерживается — письмо будет отправлено без вложений.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                if (context == null || context.UserId <= 0)
                    return ToolResult.Fail("UserId не задан в контексте.");

                // --- Валидация ---
                var to = ParseStringArray(arguments, "to");
                if (to.Count == 0)
                    return ToolResult.Fail("Не указан параметр 'to' (массив адресов получателей).");

                var cc = ParseStringArray(arguments, "cc");
                var bcc = ParseStringArray(arguments, "bcc");
                var attachments = ParseStringArray(arguments, "attachments");

                var totalRecipients = to.Count + cc.Count + bcc.Count;
                if (totalRecipients > MaxRecipientsTotal)
                {
                    return ToolResult.Fail(
                        $"Слишком много получателей: {totalRecipients}. " +
                        $"Лимит: {MaxRecipientsTotal} (To + Cc + Bcc).");
                }

                // Простая валидация адресов.
                var invalidTo = to.FirstOrDefault(IsInvalidEmail);
                if (invalidTo != null)
                    return ToolResult.Fail($"Некорректный адрес в 'to': {invalidTo}");

                var invalidCc = cc.FirstOrDefault(IsInvalidEmail);
                if (invalidCc != null)
                    return ToolResult.Fail($"Некорректный адрес в 'cc': {invalidCc}");

                var invalidBcc = bcc.FirstOrDefault(IsInvalidEmail);
                if (invalidBcc != null)
                    return ToolResult.Fail($"Некорректный адрес в 'bcc': {invalidBcc}");

                var subject = arguments?["subject"]?.ToString() ?? string.Empty;
                var body = arguments?["body"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(body))
                    return ToolResult.Fail("Не указано тело письма ('body').");

                var isHtml = ParseBool(arguments, "isHtml", false);

                // --- Сборка request ---
                var request = new SendMailRequest
                {
                    To = to,
                    Cc = cc,
                    Bcc = bcc,
                    Subject = subject,
                    Body = body,
                    IsHtml = isHtml,
                    Attachments = attachments
                };

                // TODO (Фаза 4): rate limiter (20 писем/час) + attachment service.

                // --- Отправка ---
                await _mailClient.SendAsync(request, context.UserId, context.CancellationToken);

                // Privacy: только количество получателей и вложений.
                _logger.LogInformation(
                    "Mail: send_email recipients={Recipients} cc={Cc} bcc={Bcc} attachments={Attachments} html={Html}",
                    to.Count, cc.Count, bcc.Count, attachments.Count, isHtml);

                var sentAt = DateTime.UtcNow;
                var data = new
                {
                    to = to.ToArray(),
                    cc = cc.ToArray(),
                    subject = subject,   // echo — пользователь и так его видит
                    attachmentsCount = attachments.Count,
                    isHtml,
                    sentAt
                };

                var attachNote = attachments.Count > 0
                    ? $" (вложения: {attachments.Count} — Фаза 4, пока не отправлены)"
                    : string.Empty;

                return ToolResult.Ok(
                    data,
                    $"Письмо отправлено: {to.Count} получателей, тема \"{TruncateForMessage(subject)}\"{attachNote}.");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("Операция отменена.");
            }
            catch (Exception ex)
            {
                // Privacy: лог без адресов / Subject / Body.
                _logger.LogError(ex, "SendEmailTool: ошибка отправки");
                return ToolResult.Fail($"Ошибка отправки письма: {ex.Message}");
            }
        }

        // ============ Парсеры JObject ============

        /// <summary>
        /// Парсит JArray строк (для to/cc/bcc/attachments).
        /// </summary>
        private static IReadOnlyList<string> ParseStringArray(JObject args, string key)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null)
                return Array.Empty<string>();

            var result = new List<string>();
            if (raw.Type == JTokenType.Array)
            {
                foreach (var item in raw)
                {
                    if (item == null || item.Type == JTokenType.Null) continue;
                    var s = item.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
                }
            }
            else
            {
                // Одиночное значение — допускаем.
                var s = raw.ToString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            }
            return result;
        }

        private static bool ParseBool(JObject args, string key, bool defaultValue)
        {
            var raw = args?[key];
            if (raw == null || raw.Type == JTokenType.Null) return defaultValue;
            if (raw.Type == JTokenType.Boolean) return raw.Value<bool>();
            return bool.TryParse(raw.ToString(), out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Простая проверка email (содержит '@' и '.' после него).
        /// Строгая валидация — на стороне SMTP-сервера.
        /// </summary>
        private static bool IsInvalidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true;
            var at = email.IndexOf('@');
            if (at <= 0 || at == email.Length - 1) return true;
            var dot = email.LastIndexOf('.');
            return dot < at + 2 || dot == email.Length - 1;
        }

        /// <summary>
        /// Обрезает тему для сообщения об успехе (защита от гигантских Subject в логе ответа).
        /// </summary>
        private static string TruncateForMessage(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "(без темы)";
            return subject.Length <= 80 ? subject : subject.Substring(0, 80) + "…";
        }
    }
}