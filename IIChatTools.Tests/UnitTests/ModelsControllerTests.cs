using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Controllers;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Unit-тесты <see cref="ModelsController"/> (KI-057): фильтрация моделей
    /// по настраиваемым шаблонам <c>LmStudio:ExcludeModelPatterns</c>.
    /// </summary>
    public class ModelsControllerTests
    {
        private const string DefaultModel = "qwen/qwen3-4b-2507";

        /// <summary>
        /// Создаёт контроллер с заданными шаблонами исключения и списком моделей.
        /// </summary>
        private static ModelsController CreateController(
            IReadOnlyList<string> modelIds,
            string[] excludePatterns = null,
            bool throwOnGetModels = false)
        {
            var lmMock = new Mock<ILmStudioClient>();
            if (throwOnGetModels)
            {
                lmMock
                    .Setup(x => x.GetModelIdsAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("LM Studio недоступен"));
            }
            else
            {
                lmMock
                    .Setup(x => x.GetModelIdsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(modelIds);
            }

            var settings = new Dictionary<string, string>
            {
                ["LmStudio:Model"] = DefaultModel,
                ["ExternalLlm:Providers:deepseek:DisplayName"] = "DeepSeek",
                ["ExternalLlm:Providers:deepseek:Model"] = "deepseek-chat"
            };
            if (excludePatterns != null)
            {
                for (var i = 0; i < excludePatterns.Length; i++)
                    settings[$"LmStudio:ExcludeModelPatterns:{i}"] = excludePatterns[i];
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new ModelsController(
                lmMock.Object,
                config,
                NullLogger<ModelsController>.Instance);
        }

        /// <summary>
        /// Достаёт список моделей из результата контроллера.
        /// </summary>
        private static List<string> GetIds(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JObject.FromObject(ok.Value);
            var data = (JArray)json["data"];
            // JObject.FromObject использует дефолтный контракт PascalCase
            // (RULES § 4.43) — читаем "Id", а не camelCase из HTTP-ответа.
            return data.Select(x => x["Id"]!.Value<string>()).ToList();
        }

        [Fact]
        public async Task GetModelsAsync_WithoutConfig_FiltersEmbedByDefault()
        {
            var controller = CreateController(new[]
            {
                "text-embedding-nomic-embed-text-v1.5",
                "qwen2.5-3b-instruct"
            });

            var ids = GetIds(await controller.GetModelsAsync(CancellationToken.None));

            Assert.Equal(new[] { DefaultModel, "qwen2.5-3b-instruct" }, ids);
        }

        [Fact]
        public async Task GetModelsAsync_WithCustomPatterns_FiltersAllMatches()
        {
            var controller = CreateController(
                new[] { "gemma-embedding", "llama-instruct", "whisper-large" },
                new[] { "embed", "whisper" });

            var ids = GetIds(await controller.GetModelsAsync(CancellationToken.None));

            Assert.Equal(new[] { DefaultModel, "llama-instruct" }, ids);
        }

        [Fact]
        public async Task GetModelsAsync_DefaultModelAlwaysFirst()
        {
            var controller = CreateController(new[] { "z-model", "a-model" });

            var ids = GetIds(await controller.GetModelsAsync(CancellationToken.None));

            Assert.Equal(DefaultModel, ids[0]);
            Assert.Equal(3, ids.Count);
        }

        [Fact]
        public async Task GetModelsAsync_LmStudioUnavailable_ReturnsDefaultOnly()
        {
            var controller = CreateController(
                Array.Empty<string>(),
                throwOnGetModels: true);

            var result = await controller.GetModelsAsync(CancellationToken.None);
            var ids = GetIds(result);

            Assert.Equal(new[] { DefaultModel }, ids);

            // KI-226: провайдеры доступны даже при недоступном LM Studio.
            var ok = Assert.IsType<OkObjectResult>(result);
            var providers = (JArray)JObject.FromObject(ok.Value)["providers"];
            Assert.Single(providers);
            Assert.Equal("deepseek", providers[0]!["name"]!.Value<string>());
            Assert.Equal("DeepSeek", providers[0]!["displayName"]!.Value<string>());
        }

        [Fact]
        public async Task GetModelsAsync_IncludesExternalProviders()
        {
            var controller = CreateController(new[] { "qwen2.5-3b-instruct" });

            var result = await controller.GetModelsAsync(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(result);
            var providers = (JArray)JObject.FromObject(ok.Value)["providers"];

            Assert.NotNull(providers);
            Assert.Equal("deepseek-chat", providers[0]!["model"]!.Value<string>());
        }
    }
}
