using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Mail;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис вложений Mail Agent (v1.8.0, KI-107).
    ///
    /// <para>
    /// Работает только с workspace пользователя — не с произвольной ФС.
    /// При чтении — сохраняет вложения в <c>mail-attachments/{uid}/</c>.
    /// При отправке — валидирует относительные пути в workspace.
    /// </para>
    /// </summary>
    public interface IMailAttachmentService
    {
        /// <summary>
        /// Сохранить входящее вложение в workspace пользователя.
        /// </summary>
        Task<MailAttachmentDto> SaveIncomingAsync(
            int userId, uint uid,
            string originalFileName, byte[] content, string contentType,
            CancellationToken ct = default);

        /// <summary>
        /// Проверить относительные пути вложений и вернуть абсолютные
        /// (защита от path-traversal, лимиты размеров).
        /// </summary>
        Task<IReadOnlyList<string>> ResolveForSendAsync(
            int userId, IReadOnlyList<string> relativePaths,
            CancellationToken ct = default);
    }
}