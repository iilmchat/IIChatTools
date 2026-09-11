using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Utils
{
    /// <summary>
    /// Инструмент: сохранение значения в долговременную память пользователя.
    /// </summary>
    public class SaveMemoryTool : ITool
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<SaveMemoryTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="logger">Логгер</param>
        public SaveMemoryTool(AppDbContext dbContext, ILogger<SaveMemoryTool> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "save_memory";

        /// <inheritdoc />
        public string Description => "Сохраняет значение в долговременную память пользователя (ключ-значение).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "key", Type = "string", Description = "Ключ (максимум 200 символов).", Required = true },
            new ToolParameterDescriptor { Name = "value", Type = "string", Description = "Значение (строка или JSON).", Required = true },
            new ToolParameterDescriptor { Name = "type", Type = "string", Description = "Тип данных (string, json). По умолчанию string.", Required = false, Default = "string" }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var key = arguments.GetString("key");
            var value = arguments.GetString("value");
            var type = arguments.GetString("type", "string");

            if (string.IsNullOrWhiteSpace(key))
                return ToolResult.Fail("Не указан ключ");
            if (key.Length > 200)
                return ToolResult.Fail("Ключ превышает 200 символов");
            if (value == null)
                return ToolResult.Fail("Не указано значение");
            if (value.Length > 100_000)
                return ToolResult.Fail("Значение превышает 100 000 символов");

            try
            {
                var existing = await _dbContext.MemoryEntries
                    .FirstOrDefaultAsync(m => m.UserId == context.UserId && m.Key == key);

                if (existing != null)
                {
                    existing.Value = value;
                    existing.Type = type ?? "string";
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.MemoryEntries.Add(new MemoryEntry
                    {
                        UserId = context.UserId,
                        Key = key,
                        Value = value,
                        Type = type ?? "string",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await _dbContext.SaveChangesAsync();

                return ToolResult.Ok(new { saved = true, key, type });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения памяти для пользователя {UserId}", context.UserId);
                return ToolResult.Fail($"Ошибка сохранения: {ex.Message}");
            }
        }
    }
}