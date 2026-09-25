using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API администрирования RAG Knowledge Base
    /// (v1.5.0, KI-083, Шаг 7A).
    ///
    /// <para>
    /// Доступен только роли Admin. Отдаёт список индексов, позволяет
    /// переиндексировать <c>project_docs</c>, просматривать / удалять чанки,
    /// редактировать настройки RAG.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/admin/knowledge")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminKnowledgeController : ControllerBase
    {
        private readonly IAdminKnowledgeService _service;
        private readonly ILogger<AdminKnowledgeController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="service">Сервис администрирования Knowledge Base</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public AdminKnowledgeController(
            IAdminKnowledgeService service,
            ILogger<AdminKnowledgeController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============================================================
        // GET /api/admin/knowledge/indexes
        // ============================================================

        /// <summary>
        /// Возвращает список всех 4 индексов с метриками.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: RagIndexDto[] }</returns>
        [HttpGet("indexes")]
        public async Task<IActionResult> GetIndexesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var list = await _service.GetIndexesAsync(cancellationToken);
                return Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения индексов RAG");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // POST /api/admin/knowledge/indexes/project-docs/reindex
        // ============================================================

        /// <summary>
        /// Переиндексирует индекс <c>project_docs</c> из путей
        /// <c>Rag:Ingestion:ProjectDocsPaths</c>.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: IngestionResultDto }</returns>
        [HttpPost("indexes/project-docs/reindex")]
        public async Task<IActionResult> ReindexProjectDocsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _service.ReindexProjectDocsAsync(cancellationToken);
                return Ok(new { success = true, data = result });
            }
            catch (OperationCanceledException)
            {
                return Ok(new { success = false, message = "Операция отменена." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка переиндексации project_docs");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        // ============================================================
        // GET /api/admin/knowledge/chunks?index=&page=&pageSize=
        // ============================================================

        /// <summary>
        /// Возвращает постраничный список чанков индекса.
        /// </summary>
        /// <param name="index">Имя индекса (default: project_docs)</param>
        /// <param name="page">Номер страницы (1-based, default 1)</param>
        /// <param name="pageSize">Размер страницы (default 20, max 100)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: { items, page, pageSize, totalCount, totalPages } }</returns>
        [HttpGet("chunks")]
        public async Task<IActionResult> GetChunksAsync(
            [FromQuery] string index = "project_docs",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (items, p, ps, totalCount, totalPages) =
                    await _service.GetChunksAsync(index, page, pageSize, cancellationToken);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        page = p,
                        pageSize = ps,
                        totalCount,
                        totalPages
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения чанков индекса {Index}", index);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // DELETE /api/admin/knowledge/chunks/{id}
        // ============================================================

        /// <summary>
        /// Удаляет один чанк (из БД + VectorStore).
        /// </summary>
        /// <param name="id">ID чанка</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success } или { success: false, message }</returns>
        [HttpDelete("chunks/{id:int}")]
        public async Task<IActionResult> DeleteChunkAsync(
            int id,
            CancellationToken cancellationToken)
        {
            try
            {
                var removed = await _service.DeleteChunkAsync(id, cancellationToken);
                if (!removed)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка удаления чанка {ChunkId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // GET /api/admin/knowledge/settings
        // ============================================================

        /// <summary>
        /// Возвращает текущие настройки RAG (override'ы с fallback на appsettings).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: RagSettingsDto }</returns>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var dto = await _service.GetSettingsAsync(cancellationToken);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек RAG");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        // ============================================================
        // PUT /api/admin/knowledge/settings
        // ============================================================

        /// <summary>
        /// Сохраняет override'ы настроек RAG в AppSettings.
        /// </summary>
        /// <param name="dto">Новые настройки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: RagSettingsDto }</returns>
        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettingsAsync(
            [FromBody] RagSettingsDto dto,
            CancellationToken cancellationToken)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Некорректные данные запроса."].Value
                    });
                }

                await _service.UpdateSettingsAsync(dto, cancellationToken);

                // Возвращаем актуальные (после валидации и нормализации).
                var updated = await _service.GetSettingsAsync(cancellationToken);
                return Ok(new { success = true, data = updated });
            }
            catch (ArgumentException ex)
            {
                // Валидация — из сервиса.
                return Ok(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения настроек RAG");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }
    }
}