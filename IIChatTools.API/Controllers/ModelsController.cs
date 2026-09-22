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

                // Добавляем остальные модели из LM Studio,
                // исключая дубль default и embedding-модели
                // (embedding-модели не поддерживают /v1/chat/completions).
                // TODO v1.3.x: config-driven exclusion patterns (KI-057).
                foreach (var id in ids.Where(x =>
                    !string.Equals(x, defaultModel, StringComparison.Ordinal) &&
                    !IsEmbeddingModel(x)))
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

                return Ok(new { success = true, data = list });
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
                    message = "LM Studio недоступен, показана только модель по умолчанию."
                });
            }
        }

        /// <summary>
        /// Проверяет, является ли модель embedding-моделью (не поддерживает chat completion).
        /// Эвристика по имени: содержит "embed" (например, <c>text-embedding-nomic-embed-text-v1.5</c>).
        /// </summary>
        /// <param name="modelId">Идентификатор модели</param>
        /// <returns>true, если модель является embedding-моделью</returns>
        private static bool IsEmbeddingModel(string modelId)
        {
            return !string.IsNullOrEmpty(modelId)
                && modelId.Contains("embed", StringComparison.OrdinalIgnoreCase);
        }        
    }
}