using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionScreenshotCleaner"/>
    /// (v1.12.0, KI-131, Ф6.6).
    /// </summary>
    public class VisionScreenshotCleanerTests : IDisposable
    {
        private readonly string _tempRoot;

        public VisionScreenshotCleanerTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "vision-cleaner-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
        }

        private AppDbContext CreateInMemoryDb(params int[] userIds)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("vision-cleaner-" + Guid.NewGuid().ToString("N"))
                .Options;
            var db = new AppDbContext(options);

            foreach (var id in userIds)
            {
                db.Users.Add(new ApplicationUser { Id = id, UserName = $"user{id}" });
            }
            db.SaveChanges();
            return db;
        }

        private VisionScreenshotCleaner CreateCleaner(
            AppDbContext db,
            string userWorkspaceRoot,
            int retentionHours = 1,
            bool saveToWorkspace = true)
        {
            var resolverMock = new Mock<IWorkspaceResolver>();
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(It.IsAny<int>()))
                .ReturnsAsync(userWorkspaceRoot);

            var options = new VisionAgentOptions
            {
                Privacy = new VisionPrivacyOptions
                {
                    SaveToWorkspace = saveToWorkspace,
                    WorkspaceRetentionHours = retentionHours
                }
            };

            return new VisionScreenshotCleaner(
                db,
                resolverMock.Object,
                Options.Create(options),
                NullLogger<VisionScreenshotCleaner>.Instance);
        }

        private string CreateTaskFolder(int userId, string taskId, DateTime lastWriteUtc)
        {
            var userRoot = Path.Combine(_tempRoot, $"user{userId}");
            var taskDir = Path.Combine(userRoot, "screenshots", taskId);
            Directory.CreateDirectory(taskDir);

            var file = Path.Combine(taskDir, "step-001.png");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5 });

            Directory.SetLastWriteTimeUtc(taskDir, lastWriteUtc);
            return taskDir;
        }

        // ============ 1. Старая папка удаляется ============

        [Fact]
        public async Task CleanupAsync_OldFolder_Deleted()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            var oldDir = CreateTaskFolder(1, "vt_old", DateTime.UtcNow.AddHours(-3));

            var cleaner = CreateCleaner(db, userRoot, retentionHours: 1);
            var result = await cleaner.CleanupAsync();

            Assert.False(Directory.Exists(oldDir));
            Assert.Equal(1, result.DeletedTaskFolders);
            Assert.Equal(1, result.DeletedFiles);
            Assert.Equal(5, result.FreedBytes);
            Assert.Equal(0, result.ErrorCount);
        }

        // ============ 2. Свежая папка НЕ удаляется ============

        [Fact]
        public async Task CleanupAsync_FreshFolder_Kept()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            var freshDir = CreateTaskFolder(1, "vt_fresh", DateTime.UtcNow.AddMinutes(-10));

            var cleaner = CreateCleaner(db, userRoot, retentionHours: 1);
            var result = await cleaner.CleanupAsync();

            Assert.True(Directory.Exists(freshDir));
            Assert.Equal(0, result.DeletedTaskFolders);
        }

        // ============ 3. Граница: 2 часа при retention=1 ============

        [Fact]
        public async Task CleanupAsync_BoundaryTwoHours_Deleted()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            var dir = CreateTaskFolder(1, "vt_2h", DateTime.UtcNow.AddHours(-2));

            var cleaner = CreateCleaner(db, userRoot, retentionHours: 1);
            var result = await cleaner.CleanupAsync();

            Assert.False(Directory.Exists(dir));
            Assert.Equal(1, result.DeletedTaskFolders);
        }

        // ============ 4. Несколько папок у одного юзера ============

        [Fact]
        public async Task CleanupAsync_MultipleFolders_MixedAges()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            var old1 = CreateTaskFolder(1, "vt_old1", DateTime.UtcNow.AddHours(-5));
            var old2 = CreateTaskFolder(1, "vt_old2", DateTime.UtcNow.AddHours(-2));
            var fresh = CreateTaskFolder(1, "vt_fresh", DateTime.UtcNow.AddMinutes(-5));

            var cleaner = CreateCleaner(db, userRoot, retentionHours: 1);
            var result = await cleaner.CleanupAsync();

            Assert.False(Directory.Exists(old1));
            Assert.False(Directory.Exists(old2));
            Assert.True(Directory.Exists(fresh));
            Assert.Equal(2, result.DeletedTaskFolders);
        }

        // ============ 5. Несколько пользователей ============

        [Fact]
        public async Task CleanupAsync_MultipleUsers_EachHandled()
        {
            using var db = CreateInMemoryDb(1, 2, 3);

            var user1Root = Path.Combine(_tempRoot, "user1");
            var user2Root = Path.Combine(_tempRoot, "user2");
            var user3Root = Path.Combine(_tempRoot, "user3");

            var d1 = CreateTaskFolder(1, "vt_a", DateTime.UtcNow.AddHours(-3));
            var d2 = CreateTaskFolder(2, "vt_b", DateTime.UtcNow.AddHours(-3));
            var d3 = CreateTaskFolder(3, "vt_c", DateTime.UtcNow.AddMinutes(-10));

            // Резолвер должен возвращать разные пути.
            var resolverMock = new Mock<IWorkspaceResolver>();
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(1)).ReturnsAsync(user1Root);
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(2)).ReturnsAsync(user2Root);
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(3)).ReturnsAsync(user3Root);

            var options = new VisionAgentOptions
            {
                Privacy = new VisionPrivacyOptions
                {
                    SaveToWorkspace = true,
                    WorkspaceRetentionHours = 1
                }
            };

            var cleaner = new VisionScreenshotCleaner(
                db, resolverMock.Object, Options.Create(options),
                NullLogger<VisionScreenshotCleaner>.Instance);

            var result = await cleaner.CleanupAsync();

            Assert.False(Directory.Exists(d1));
            Assert.False(Directory.Exists(d2));
            Assert.True(Directory.Exists(d3));
            Assert.Equal(2, result.DeletedTaskFolders);
        }

        // ============ 6. SaveToWorkspace=false → no-op ============

        [Fact]
        public async Task CleanupAsync_SaveDisabled_ReturnsEmpty()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            var oldDir = CreateTaskFolder(1, "vt_old", DateTime.UtcNow.AddHours(-5));

            var cleaner = CreateCleaner(db, userRoot, retentionHours: 1, saveToWorkspace: false);
            var result = await cleaner.CleanupAsync();

            Assert.True(Directory.Exists(oldDir));
            Assert.Equal(0, result.DeletedTaskFolders);
        }

        // ============ 7. Папки нет — не падаем ============

        [Fact]
        public async Task CleanupAsync_NoScreenshotsFolder_NoError()
        {
            using var db = CreateInMemoryDb(1);
            var userRoot = Path.Combine(_tempRoot, "user1");
            Directory.CreateDirectory(userRoot);   // но без screenshots/

            var cleaner = CreateCleaner(db, userRoot);
            var result = await cleaner.CleanupAsync();

            Assert.Equal(0, result.DeletedTaskFolders);
            Assert.Equal(0, result.ErrorCount);
        }

        // ============ 8. Нет пользователей — no-op ============

        [Fact]
        public async Task CleanupAsync_NoUsers_NoError()
        {
            using var db = CreateInMemoryDb();   // пусто
            var cleaner = CreateCleaner(db, _tempRoot);
            var result = await cleaner.CleanupAsync();

            Assert.Equal(0, result.DeletedTaskFolders);
            Assert.Equal(0, result.ErrorCount);
        }
    }
}