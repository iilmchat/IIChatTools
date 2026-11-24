using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// D15 / KI-240: Admin-only экспорт оценок 👍/👎 в JSONL для датасета.
    /// Строка = один JSON-объект { messageId, chatId, feedback, content }.
    /// PII не маскируется (данные остаются внутри административного контура).
    /// </summary>
    [ApiController]
    [Route("api/admin/feedback")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminFeedbackController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AdminFeedbackController(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Экспортирует все сообщения с непустой оценкой в формате JSONL.
        /// </summary>
        [HttpGet("export")]
        public async Task<IActionResult> ExportJsonlAsync(CancellationToken cancellationToken)
        {
            var rows = await _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.Feedback != null && m.Feedback != string.Empty)
                .OrderBy(m => m.Id)
                .Select(m => new
                {
                    messageId = m.Id,
                    chatId = m.ChatId,
                    feedback = m.Feedback,
                    role = m.Role,
                    content = m.Content
                })
                .ToListAsync(cancellationToken);

            var sb = new StringBuilder();
            foreach (var row in rows)
                sb.AppendLine(JsonConvert.SerializeObject(row));

            return Content(sb.ToString(), "application/x-ndjson", Encoding.UTF8);
        }
    }
}
