using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Implementation.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionScreenshotStore"/>
    /// (v1.12.0, KI-131, Ф6.5). Используют temp-папку как workspace.
    /// </summary>
    public class VisionScreenshotStoreTests : IDisposable
    {
        private readonly string _tempRoot;

        public VisionScreenshotStoreTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "vision-store-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
        }

        private VisionScreenshotStore CreateStore(
            bool saveToWorkspace = true,
            string workspaceRoot = null)
        {
            var options = new VisionAgentOptions
            {
                Privacy = new VisionPrivacyOptions
                {
                    SaveToWorkspace = saveToWorkspace
                }
            };

            var resolverMock = new Mock<IWorkspaceResolver>();
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(It.IsAny<int>()))
                .ReturnsAsync(workspaceRoot ?? _tempRoot);

            return new VisionScreenshotStore(
                resolverMock.Object,
                Options.Create(options),
                NullLogger<VisionScreenshotStore>.Instance);
        }

        private static byte[] FakePng() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        // ============ 1. SaveAsync — happy path ============

        [Fact]
        public async Task SaveAsync_ValidData_WritesFile()
        {
            var store = CreateStore();
            var png = FakePng();

            var relativePath = await store.SaveAsync(
                1, "vt_test01", 1, png, CancellationToken.None);

            Assert.NotNull(relativePath);
            Assert.Equal("screenshots/vt_test01/step-001.png", relativePath);

            var absolutePath = Path.Combine(_tempRoot,
                "screenshots", "vt_test01", "step-001.png");
            Assert.True(File.Exists(absolutePath));
            Assert.Equal(png, await File.ReadAllBytesAsync(absolutePath));
        }

        // ============ 2. SaveToWorkspace = false → null ============

        [Fact]
        public async Task SaveAsync_SaveDisabled_ReturnsNull()
        {
            var store = CreateStore(saveToWorkspace: false);

            var path = await store.SaveAsync(
                1, "vt_x", 1, FakePng(), CancellationToken.None);

            Assert.Null(path);
            Assert.False(Directory.Exists(Path.Combine(_tempRoot, "screenshots")));
        }

        // ============ 3. Некорректный userId → null ============

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task SaveAsync_InvalidUserId_ReturnsNull(int userId)
        {
            var store = CreateStore();
            var path = await store.SaveAsync(
                userId, "vt_x", 1, FakePng(), CancellationToken.None);

            Assert.Null(path);
        }

        // ============ 4. Пустой PNG → null ============

        [Fact]
        public async Task SaveAsync_EmptyPng_ReturnsNull()
        {
            var store = CreateStore();
            var path = await store.SaveAsync(
                1, "vt_x", 1, Array.Empty<byte>(), CancellationToken.None);

            Assert.Null(path);
        }

        // ============ 5. taskId нормализуется ============

        [Fact]
        public async Task SaveAsync_DangerousTaskId_Sanitized()
        {
            var store = CreateStore();
            var png = FakePng();

            // "../etc/passwd" — опасный taskId. NormalizeTaskId через regex
            // [^a-zA-Z0-9_-] заменяет '.' и '/' на '_' → безопасный "___etc_passwd".
            // ВАЖНО: не проверяем File.Exists("/etc/passwd") — на Linux этот файл
            // существует всегда как системный (cross-platform ловушка).
            var path = await store.SaveAsync(
                1, "../etc/passwd", 1, png, CancellationToken.None);

            // Проверки только по формату относительного пути (без OS-специфики).
            Assert.NotNull(path);
            Assert.DoesNotContain("..", path);
            Assert.DoesNotContain("/etc/", path);
            Assert.StartsWith("screenshots/", path);
            Assert.EndsWith("/step-001.png", path);

            // Файл создан ВНУТРИ workspace (temp), а не вне.
            var absolutePath = Path.Combine(_tempRoot,
                path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absolutePath),
                $"Ожидался файл внутри workspace: {absolutePath}");
        }

        [Fact]
        public async Task SaveAsync_SpaceAndSpecialsInTaskId_Normalized()
        {
            var store = CreateStore();
            var path = await store.SaveAsync(
                1, "vt test@#$%", 2, FakePng(), CancellationToken.None);

            Assert.NotNull(path);
            Assert.StartsWith("screenshots/vt_test", path);
            Assert.EndsWith("/step-002.png", path);
        }

        // ============ 6. stepIndex нормализуется (0 / отрицательный) ============

        [Fact]
        public async Task SaveAsync_ZeroStepIndex_Becomes1()
        {
            var store = CreateStore();
            var path = await store.SaveAsync(
                1, "vt_x", 0, FakePng(), CancellationToken.None);

            Assert.NotNull(path);
            Assert.EndsWith("/step-001.png", path);
        }

        // ============ 7. GetTaskDirectoryAsync ============

        [Fact]
        public async Task GetTaskDirectory_AfterSave_ReturnsPath()
        {
            var store = CreateStore();
            await store.SaveAsync(1, "vt_dir", 1, FakePng(), CancellationToken.None);

            var dir = await store.GetTaskDirectoryAsync(1, "vt_dir");
            Assert.NotNull(dir);
            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public async Task GetTaskDirectory_BeforeSave_ReturnsNull()
        {
            var store = CreateStore();
            var dir = await store.GetTaskDirectoryAsync(1, "vt_nope");
            Assert.Null(dir);
        }

        // ============ 8. WorkspaceRoot = null → null ============

        [Fact]
        public async Task SaveAsync_NullWorkspaceRoot_ReturnsNull()
        {
            var options = new VisionAgentOptions
            {
                Privacy = new VisionPrivacyOptions { SaveToWorkspace = true }
            };

            var resolverMock = new Mock<IWorkspaceResolver>();
            resolverMock
                .Setup(r => r.GetWorkspacePathAsync(It.IsAny<int>()))
                .ReturnsAsync((string)null);

            var store = new VisionScreenshotStore(
                resolverMock.Object,
                Options.Create(options),
                NullLogger<VisionScreenshotStore>.Instance);

            var path = await store.SaveAsync(
                1, "vt_x", 1, FakePng(), CancellationToken.None);

            Assert.Null(path);
        }
    }
}