using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// MVC-контроллер страницы чата (UI).
    /// Отдаёт Razor-представление <c>Views/Chat/Index.cshtml</c>.
    ///
    /// API чатов — в <see cref="ChatController"/> (<c>/api/chats</c>).
    /// SSE-стриминг — в <see cref="ChatStreamController"/> (<c>/api/chat/stream</c>).
    /// </summary>
    [Authorize]
    [Route("chat")]
    public class ChatViewController : Controller
    {
        /// <summary>
        /// Отображает страницу чата (GET <c>/chat</c>).
        /// </summary>
        /// <returns>Razor-представление <c>Views/Chat/Index.cshtml</c></returns>
        [HttpGet("")]
        public IActionResult Index()
        {
            return View("~/Views/Chat/Index.cshtml");
        }
    }
}