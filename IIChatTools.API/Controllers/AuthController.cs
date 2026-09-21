using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.API.ViewModels;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using IIChatTools.API.Resources;
using Microsoft.Extensions.Logging;


namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Контроллер аутентификации: регистрация, вход, выход, выдача JWT.
    /// Первый зарегистрированный пользователь получает роль Admin.
    /// </summary>
    [Route("auth")]
    public class AuthController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IJwtService _jwtService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AuthController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IJwtService jwtService,
            IAuditService auditService,
            ILogger<AuthController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Отображает страницу входа.
        /// </summary>
        /// <param name="returnUrl">URL возврата после входа</param>
        /// <param name="error">Маркер ошибки (например, <c>ratelimit</c> — сработал rate limit)</param>
        /// <param name="retryAfter">Через сколько секунд можно повторить (при <c>error=ratelimit</c>)</param>
        /// <returns>Razor-представление</returns>
        [HttpGet("login")]
        public IActionResult Login(string returnUrl = null, string error = null, int? retryAfter = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            // KI-050: маркер "сработал rate limit" — приходит из RateLimitingMiddleware
            if (!string.IsNullOrEmpty(error) && error.Equals("ratelimit", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["RateLimitError"] = true;
                ViewData["RetryAfter"] = retryAfter ?? 60;
            }

            return View(new LoginViewModel());
        }

        /// <summary>
        /// Обрабатывает POST-запрос на вход.
        /// </summary>
        /// <param name="model">Данные входа</param>
        /// <param name="returnUrl">URL возврата</param>
        /// <returns>Редирект или представление с ошибками</returns>
        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginAsync([FromForm] LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null || !user.IsActive)
                {
                    ModelState.AddModelError(string.Empty, _localizer["Неверное имя пользователя или пароль."]);
                    return View(model);
                }

                var result = await _signInManager.PasswordSignInAsync(
                    user, model.Password, model.RememberMe, lockoutOnFailure: false);

                if (!result.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, _localizer["Неверное имя пользователя или пароль."]);
                    return View(model);
                }

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = user.Id,
                    ToolName = "auth.login",
                    Status = "Success",
                    DurationMs = 0,
                    ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

                _logger.LogInformation("Пользователь {Email} вошёл в систему", model.Email);

                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при входе пользователя {Email}", model.Email);
                ModelState.AddModelError(string.Empty, _localizer["Внутренняя ошибка сервера."]);
                return View(model);
            }
        }

        /// <summary>
        /// Отображает страницу регистрации.
        /// </summary>
        /// <param name="error">Маркер ошибки (например, <c>ratelimit</c> — сработал rate limit)</param>
        /// <param name="retryAfter">Через сколько секунд можно повторить (при <c>error=ratelimit</c>)</param>
        /// <returns>Razor-представление</returns>
        [HttpGet("register")]
        public IActionResult Register(string error = null, int? retryAfter = null)
        {
            // KI-050: маркер "сработал rate limit"
            if (!string.IsNullOrEmpty(error) && error.Equals("ratelimit", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["RateLimitError"] = true;
                ViewData["RetryAfter"] = retryAfter ?? 60;
            }

            return View(new RegisterViewModel());
        }

        /// <summary>
        /// Обрабатывает POST-запрос регистрации.
        /// Первый зарегистрированный пользователь получает роль Admin.
        /// </summary>
        /// <param name="model">Данные регистрации</param>
        /// <returns>Редирект при успехе или представление с ошибками</returns>
        [HttpPost("register")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterAsync([FromForm] RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var existing = await _userManager.FindByEmailAsync(model.Email);
                if (existing != null)
                {
                    ModelState.AddModelError(nameof(model.Email), _localizer["Пользователь с таким email уже существует."]);
                    return View(model);
                }

                // Определяем, является ли это первым пользователем в системе
                var isFirstUser = !_userManager.Users.Any();

                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = true,
                    FullName = model.FullName,
                    IsActive = true,
                    RegisteredAt = DateTime.UtcNow
                };

                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    foreach (var error in createResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
                    return View(model);
                }

                var role = isFirstUser ? SeedData.RoleAdmin : SeedData.RoleUser;
                await _userManager.AddToRoleAsync(user, role);

                await _auditService.LogActionAsync(new AuditLog
                {
                    UserId = user.Id,
                    ToolName = "auth.register",
                    ParametersJson = $"{{\"role\":\"{role}\"}}",
                    Status = "Success",
                    DurationMs = 0,
                    ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

                _logger.LogInformation(
                    "Зарегистрирован новый пользователь {Email} с ролью {Role}",
                    model.Email, role);

                await _signInManager.SignInAsync(user, isPersistent: false);

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка регистрации пользователя {Email}", model.Email);
                ModelState.AddModelError(string.Empty, _localizer["Внутренняя ошибка сервера."]);
                return View(model);
            }
        }

        /// <summary>
        /// Выполняет выход из системы.
        /// </summary>
        /// <returns>Редирект на главную</returns>
        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutAsync()
        {
            try
            {
                var userIdRaw = _userManager.GetUserId(User);
                if (int.TryParse(userIdRaw, out var userId))
                {
                    await _auditService.LogActionAsync(new AuditLog
                    {
                        UserId = userId,
                        ToolName = "auth.logout",
                        Status = "Success",
                        DurationMs = 0
                    });
                }

                await _signInManager.SignOutAsync();
                _logger.LogInformation("Пользователь вышел из системы");
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выходе из системы");
                return RedirectToAction("Index", "Home");
            }
        }

        /// <summary>
        /// Выдаёт JWT-токен для API-клиента.
        /// </summary>
        /// <param name="model">Данные входа</param>
        /// <returns>JSON с токеном или ошибкой</returns>
        [HttpPost("token")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TokenAsync([FromBody] LoginViewModel model)
        {
            try
            {
                if (model == null)
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value  });

                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null || !user.IsActive)
                    return Ok(new { success = false, message = _localizer["Неверное имя пользователя или пароль."].Value  });

                var passwordValid = await _userManager.CheckPasswordAsync(user, model.Password);
                if (!passwordValid)
                    return Ok(new { success = false, message = _localizer["Неверное имя пользователя или пароль."].Value  });

                var roles = await _userManager.GetRolesAsync(user);
                var token = await _jwtService.GenerateTokenAsync(user, roles);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        token,
                        userId = user.Id,
                        email = user.Email,
                        roles
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выдачи JWT-токена");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value  });
            }
        }
    }
}