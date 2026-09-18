using System.IO;
using IIChatTools.Services.Implementation;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты безопасности PathHelper (защита от path traversal).
    /// Проверяют кроссплатформенность: оба разделителя ('/', '\') должны
    /// блокироваться одинаково на Windows и Linux (KI-040).
    /// </summary>
    public class PathHelperTests
    {
        /// <summary>
        /// Создаёт временный каталог для теста и возвращает путь.
        /// </summary>
        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ws_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// Проверяет, что относительный путь внутри workspace разрешён.
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_InsideWorkspace_ReturnsTrue()
        {
            var root = CreateTempRoot();
            try
            {
                var ok = PathHelper.TryGetSafeFullPath("subdir/file.txt", root, out var safePath);
                Assert.True(ok);
                Assert.StartsWith(root, safePath);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что относительный путь с обратными слэшами внутри workspace разрешён
        /// (нормализуется к разделителю текущей ОС).
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_InsideWorkspace_BackslashNormalized_ReturnsTrue()
        {
            var root = CreateTempRoot();
            try
            {
                var ok = PathHelper.TryGetSafeFullPath("subdir\\file.txt", root, out var safePath);
                Assert.True(ok);
                Assert.StartsWith(root, safePath);
                // Обратный слэш должен быть нормализован к DirectorySeparatorChar
                Assert.Contains(Path.DirectorySeparatorChar, safePath.Substring(root.Length));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что path traversal блокируется на всех платформах,
        /// включая обход через backslash на Linux (KI-040).
        /// </summary>
        [Theory]
        [InlineData("../etc/passwd")]
        [InlineData("..\\Windows\\System32")]
        [InlineData("..\\..\\etc\\passwd")]
        [InlineData("subdir/../../escape.txt")]
        [InlineData("subdir\\..\\..\\escape.txt")]
        [InlineData("subdir/..\\../escape.txt")]
        public void TryGetSafeFullPath_PathTraversal_ReturnsFalse(string malicious)
        {
            var root = CreateTempRoot();
            try
            {
                var ok = PathHelper.TryGetSafeFullPath(malicious, root, out _);
                Assert.False(ok, $"Путь '{malicious}' должен быть отвергнут как path traversal");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что пустой путь возвращает корень workspace.
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_EmptyPath_ReturnsRoot()
        {
            var root = CreateTempRoot();
            try
            {
                var ok = PathHelper.TryGetSafeFullPath("", root, out var safePath);
                Assert.True(ok);
                Assert.Equal(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                    Path.TrimEndingDirectorySeparator(safePath));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Проверяет, что путь, начинающийся как root, но содержащий продолжение
        /// с другим суффиксом (например, "workspace-evil"), не проходит проверку.
        /// Классический обход через префиксное совпадение строк.
        /// </summary>
        [Fact]
        public void TryGetSafeFullPath_PrefixSiblingDirectory_ReturnsFalse()
        {
            var root = CreateTempRoot();
            var sibling = root + "-evil";
            Directory.CreateDirectory(sibling);
            try
            {
                // Абсолютный путь к соседней папке не должен попасть внутрь workspace
                var ok = PathHelper.TryGetSafeFullPath(sibling, root, out _);
                Assert.False(ok, "Путь к соседней папке с общим префиксом должен быть отвергнут");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(sibling, recursive: true);
            }
        }
    }
}