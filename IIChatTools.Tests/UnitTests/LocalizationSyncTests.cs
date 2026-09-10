using System.Collections;
using System.Globalization;
using System.Resources;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты синхронизации ключей файлов локализации.
    /// Проверяет, что наборы ключей в SharedResources.resx (английский по умолчанию)
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
            var assembly = typeof(IIChatTools.API.Resources.SharedResources).Assembly;

            // Английский (нейтральный) — базовый ресурс
            var neutralManager = new ResourceManager(
                "IIChatTools.API.Resources.SharedResources", assembly);
            var neutralSet = neutralManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);

            // Русский — целевая культура
            var ruManager = new ResourceManager(
                "IIChatTools.API.Resources.SharedResources", assembly);
            var ruSet = ruManager.GetResourceSet(new CultureInfo("ru"), true, true);

            Assert.NotNull(neutralSet);
            Assert.NotNull(ruSet);

            // Act
            var neutralKeys = GetKeys(neutralSet);
            var ruKeys = GetKeys(ruSet);

            // Assert — отсутствующие в русском
            var missingInRu = neutralKeys.Except(ruKeys).OrderBy(k => k).ToList();
            Assert.True(missingInRu.Count == 0,
                $"Ключи, отсутствующие в SharedResources.ru.resx: {string.Join(", ", missingInRu)}");

            // Assert — лишние в русском (не должны существовать в базовом)
            var extraInRu = ruKeys.Except(neutralKeys).OrderBy(k => k).ToList();
            Assert.True(extraInRu.Count == 0,
                $"Ключи, отсутствующие в SharedResources.resx: {string.Join(", ", extraInRu)}");
        }

        /// <summary>
        /// Извлекает все ключи из набора ресурсов.
        /// </summary>
        /// <param name="resourceSet">Набор ресурсов ResourceSet</param>
        /// <returns>Коллекция строковых ключей</returns>
        private static HashSet<string> GetKeys(ResourceSet resourceSet)
        {
            var keys = new HashSet<string>();
            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is string key)
                    keys.Add(key);
            }
            return keys;
        }
    }
}