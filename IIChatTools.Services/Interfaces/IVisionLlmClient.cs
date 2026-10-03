using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Клиент Vision LLM — модели, которая описывает UI со скриншота
    /// и возвращает структурированный <see cref="ScreenDescriptionDto"/>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.2, § 4.4.
    /// </remarks>
    public interface IVisionLlmClient
    {
        /// <summary>
        /// Отправляет PNG скриншота в Vision LLM, возвращает описание UI
        /// (с текстовым summary + списком <c>ui_elements[]</c>).
        /// </summary>
        /// <param name="screenshotPng">PNG-байты скриншота.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Описание экрана. Fallback: <c>Description</c> = текстовая часть ответа, <c>UiElements</c> = пустой список.</returns>
        Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Готов ли клиент к работе (например, LM Studio доступен и модель загружена).
        /// </summary>
        bool IsReady { get; }
    }
}