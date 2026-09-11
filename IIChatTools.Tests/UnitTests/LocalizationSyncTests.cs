using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using IIChatTools.API.Resources;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты синхронизации ключей файлов локализации.
    /// Проверяет, что наборы ключей в SharedResources.resx (английский, базовый)
    /// и SharedResources.ru.resx (русский) полностью совпадают.
    /// </summary>
    public class LocalizationSyncTests
    {
        /// <summary>
        /// Проверяет, что ключи английского и русского файлов ресурсов совпадают.
        /// Тест падает, если какой-либо ключ присутствует только в одном из файлов.
        /// </summary>
        [Fact]
        public void ResourceKeys_ShouldBeSynchronized()
        {
            // Arrange
            var assembly = typeof(SharedResources).Assembly;

            const string baseName = "IIChatTools.API.Resources.SharedResources";

            var neutralManager = new ResourceManager(baseName, assembly);
            var neutralSet = neutralManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);

            var ruManager = new ResourceManager(baseName, assembly);
            var ruSet = ruManager.GetResourceSet(new CultureInfo("ru"), true, true);

            Assert.NotNull(neutralSet);
            Assert.NotNull(ruSet);

            // Act
            var neutralKeys = GetKeys(neutralSet);
            var ruKeys = GetKeys(ruSet);

            // Assert — ключи, присутствующие в базовом файле, но отсутствующие в русском
            var missingInRu = neutralKeys
                .Except(ruKeys)
                .OrderBy(k => k)
                .ToList();

            Assert.True(
                missingInRu.Count == 0,
                $"Ключи, отсутствующие в SharedResources.ru.resx: {string.Join(", ", missingInRu)}");

            // Assert — лишние ключи в русском (их не должно быть в базовом)
            var extraInRu = ruKeys
                .Except(neutralKeys)
                .OrderBy(k => k)
                .ToList();

            Assert.True(
                extraInRu.Count == 0,
                $"Ключи, отсутствующие в SharedResources.resx: {string.Join(", ", extraInRu)}");
        }

        /// <summary>
        /// Извлекает все строковые ключи из набора ресурсов.
        /// </summary>
        /// <param name="resourceSet">Набор ресурсов ResourceSet</param>
        /// <returns>Множество строковых ключей</returns>
        private static HashSet<string> GetKeys(ResourceSet resourceSet)
        {
            var keys = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is string key)
                    keys.Add(key);
            }

            return keys;
        }
    }
}