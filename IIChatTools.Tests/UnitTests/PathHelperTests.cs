using System.IO;
using IIChatTools.Services.Implementation;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты безопасности PathHelper (защита от path traversal).
    /// </summary>
    public class PathHelperTests
    {
        /// <summary>
        /// Проверяет, что относительный путь внутри workspace разрешён.
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_InsideWorkspace_ReturnsTrue()
        {
            var root = Path.Combine(Path.GetTempPath(), "ws_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var ok = PathHelper.TryGetSafeFullPath("subdir/file.txt", root, out var safePath);
                Assert.True(ok);
                Assert.StartsWith(root, safePath);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что path traversal блокируется.
        /// </summary>
        [Theory]
        [InlineData("../etc/passwd")]
        [InlineData("..\\Windows\\System32")]
        [InlineData("subdir/../../escape.txt")]
        public void TryGetSafeFullPath_PathTraversal_ReturnsFalse(string malicious)
        {
            var root = Path.Combine(Path.GetTempPath(), "ws_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var ok = PathHelper.TryGetSafeFullPath(malicious, root, out _);
                Assert.False(ok);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что пустой путь возвращает корень workspace.
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_EmptyPath_ReturnsRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ws_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var ok = PathHelper.TryGetSafeFullPath("", root, out var safePath);
                Assert.True(ok);
                Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    safePath.TrimEnd(Path.DirectorySeparatorChar));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }
}