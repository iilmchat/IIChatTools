using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.ChatTools
{
    /// <summary>
    /// Реализация сервиса вложений чата
    /// (v1.5.0, KI-083, Шаг 6A).
    ///
    /// <para>
    /// Scoped. Файлы сохраняются в workspace пользователя:
    /// <c>{UserWorkspace}/chat-attachments/{chatId}/{guid}.ext</c>.
    /// Индексация — через <see cref="IDocumentIngestionService"/>
    /// (IndexName = <c>my_rag_docs</c>, ChatId, UserId).
    /// </para>
    /// </summary>
    public sealed class ChatAttachmentService : IChatAttachmentService
    {
        /// <summary>Индекс RAG для документов, приложенных к чату.</summary>
        private const string MyRagDocsIndex = "my_rag_docs";

        /// <summary>Лимит размера одного файла по умолчанию (32 MB).</summary>
        private const int DefaultMaxFileSizeBytes = 33_554_432;

        /// <summary>Лимит файлов на чат по умолчанию.</summary>
        private const int DefaultMaxFilesPerChat = 5;

        /// <summary>Лимит суммарного размера на чат по умолчанию (30 MB).</summary>
        private const long DefaultMaxTotalSizePerChat = 31_457_280;

        /// <summary>Подпапка в workspace для вложений.</summary>
        private const string DefaultStorageSubfolder = "chat-attachments";

        private readonly AppDbContext _db;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly IRagDocumentParserRegistry _parserRegistry;
        private readonly IDocumentIngestionService _ingestionService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatAttachmentService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="db">Контекст БД</param>
        /// <param name="workspaceResolver">Резолвер user-workspace</param>
        /// <param name="parserRegistry">Реестр парсеров (для валидации формата)</param>
        /// <param name="ingestionService">Сервис индексации</param>
        /// <param name="configuration">Конфигурация (Rag:Attachments)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatAttachmentService(
            AppDbContext db,
            IWorkspaceResolver workspaceResolver,
            IRagDocumentParserRegistry parserRegistry,
            IDocumentIngestionService ingestionService,
            IConfiguration configuration,
            ILogger<ChatAttachmentService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _parserRegistry = parserRegistry ?? throw new ArgumentNullException(nameof(parserRegistry));
            _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ChatAttachmentDto> UploadAsync(
            int chatId,
            int userId,
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Имя файла обязательно", nameof(fileName));

            // 1. Проверяем, что чат существует и принадлежит пользователю.
            var chatExists = await _db.Chats
                .AnyAsync(c => c.Id == chatId && c.UserId == userId, cancellationToken);

            if (!chatExists)
                throw new ArgumentException(
                    $"Чат {chatId} не найден или не принадлежит пользователю {userId}.");

            // 2. Читаем содержимое целиком (multipart-stream не seekable).
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await content.CopyToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }

            // 3. Валидация размера одного файла.
            var maxFileSize = GetIntConfig(
                "Rag:Attachments:MaxFileSizeBytes", DefaultMaxFileSizeBytes);

            if (bytes.Length > maxFileSize)
            {
                throw new ArgumentException(
                    $"Файл слишком большой: {bytes.Length} байт " +
                    $"({bytes.Length / 1024.0 / 1024.0:F1} МБ). " +
                    $"Лимит: {maxFileSize} байт ({maxFileSize / 1024.0 / 1024.0:F1} МБ).");
            }

            // 4. Валидация формата (по расширению — через реестр парсеров).
            var parser = _parserRegistry.Resolve(fileName);
            if (parser == null)
            {
                throw new ArgumentException(
                    $"Формат файла не поддерживается: {Path.GetExtension(fileName)}. " +
                    $"Поддерживаемые: {string.Join(", ", _parserRegistry.GetAllSupportedExtensions())}");
            }

            // 5. SHA256 для дедупликации.
            var hash = ComputeSha256Hex(bytes);

            // 6. Дедупликация (Вариант A): тот же файл в том же чате → возвращаем существующую запись.
            var existing = await _db.ChatAttachments
                .FirstOrDefaultAsync(a =>
                    a.UserId == userId &&
                    a.ChatId == chatId &&
                    a.ContentHash == hash,
                    cancellationToken);

            if (existing != null)
            {
                _logger.LogInformation(
                    "Attachment: дубликат (hash={Hash}), возвращаю существующий Id={Id}",
                    hash, existing.Id);
                return ToDto(existing);
            }

            // 7. Лимит на количество файлов в чате.
            var maxFiles = GetIntConfig(
                "Rag:Attachments:MaxFilesPerChat", DefaultMaxFilesPerChat);

            var currentCount = await _db.ChatAttachments
                .CountAsync(a => a.ChatId == chatId, cancellationToken);

            if (currentCount >= maxFiles)
            {
                throw new ArgumentException(
                    $"Достигнут лимит файлов на чат: {maxFiles}. " +
                    $"Удалите одно из вложений или увеличьте Rag:Attachments:MaxFilesPerChat.");
            }

            // 8. Лимит суммарного размера.
            var maxTotalSize = GetLongConfig(
                "Rag:Attachments:MaxTotalSizePerChat", DefaultMaxTotalSizePerChat);

            var currentTotalSize = await _db.ChatAttachments
                .Where(a => a.ChatId == chatId)
                .SumAsync(a => a.SizeBytes, cancellationToken);

            if (currentTotalSize + bytes.Length > maxTotalSize)
            {
                throw new ArgumentException(
                    $"Превышен суммарный размер вложений в чате: " +
                    $"{currentTotalSize + bytes.Length} байт, лимит {maxTotalSize} байт " +
                    $"({maxTotalSize / 1024.0 / 1024.0:F1} МБ).");
            }

            // 9. Сохраняем файл на диск в user-workspace.
            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
            var subfolder = GetStringConfig(
                "Rag:Attachments:StorageSubfolder", DefaultStorageSubfolder);

            var ext = Path.GetExtension(fileName);
            var storedName = Guid.NewGuid().ToString("N") + ext;
            var chatFolder = Path.Combine(workspaceRoot, subfolder, chatId.ToString());
            var fullPath = Path.Combine(chatFolder, storedName);

            Directory.CreateDirectory(chatFolder);
            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

            // Relative path — для UI + citations (StoragePath в БД).
            var storagePath = Path.Combine(subfolder, chatId.ToString(), storedName)
                .Replace('\\', '/');

            // 10. INSERT ChatAttachment.
            var entity = new ChatAttachment
            {
                ChatId = chatId,
                UserId = userId,
                FileName = fileName,
                ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? "application/octet-stream"
                    : contentType,
                SizeBytes = bytes.Length,
                StoragePath = storagePath,
                ContentHash = hash,
                ChunksCount = 0   // заполним после ingestion
            };

            _db.ChatAttachments.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            // 11. Ingestion в my_rag_docs. При ошибке — attachment остаётся
            //     (ChunksCount = 0), файл сохранён, пользователь может повторить.
            try
            {
                var ingestResult = await _ingestionService.IngestAsync(new IngestionRequest
                {
                    IndexName = MyRagDocsIndex,
                    SourceType = IngestionSourceType.File,
                    FilePath = fullPath,
                    ChatId = chatId,
                    UserId = userId,
                    Source = storagePath   // для citations (KI-086, v1.6.0)
                }, cancellationToken);

                entity.ChunksCount = ingestResult.DocumentChunksCreated;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Attachment загружен: id={Id}, chatId={ChatId}, file={File}, " +
                    "size={Size}, chunks={Chunks}, hash={Hash}",
                    entity.Id, chatId, fileName, bytes.Length,
                    entity.ChunksCount, hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ingestion упал для attachment id={Id} (file={File}). " +
                    "Файл сохранён, запись в БД создана, но чанков нет (ChunksCount=0). " +
                    "Пользователь может удалить и перезагрузить файл.",
                    entity.Id, fileName);
            }

            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChatAttachmentDto>> GetForChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var items = await _db.ChatAttachments
                .AsNoTracking()
                .Where(a => a.ChatId == chatId && a.UserId == userId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(cancellationToken);

            return items.Select(ToDto).ToList();
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(
            int attachmentId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var entity = await _db.ChatAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId,
                    cancellationToken);

            if (entity == null)
                return false;

            // 1. Удаляем чанки из my_rag_docs.
            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
            var fullPath = Path.Combine(workspaceRoot,
                entity.StoragePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                await _ingestionService.DeleteDocumentAsync(
                    MyRagDocsIndex, fullPath, chatId: entity.ChatId,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось удалить чанки для attachment id={Id} (path={Path}). Продолжаем.",
                    entity.Id, entity.StoragePath);
            }

            // 2. Удаляем физический файл (best-effort, не падаем).
            try
            {
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось удалить файл {Path} для attachment id={Id}. Продолжаем.",
                    fullPath, entity.Id);
            }

            // 3. Удаляем запись из БД.
            _db.ChatAttachments.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Attachment удалён: id={Id}, chatId={ChatId}, file={File}",
                entity.Id, entity.ChatId, entity.FileName);

            return true;
        }

        /// <inheritdoc />
        public async Task<int> ClearForChatAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var items = await _db.ChatAttachments
                .Where(a => a.ChatId == chatId && a.UserId == userId)
                .ToListAsync(cancellationToken);

            if (items.Count == 0)
                return 0;

            // 1. Удаляем все чанки из my_rag_docs одним вызовом.
            try
            {
                await _ingestionService.ClearIndexAsync(
                    MyRagDocsIndex, chatId: chatId,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ClearIndexAsync (my_rag_docs, chatId={ChatId}) упал. Продолжаем удаление файлов.",
                    chatId);
            }

            // 2. Удаляем физические файлы (best-effort).
            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
            foreach (var item in items)
            {
                try
                {
                    var fullPath = Path.Combine(workspaceRoot,
                        item.StoragePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Не удалось удалить файл {Path} (attachment id={Id})",
                        item.StoragePath, item.Id);
                }
            }

            // 3. Удаляем папку chat-attachments/{chatId}, если пуста.
            try
            {
                var subfolder = GetStringConfig(
                    "Rag:Attachments:StorageSubfolder", DefaultStorageSubfolder);
                var chatFolder = Path.Combine(workspaceRoot, subfolder, chatId.ToString());

                if (Directory.Exists(chatFolder)
                    && !Directory.EnumerateFileSystemEntries(chatFolder).Any())
                {
                    Directory.Delete(chatFolder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Не удалось удалить пустую папку chat-attachments/{ChatId}", chatId);
            }

            // 4. Удаляем записи из БД.
            _db.ChatAttachments.RemoveRange(items);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Очистка вложений чата {ChatId}: удалено {Count} файлов",
                chatId, items.Count);

            return items.Count;
        }

        // ============================================================
        // Внутренние
        // ============================================================

        /// <summary>
        /// Хэш SHA256 содержимого (hex, lower case).
        /// </summary>
        private static string ComputeSha256Hex(byte[] bytes)
        {
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>Проекция в DTO.</summary>
        private static ChatAttachmentDto ToDto(ChatAttachment entity)
        {
            return new ChatAttachmentDto
            {
                Id = entity.Id,
                ChatId = entity.ChatId,
                UserId = entity.UserId,
                FileName = entity.FileName,
                ContentType = entity.ContentType,
                SizeBytes = entity.SizeBytes,
                ChunksCount = entity.ChunksCount,
                UploadedAt = entity.CreatedAt
            };
        }

        /// <summary>Читает int из конфига.</summary>
        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>Читает long из конфига.</summary>
        private long GetLongConfig(string key, long defaultValue)
        {
            var raw = _configuration[key];
            return long.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>Читает string из конфига.</summary>
        private string GetStringConfig(string key, string defaultValue)
        {
            var raw = _configuration[key];
            return string.IsNullOrWhiteSpace(raw) ? defaultValue : raw;
        }
    }
}