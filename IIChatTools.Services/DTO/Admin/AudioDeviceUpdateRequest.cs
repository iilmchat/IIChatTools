namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Запрос на обновление выбранного пользователем микрофона
    /// (v1.13.1-fix8, KI-145).
    ///
    /// <para>
    /// Отдельный DTO — чтобы PUT /api/profile/audio-device не задевал
    /// retention-поля (<c>Chat.RetentionDays</c> / <c>Chat.DoNotDelete</c>).
    /// Семантика <c>null</c> отличается от <see cref="UserSettingsDto"/>:
    /// здесь <c>null</c>/пустая строка означает «сбросить на системный default»,
    /// а не «не трогать».
    /// </para>
    /// </summary>
    public class AudioDeviceUpdateRequest
    {
        /// <summary>
        /// ID микрофона из <c>MediaDeviceInfo.deviceId</c>.
        /// <c>null</c> или пустая строка — сбросить на системный default.
        /// </summary>
        public string AudioInputDeviceId { get; set; }
    }
}