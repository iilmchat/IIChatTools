using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.Mail
{
    /// <summary>
    /// Сервис вложений Mail Agent (v1.8.0, KI-107, DESIGN_MAIL_AGENT § 3.5 и § 6.3).
    ///
    /// <para>
    /// Работает только с workspace пользователя — не с произвольной ФС.
    /// При чтении — сохраняет вложения в <c>mail-attachments/{uid}/{sha256}.ext</c>
    /// (дедупликация по хешу содержимого — повторное чтение того же вложения
    /// не создаёт дубликат).
    /// </para>
    ///
    /// <para>
    /// <b>Lifecycle:</b> Scoped (зависит от <see cref="IWorkspaceResolver"/>).
    /// </para>
    /// </summary>
    public sealed class MailAttachmentService : IMailAttachmentService
    {
        private const string DefaultStorageSubfolder = "mail-attachments";

        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly MailOptions _options;
        private readonly ILogger<MailAttachmentService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="workspaceResolver">Резолвер user-workspace</param>
        /// <param name="options">Опции Mail Agent (лимиты вложений)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если параметр null</exception>
        public MailAttachmentService(
            IWorkspaceResolver workspaceResolver,
            IOptions<MailOptions> options,
            ILogger<MailAttachmentService> logger)
        {
            _workspaceResolver = workspaceResolver
                ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _options = options?.Value
                ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<MailAttachmentDto> SaveIncomingAsync(
            int userId, uint uid,
            string originalFileName, byte[] content, string contentType,
            CancellationToken ct = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (string.IsNullOrWhiteSpace(originalFileName))
                originalFileName = "attachment";

            var attachmentsOpts = _options.Attachments ?? new MailAttachmentsOptions();

            // Лимит размера одного вложения.
            if (content.Length > attachmentsOpts.MaxFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Вложение '{originalFileName}' превышает лимит " +
                    $"{attachmentsOpts.MaxFileSizeBytes / 1024 / 1024} МБ " +
                    $"(фактический размер: {content.Length / 1024 / 1024} МБ).");
            }

            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
            var subfolder = string.IsNullOrWhiteSpace(attachmentsOpts.StorageSubfolder)
                ? DefaultStorageSubfolder
                : attachmentsOpts.StorageSubfolder;

            // Дедупликация по SHA256: имя файла = {sha256}.ext.
            // Повторное сохранение того же вложения (по содержимому) → тот же путь.
            var hash = ComputeSha256Hex(content);
            var ext = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

            // Защита от path-traversal в ext: берём только буквы/цифры/точку.
            ext = SanitizeExtension(ext);

            var storedName = hash + ext;
            var folder = Path.Combine(workspaceRoot, subfolder, uid.ToString());
            var fullPath = Path.Combine(folder, storedName);

            // Проверка, что итоговый путь внутри workspace (защита от ..).
            if (!PathHelper.TryGetSafeFullPath(
                    Path.Combine(subfolder, uid.ToString(), storedName),
                    workspaceRoot,
                    out var safePath))
            {
                throw new InvalidOperationException(
                    "Не удалось получить безопасный путь для вложения.");
            }

            Directory.CreateDirectory(folder);

            if (!File.Exists(safePath))
            {
                await File.WriteAllBytesAsync(safePath, content, ct);
                _logger.LogDebug(
                    "Mail: attachment сохранён ({File}, {Bytes} байт, uid={Uid})",
                    originalFileName, content.Length, uid);
            }
            else
            {
                _logger.LogDebug(
                    "Mail: attachment дедуплицирован (уже есть, hash={Hash}, uid={Uid})",
                    hash.Substring(0, 8), uid);
            }

            var storagePath = Path.Combine(subfolder, uid.ToString(), storedName)
                .Replace('\\', '/');

            return new MailAttachmentDto
            {
                FileName = originalFileName,
                ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? "application/octet-stream"
                    : contentType,
                SizeBytes = content.Length,
                StoragePath = storagePath
            };
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ResolveForSendAsync(
            int userId, IReadOnlyList<string> relativePaths,
            CancellationToken ct = default)
        {
            var result = new List<string>();

            if (relativePaths == null || relativePaths.Count == 0)
                return result;

            var attachmentsOpts = _options.Attachments ?? new MailAttachmentsOptions();

            // Лимит количества вложений.
            if (relativePaths.Count > attachmentsOpts.MaxFilesPerMessage)
            {
                throw new InvalidOperationException(
                    $"Слишком много вложений: {relativePaths.Count}. " +
                    $"Лимит: {attachmentsOpts.MaxFilesPerMessage}.");
            }

            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
            long totalSize = 0;

            foreach (var rel in relativePaths)
            {
                if (string.IsNullOrWhiteSpace(rel))
                    continue;

                // PathHelper — защита от ..\..\ и выход за пределы workspace (RULES § 1.9).
                if (!PathHelper.TryGetSafeFullPath(rel, workspaceRoot, out var fullPath))
                {
                    throw new InvalidOperationException(
                        $"Путь '{rel}' выходит за пределы workspace или недопустим.");
                }

                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException(
                        $"Файл '{rel}' не найден в workspace.");
                }

                var fileSize = new FileInfo(fullPath).Length;

                if (fileSize > attachmentsOpts.MaxFileSizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Вложение '{rel}' превышает лимит " +
                        $"{attachmentsOpts.MaxFileSizeBytes / 1024 / 1024} МБ " +
                        $"(фактический размер: {fileSize / 1024 / 1024} МБ).");
                }

                totalSize += fileSize;
                result.Add(fullPath);
            }

            // Лимит суммарного размера.
            if (totalSize > attachmentsOpts.MaxTotalSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Суммарный размер вложений ({totalSize / 1024 / 1024} МБ) " +
                    $"превышает лимит {attachmentsOpts.MaxTotalSizeBytes / 1024 / 1024} МБ.");
            }

            _logger.LogDebug(
                "Mail: resolve attachments ok ({Count} файлов, {Total} байт)",
                result.Count, totalSize);

            return result;
        }

        // ============ Private ============

        private static string ComputeSha256Hex(byte[] bytes)
        {
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Оставляет в расширении только буквы, цифры и точку.
        /// Например, ".pdf" → ".pdf", "../..pdf" → ".pdf" (безопасно).
        /// </summary>
        private static string SanitizeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return ".bin";
            var sb = new System.Text.StringBuilder(ext.Length);
            foreach (var c in ext)
            {
                if (char.IsLetterOrDigit(c) || c == '.') sb.Append(c);
            }
            var result = sb.ToString();
            return result.Length <= 1 ? ".bin" : result;
        }
    }
}