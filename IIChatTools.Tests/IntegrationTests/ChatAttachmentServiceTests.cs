using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.ChatTools;
using IIChatTools.Services.Implementation.Rag.Parsers;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIChatTools.Tests.IntegrationTests
{
    /// <summary>
    /// Тесты <see cref="ChatAttachmentService"/>
    /// (v1.5.0, KI-083, Шаг 6A-тесты).
    ///
    /// <para>
    /// 11 тестов: загрузка/удаление/очистка вложений, валидация лимитов,
    /// дедупликация, изоляция по пользователю/чату.
    /// </para>
    ///
    /// <para>
    /// Fake: <see cref="FakeWorkspaceResolver"/>, <see cref="FakeIngestionService"/>.
    /// Реальные: <see cref="RagDocumentParserRegistry"/> + <see cref="PlainTextParser"/>,
    /// <c>AppDbContext</c> через <see cref="TestDbContextFactory"/>.
    /// </para>
    /// </summary>
    public class ChatAttachmentServiceTests
    {
        // ============================================================
        // Хелперы
        // ============================================================

        /// <summary>
        /// Создаёт temp-root для теста. Удаляется вызывающим кодом в finally.
        /// </summary>
        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(),
                "iichattools_attach_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// Создаёт чат в БД (минимально).
        /// </summary>
        private static async Task<int> CreateChatAsync(AppDbContext db, int userId, string title = "test chat")
        {
            var chat = new Chat
            {
                UserId = userId,
                Title = title,
                Model = "test-model",
                UpdatedAt = DateTime.UtcNow
            };
            db.Chats.Add(chat);
            await db.SaveChangesAsync();
            return chat.Id;
        }

        /// <summary>
        /// Создаёт сервис + fake-зависимости + temp-root.
        /// </summary>
        private static (ChatAttachmentService Service,
                        AppDbContext Db,
                        FakeWorkspaceResolver Resolver,
                        FakeIngestionService Ingestion,
                        string TempRoot)
            CreateService(IDictionary<string, string> configOverrides = null)
        {
            var tempRoot = CreateTempRoot();
            var db = TestDbContextFactory.Create();
            var resolver = new FakeWorkspaceResolver(tempRoot);
            var ingestion = new FakeIngestionService();

            var parserRegistry = new RagDocumentParserRegistry(
                new IRagDocumentParser[] { new PlainTextParser() });

            var settings = new Dictionary<string, string>
            {
                ["Rag:Attachments:MaxFileSizeBytes"] = "1048576",         // 1 MB
                ["Rag:Attachments:MaxFilesPerChat"] = "5",
                ["Rag:Attachments:MaxTotalSizePerChat"] = "5242880",      // 5 MB
                ["Rag:Attachments:StorageSubfolder"] = "chat-attachments"
            };

            if (configOverrides != null)
            {
                foreach (var kv in configOverrides)
                    settings[kv.Key] = kv.Value;
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var service = new ChatAttachmentService(
                db,
                resolver,
                parserRegistry,
                ingestion,
                config,
                NullLogger<ChatAttachmentService>.Instance);

            return (service, db, resolver, ingestion, tempRoot);
        }

        /// <summary>
        /// Хелпер: собрать MemoryStream из строки (UTF-8).
        /// </summary>
        private static MemoryStream MakeStream(string content)
            => new MemoryStream(new UTF8Encoding(false).GetBytes(content));

        /// <summary>
        /// Хелпер: собрать MemoryStream из байтов.
        /// </summary>
        private static MemoryStream MakeStream(byte[] bytes)
            => new MemoryStream(bytes);

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// Успешная загрузка: создаётся attachment + вызов ingestion.
        /// </summary>
        [Fact]
        public async Task UploadAsync_ValidFile_CreatesAttachmentAndChunks()
        {
            var (service, db, _, ingestion, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                var dto = await service.UploadAsync(
                    chatId, userId: 1,
                    content: MakeStream("Hello, world!"),
                    fileName: "hello.txt",
                    contentType: "text/plain");

                Assert.NotNull(dto);
                Assert.True(dto.Id > 0);
                Assert.Equal(chatId, dto.ChatId);
                Assert.Equal(1, dto.UserId);
                Assert.Equal("hello.txt", dto.FileName);
                Assert.Equal("text/plain", dto.ContentType);
                Assert.True(dto.SizeBytes > 0);
                Assert.Equal(3, dto.ChunksCount);   // из FakeIngestionService.NextChunksCreated

                Assert.Equal(1, db.ChatAttachments.Count());

                // Ingestion вызван с правильными параметрами.
                Assert.Single(ingestion.IngestCalls);
                var call = ingestion.IngestCalls[0];
                Assert.Equal("my_rag_docs", call.IndexName);
                Assert.Equal(IngestionSourceType.File, call.SourceType);
                Assert.Equal(chatId, call.ChatId);
                Assert.Equal(1, call.UserId);

                // Файл на диске называется {guid}.txt (DESIGN § 4.7.6),
                // поэтому проверяем расширение + подпапку, а не исходное имя.
                Assert.EndsWith(".txt", call.FilePath ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Contains("chat-attachments", call.FilePath ?? string.Empty);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Файл больше MaxFileSizeBytes → ArgumentException.
        /// </summary>
        [Fact]
        public async Task UploadAsync_TooLarge_Throws()
        {
            var (service, db, _, _, tempRoot) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Attachments:MaxFileSizeBytes"] = "10"
                });
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);
                var content = new string('x', 100);   // 100 байт

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UploadAsync(chatId, 1, MakeStream(content), "big.txt", "text/plain"));

                Assert.Contains("слишком большой", ex.Message);
                Assert.Equal(0, db.ChatAttachments.Count());
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Неподдерживаемый формат (.xyz) → ArgumentException.
        /// </summary>
        [Fact]
        public async Task UploadAsync_UnsupportedFormat_Throws()
        {
            var (service, db, _, _, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UploadAsync(chatId, 1, MakeStream("abc"), "file.xyz", "application/octet-stream"));

                Assert.Contains("не поддерживается", ex.Message);
                Assert.Equal(0, db.ChatAttachments.Count());
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Превышение MaxFilesPerChat → ArgumentException.
        /// </summary>
        [Fact]
        public async Task UploadAsync_ExceedsFilesPerChat_Throws()
        {
            var (service, db, _, _, tempRoot) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Attachments:MaxFilesPerChat"] = "2"
                });
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                await service.UploadAsync(chatId, 1, MakeStream("first"), "a.txt", "text/plain");
                await service.UploadAsync(chatId, 1, MakeStream("second"), "b.txt", "text/plain");

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UploadAsync(chatId, 1, MakeStream("third"), "c.txt", "text/plain"));

                Assert.Contains("лимит файлов", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, db.ChatAttachments.Count());
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Превышение MaxTotalSizePerChat → ArgumentException.
        /// </summary>
        [Fact]
        public async Task UploadAsync_ExceedsTotalSize_Throws()
        {
            var (service, db, _, _, tempRoot) = CreateService(configOverrides:
                new Dictionary<string, string>
                {
                    ["Rag:Attachments:MaxFileSizeBytes"] = "1000",
                    ["Rag:Attachments:MaxTotalSizePerChat"] = "100"
                });
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                // Файл 80 байт — ок (80 < 100).
                await service.UploadAsync(chatId, 1,
                    MakeStream(new string('a', 80)), "a.txt", "text/plain");

                // Второй 80 байт — 80+80 = 160 > 100 → ошибка.
                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UploadAsync(chatId, 1,
                        MakeStream(new string('b', 80)), "b.txt", "text/plain"));

                Assert.Contains("суммарный размер", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, db.ChatAttachments.Count());
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Дедупликация (Вариант A): тот же файл в том же чате → возвращает существующий.
        /// </summary>
        [Fact]
        public async Task UploadAsync_SameHash_ReturnsExisting()
        {
            var (service, db, _, ingestion, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                var first = await service.UploadAsync(chatId, 1,
                    MakeStream("identical content"), "a.txt", "text/plain");

                var second = await service.UploadAsync(chatId, 1,
                    MakeStream("identical content"), "b.txt", "text/plain");

                // Тот же attachment.
                Assert.Equal(first.Id, second.Id);
                Assert.Equal(1, db.ChatAttachments.Count());

                // Ingestion вызван только один раз.
                Assert.Single(ingestion.IngestCalls);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Чужой чат (не принадлежит пользователю) → ArgumentException.
        /// </summary>
        [Fact]
        public async Task UploadAsync_ChatNotOwned_Throws()
        {
            var (service, db, _, _, tempRoot) = CreateService();
            try
            {
                // Чат пользователя 2.
                var foreignChatId = await CreateChatAsync(db, userId: 2);

                // Пытаемся загрузить от имени пользователя 1.
                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UploadAsync(foreignChatId, 1,
                        MakeStream("hack"), "f.txt", "text/plain"));

                Assert.Contains("не найден или не принадлежит", ex.Message);
                Assert.Equal(0, db.ChatAttachments.Count());
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// Файл физически сохраняется в {UserWorkspace}/chat-attachments/{chatId}/{guid}.ext.
        /// </summary>
        [Fact]
        public async Task UploadAsync_SavesFileInUserWorkspace()
        {
            var (service, db, resolver, _, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);
                var userWorkspace = await resolver.GetWorkspacePathAsync(1);

                await service.UploadAsync(chatId, 1,
                    MakeStream("saved to disk"), "test.txt", "text/plain");

                var expectedFolder = Path.Combine(userWorkspace, "chat-attachments", chatId.ToString());
                Assert.True(Directory.Exists(expectedFolder),
                    $"Папка {expectedFolder} не создана.");

                var files = Directory.GetFiles(expectedFolder);
                Assert.Single(files);
                Assert.EndsWith(".txt", files[0]);

                // Проверяем содержимое.
                var onDisk = await File.ReadAllTextAsync(files[0]);
                Assert.Equal("saved to disk", onDisk);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// DeleteAsync: удаляет файл, чанки (через ingestion) и запись из БД.
        /// </summary>
        [Fact]
        public async Task DeleteAsync_RemovesFileAndChunks()
        {
            var (service, db, _, ingestion, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                var dto = await service.UploadAsync(chatId, 1,
                    MakeStream("to delete"), "del.txt", "text/plain");

                // Найти физический файл ДО удаления.
                var workspace = await new FakeWorkspaceResolver(tempRoot).GetWorkspacePathAsync(1);
                var folder = Path.Combine(workspace, "chat-attachments", chatId.ToString());
                var filesBefore = Directory.GetFiles(folder);
                Assert.Single(filesBefore);

                var result = await service.DeleteAsync(dto.Id, userId: 1);

                Assert.True(result);
                Assert.Equal(0, db.ChatAttachments.Count());
                Assert.Single(ingestion.DeleteCalls);
                Assert.Equal("my_rag_docs", ingestion.DeleteCalls[0].Index);
                Assert.Equal(chatId, ingestion.DeleteCalls[0].ChatId);

                // Файл удалён.
                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// ClearForChatAsync удаляет все вложения + чанки + папку.
        /// </summary>
        [Fact]
        public async Task ClearForChatAsync_RemovesAll()
        {
            var (service, db, resolver, ingestion, tempRoot) = CreateService();
            try
            {
                var chatId = await CreateChatAsync(db, userId: 1);

                await service.UploadAsync(chatId, 1, MakeStream("first"), "a.txt", "text/plain");
                await service.UploadAsync(chatId, 1, MakeStream("second"), "b.txt", "text/plain");

                Assert.Equal(2, db.ChatAttachments.Count());

                var removed = await service.ClearForChatAsync(chatId, userId: 1);

                Assert.Equal(2, removed);
                Assert.Equal(0, db.ChatAttachments.Count());
                Assert.Single(ingestion.ClearCalls);
                Assert.Equal("my_rag_docs", ingestion.ClearCalls[0].Index);
                Assert.Equal(chatId, ingestion.ClearCalls[0].ChatId);

                // Папка chatId удалена (была пуста).
                var workspace = await resolver.GetWorkspacePathAsync(1);
                var chatFolder = Path.Combine(workspace, "chat-attachments", chatId.ToString());
                Assert.False(Directory.Exists(chatFolder),
                    $"Папка {chatFolder} должна быть удалена после ClearForChat.");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// GetForChatAsync возвращает только вложения указанного чата/пользователя.
        /// </summary>
        [Fact]
        public async Task GetForChatAsync_ReturnsOnlyOwnAttachments()
        {
            var (service, db, _, _, tempRoot) = CreateService();
            try
            {
                // Два чата одного пользователя.
                var chat1 = await CreateChatAsync(db, userId: 1, title: "chat1");
                var chat2 = await CreateChatAsync(db, userId: 1, title: "chat2");

                await service.UploadAsync(chat1, 1, MakeStream("content 1"), "one.txt", "text/plain");
                await service.UploadAsync(chat2, 1, MakeStream("content 2"), "two.txt", "text/plain");

                var chat1Attachments = await service.GetForChatAsync(chat1, userId: 1);
                var chat2Attachments = await service.GetForChatAsync(chat2, userId: 1);

                Assert.Single(chat1Attachments);
                Assert.Single(chat2Attachments);
                Assert.Equal("one.txt", chat1Attachments[0].FileName);
                Assert.Equal("two.txt", chat2Attachments[0].FileName);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}