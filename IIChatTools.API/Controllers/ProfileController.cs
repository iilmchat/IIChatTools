using System;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Контроллер страницы «Профиль» и API для управления
    /// своими настройками (v1.4.x, KI-067-3).
    ///
    /// Позволяет текущему пользователю настроить срок хранения
    /// своих чатов (override глобального <c>Chat:Retention:DefaultDays</c>).
    /// </summary>
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IUserSettingsService _userSettingsService;
        private readonly IConfiguration _configuration;
        private readonly IStringLocalizer<SharedResources> _localizer;
        private readonly ILogger<ProfileController> _logger;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public ProfileController(
            IUserSettingsService userSettingsService,
            IConfiguration configuration,
            IStringLocalizer<SharedResources> localizer,
            ILogger<ProfileController> logger)
        {
            _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Страница профиля пользователя (GET <c>/profile</c>).
        /// </summary>
        [HttpGet("/profile")]
        public IActionResult Index()
        {
            ViewData["Title"] = _localizer["ProfileTitle"];
            return View("~/Views/Profile/Index.cshtml");
        }

        /// <summary>
        /// Возвращает per-user настройки текущего пользователя.
        /// </summary>
        [HttpGet("/api/profile/settings")]
        public async Task<IActionResult> GetSettingsAsync()
        {
            try
            {
                var userId = GetCurrentUserId();
                var dto = await BuildUserSettingsDtoAsync(userId);
                return Ok(new { success = true, data = dto });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения настроек профиля");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Обновляет per-user настройки текущего пользователя.
        /// </summary>
        [HttpPut("/api/profile/settings")]
        public async Task<IActionResult> UpdateSettingsAsync([FromBody] UserSettingsDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });

                var userId = GetCurrentUserId();
                var maxDays = _configuration.GetValue<int>("Chat:Retention:MaxDays", 365);

                // --- RetentionDays ---
                if (dto.RetentionDays.HasValue)
                {
                    if (dto.RetentionDays.Value <= 0 || dto.RetentionDays.Value > maxDays)
                    {
                        return Ok(new
                        {
                            success = false,
                            message = $"Срок хранения должен быть в диапазоне 1–{maxDays}."
                        });
                    }

                    await _userSettingsService.SetAsync(
                        userId, "Chat.RetentionDays", dto.RetentionDays.Value.ToString(), "int");
                }
                else
                {
                    await _userSettingsService.DeleteAsync(userId, "Chat.RetentionDays");
                }

                // --- DoNotDelete ---
                if (dto.DoNotDelete)
                {
                    await _userSettingsService.SetAsync(
                        userId, "Chat.DoNotDelete", "true", "bool");
                }
                else
                {
                    await _userSettingsService.DeleteAsync(userId, "Chat.DoNotDelete");
                }

                var updated = await BuildUserSettingsDtoAsync(userId);
                return Ok(new { success = true, data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления настроек профиля");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Обновляет выбранный пользователем микрофон для голосового ввода
        /// (v1.13.1-fix8, KI-145).
        /// </summary>
        /// <param name="request">ID устройства (<c>null</c> или пусто = системный default)</param>
        /// <remarks>
        /// Отдельный endpoint от <see cref="UpdateSettingsAsync"/> —
        /// чтобы PUT не задевал retention-настройки (у <c>AudioInputDeviceId</c>
        /// семантика <c>null</c> другая: «сбросить на default», а не «не трогать»).
        /// </remarks>
        [HttpPut("/api/profile/audio-device")]
        public async Task<IActionResult> UpdateAudioDeviceAsync([FromBody] AudioDeviceUpdateRequest request)
        {
            try
            {
                if (request == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });

                var userId = GetCurrentUserId();
                var deviceId = request.AudioInputDeviceId?.Trim();

                if (string.IsNullOrEmpty(deviceId))
                {
                    // Сброс на системный default — удаляем настройку.
                    await _userSettingsService.DeleteAsync(userId, "Audio.InputDeviceId");
                }
                else
                {
                    if (deviceId.Length > 200)
                        return Ok(new { success = false, message = "AudioInputDeviceId слишком длинный." });

                    await _userSettingsService.SetAsync(userId, "Audio.InputDeviceId", deviceId, "string");
                }

                _logger.LogInformation(
                    "Профиль: пользователь {UserId} обновил микрофон (deviceId={HasDevice})",
                    userId, string.IsNullOrEmpty(deviceId) ? "default" : "custom");

                return Ok(new { success = true, data = new { audioInputDeviceId = deviceId } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения выбранного микрофона");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Собирает <see cref="UserSettingsDto"/> из per-user настроек.
        /// </summary>
        private async Task<UserSettingsDto> BuildUserSettingsDtoAsync(int userId)
        {
            var settings = await _userSettingsService.GetAllForUserAsync(userId);

            int? retentionDays = null;
            var doNotDelete = false;
            string audioInputDeviceId = null;

            foreach (var s in settings)
            {
                if (string.Equals(s.Key, "Chat.RetentionDays", StringComparison.Ordinal)
                    && int.TryParse(s.Value, out var days) && days > 0)
                {
                    retentionDays = days;
                }
                else if (string.Equals(s.Key, "Chat.DoNotDelete", StringComparison.Ordinal)
                         && bool.TryParse(s.Value, out var dn) && dn)
                {
                    doNotDelete = true;
                }
                else if (string.Equals(s.Key, "Audio.InputDeviceId", StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(s.Value))
                {
                    // v1.13.1-fix8 (KI-145): выбранный пользователем микрофон.
                    audioInputDeviceId = s.Value;
                }
            }

            return new UserSettingsDto
            {
                RetentionDays = retentionDays,
                DoNotDelete = doNotDelete,
                GlobalRetentionDays = _configuration.GetValue<int>("Chat:Retention:DefaultDays", 30),
                MaxRetentionDays = _configuration.GetValue<int>("Chat:Retention:MaxDays", 365),
                AudioInputDeviceId = audioInputDeviceId
            };
        }

        /// <summary>
        /// Извлекает идентификатор текущего пользователя из claims.
        /// </summary>
        private int GetCurrentUserId()
        {
            var idRaw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idRaw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}