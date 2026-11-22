using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API для получения списка доступных моделей LM Studio.
    /// Используется UI для выбора модели при создании чата (v1.3 Фаза 2.0).
    /// </summary>
    [ApiController]
    [Route("api/models")]
    [Authorize]
    public class ModelsController : ControllerBase
    {
        private readonly ILmStudioClient _lmStudioClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ModelsController> _logger;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="lmStudioClient">Клиент LM Studio</param>
        /// <param name="configuration">Конфигурация (для <c>LmStudio:Model</c>)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ModelsController(
            ILmStudioClient lmStudioClient,
            IConfiguration configuration,
            ILogger<ModelsController> logger)
        {
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Возвращает список моделей LM Studio + модель по умолчанию из конфига.
        /// Если LM Studio недоступен — возвращает только модель по умолчанию
        /// (это позволяет UI работать в offline-режиме).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: ModelInfoDto[] }</returns>
        [HttpGet]
        public async Task<IActionResult> GetModelsAsync(CancellationToken cancellationToken)
        {
            var defaultModel = _configuration["LmStudio:Model"] ?? "local-model";

            try
            {
                var ids = await _lmStudioClient.GetModelIdsAsync(cancellationToken);

                var list = new List<ModelInfoDto>
                {
                    // Default-модель всегда первая в списке
                    new ModelInfoDto
                    {
                        Id = defaultModel,
                        Name = defaultModel,
                        IsDefault = true
                    }
                };

                // Добавляем остальные модели из LM Studio, исключая дубль default
                // и модели, попадающие под настраиваемые шаблоны (KI-057).
                // По умолчанию исключаем embedding-модели ("embed") — они не
                // поддерживают /v1/chat/completions. Шаблоны задаются в
                // LmStudio:ExcludeModelPatterns (config-driven, RULES § 4.56).
                var excludePatterns = GetExcludePatterns();
                foreach (var id in ids.Where(x =>
                    !string.Equals(x, defaultModel, StringComparison.Ordinal) &&
                    !IsExcludedModel(x, excludePatterns)))
                {
                    list.Add(new ModelInfoDto
                    {
                        Id = id,
                        Name = id,
                        IsDefault = false
                    });
                }

                _logger.LogInformation(
                    "Список моделей LM Studio: default={Default}, total={Total}",
                    defaultModel, list.Count);

                // KI-226: добавляем модели внешних провайдеров (группировка
                // по провайдеру). UI может показывать optgroup; значения
                // Chat.Model пока остаются <model> (изменение контракта БД
                // на `<provider>:<model>` — отдельная задача).
                return Ok(new
                {
                    success = true,
                    data = list,
                    providers = GetExternalProviders()
                });
            }
            catch (Exception ex)
            {
                // Fallback: LM Studio недоступен → отдаём только default.
                // Не роняем 500 — UI должен иметь возможность выбрать модель.
                _logger.LogWarning(ex,
                    "Не удалось получить список моделей LM Studio, используется fallback (default={Default})",
                    defaultModel);

                var fallback = new List<ModelInfoDto>
                {
                    new ModelInfoDto
                    {
                        Id = defaultModel,
                        Name = defaultModel,
                        IsDefault = true
                    }
                };

                return Ok(new
                {
                    success = true,
                    data = fallback,
                    providers = GetExternalProviders(),
                    message = "LM Studio недоступен, показана только модель по умолчанию."
                });
            }
        }

        /// <summary>
        /// Возвращает список внешних провайдеров LLM из конфигурации
        /// (<c>ExternalLlm:Providers</c>) для группировки моделей в UI (KI-226).
        /// </summary>
        /// <returns>Массив объектов { name, displayName, model }</returns>
        private object[] GetExternalProviders()
        {
            return _configuration
                .GetSection("ExternalLlm:Providers")
                .GetChildren()
                .Select(p => new
                {
                    name = p.Key,
                    displayName = p["DisplayName"] ?? p.Key,
                    model = p["Model"] ?? string.Empty
                })
                .ToArray();
        }

        /// <summary>
        /// Возвращает шаблоны исключения моделей из конфигурации
        /// (<c>LmStudio:ExcludeModelPatterns</c>). Если ключ не задан —
        /// используется шаблон по умолчанию "embed" (KI-057).
        /// </summary>
        /// <returns>Список подстрок для исключения (не пустой)</returns>
        private string[] GetExcludePatterns()
        {
            var children = _configuration
                .GetSection("LmStudio:ExcludeModelPatterns")
                .GetChildren()
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return children.Length > 0 ? children : new[] { "embed" };
        }

        /// <summary>
        /// Проверяет, попадает ли модель под один из шаблонов исключения.
        /// Сравнение регистронезависимое (RULES § 4.16 — ключи .resx
        /// case-insensitive; здесь та же логика для имён моделей).
        /// </summary>
        /// <param name="modelId">Идентификатор модели</param>
        /// <param name="patterns">Шаблоны подстрок</param>
        /// <returns>true, если модель нужно скрыть из списка</returns>
        private static bool IsExcludedModel(string modelId, string[] patterns)
        {
            if (string.IsNullOrEmpty(modelId))
                return false;

            return patterns.Any(p =>
                modelId.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}