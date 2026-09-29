using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;
using IIChatTools.Services.Implementation.Mail;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Mail
{
    /// <summary>
    /// Тесты <see cref="MailAttachmentService"/> (Фаза 4, v1.8.0, KI-107).
    /// Используют <see cref="FakeWorkspaceResolver"/> (temp-dir) + реальную ФС.
    /// </summary>
    public class MailAttachmentServiceTests
    {
        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(),
                "iichattools_mail_att_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static (MailAttachmentService Service, string TempRoot) CreateService(
            MailAttachmentsOptions attachmentsOpts = null)
        {
            var tempRoot = CreateTempRoot();
            var resolver = new FakeWorkspaceResolver(tempRoot);

            var options = new MailOptions
            {
                Attachments = attachmentsOpts ?? new MailAttachmentsOptions
                {
                    MaxFileSizeBytes = 1_048_576,       // 1 MB
                    MaxTotalSizeBytes = 2_097_152,      // 2 MB
                    MaxFilesPerMessage = 3,
                    StorageSubfolder = "mail-attachments"
                }
            };

            var service = new MailAttachmentService(
                resolver, Options.Create(options),
                NullLogger<MailAttachmentService>.Instance);

            return (service, tempRoot);
        }

        // ============================================================
        // SaveIncomingAsync
        // ============================================================

        [Fact]
        public async Task SaveIncomingAsync_ValidFile_SavesToWorkspace()
        {
            var (service, tempRoot) = CreateService();
            try
            {
                var content = Encoding.UTF8.GetBytes("Hello attachment");
                var dto = await service.SaveIncomingAsync(
                    userId: 1, uid: 42,
                    originalFileName: "test.txt",
                    content: content,
                    contentType: "text/plain");

                Assert.NotNull(dto);
                Assert.Equal("test.txt", dto.FileName);
                Assert.Equal("text/plain", dto.ContentType);
                Assert.Equal(content.Length, dto.SizeBytes);
                Assert.StartsWith("mail-attachments/42/", dto.StoragePath);
                Assert.EndsWith(".txt", dto.StoragePath);

                // Проверка файла на диске.
                var fullPath = Path.Combine(tempRoot, "users", "1",
                    dto.StoragePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(fullPath));
                Assert.Equal(content, await File.ReadAllBytesAsync(fullPath));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task SaveIncomingAsync_TooLarge_Throws()
        {
            var (service, tempRoot) = CreateService(new MailAttachmentsOptions
            {
                MaxFileSizeBytes = 100,
                MaxTotalSizeBytes = 1000,
                MaxFilesPerMessage = 5,
                StorageSubfolder = "mail-attachments"
            });
            try
            {
                var content = new byte[200];
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.SaveIncomingAsync(1, 42, "big.bin", content, "application/octet-stream"));

                Assert.Contains("превышает лимит", ex.Message);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task SaveIncomingAsync_Duplicate_DoesNotRewrite()
        {
            var (service, tempRoot) = CreateService();
            try
            {
                var content = Encoding.UTF8.GetBytes("Same content");

                var dto1 = await service.SaveIncomingAsync(1, 42, "a.txt", content, "text/plain");
                var dto2 = await service.SaveIncomingAsync(1, 42, "b.txt", content, "text/plain");

                // Дедупликация по SHA256 → одинаковый StoragePath.
                Assert.Equal(dto1.StoragePath, dto2.StoragePath);
                Assert.Equal("a.txt", dto1.FileName);   // первое сохранённое имя
                Assert.Equal("b.txt", dto2.FileName);   // второе имя (метаданные)
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        // ============================================================
        // ResolveForSendAsync
        // ============================================================

        [Fact]
        public async Task ResolveForSendAsync_ValidPaths_ReturnsFullPaths()
        {
            var (service, tempRoot) = CreateService();
            try
            {
                // Создаём файлы в workspace.
                var workspace = Path.Combine(tempRoot, "users", "1");
                Directory.CreateDirectory(workspace);
                File.WriteAllText(Path.Combine(workspace, "a.txt"), "A");
                File.WriteAllText(Path.Combine(workspace, "b.txt"), "B");

                var paths = await service.ResolveForSendAsync(
                    userId: 1,
                    relativePaths: new[] { "a.txt", "b.txt" });

                Assert.Equal(2, paths.Count);
                foreach (var p in paths) Assert.True(File.Exists(p));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task ResolveForSendAsync_PathTraversal_Throws()
        {
            var (service, tempRoot) = CreateService();
            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ResolveForSendAsync(1, new[] { "../../etc/passwd" }));

                Assert.Contains("выходит за пределы", ex.Message);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task ResolveForSendAsync_FileNotExists_Throws()
        {
            var (service, tempRoot) = CreateService();
            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ResolveForSendAsync(1, new[] { "nonexistent.txt" }));

                Assert.Contains("не найден", ex.Message);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public async Task ResolveForSendAsync_TooManyFiles_Throws()
        {
            var (service, tempRoot) = CreateService(new MailAttachmentsOptions
            {
                MaxFileSizeBytes = 10_000,
                MaxTotalSizeBytes = 10_000,
                MaxFilesPerMessage = 2,
                StorageSubfolder = "mail-attachments"
            });
            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ResolveForSendAsync(1, new[] { "a.txt", "b.txt", "c.txt" }));

                Assert.Contains("Слишком много вложений", ex.Message);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}