using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.ChatTools;      // ← ДОБАВИТЬ
using IIChatTools.Services.Implementation.Rag;            // v1.5.0 (KI-083): EmbeddingService
using IIChatTools.Services.Implementation.Tools.Browser;
using IIChatTools.Services.Implementation.Tools.CodeExecution;
using IIChatTools.Services.Implementation.Tools.FileSystem;
using IIChatTools.Services.Implementation.Tools.Git;
using IIChatTools.Services.Implementation.Tools.GitHub;
using IIChatTools.Services.Implementation.Tools.SubAgent;
using IIChatTools.Services.Implementation.Tools.Utils;
using IIChatTools.Services.Implementation.Tools.Web;
using IIChatTools.API.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;              // ← добавить
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using IIChatTools.API.HealthChecks;
using IIChatTools.API.RateLimiting;
using IIChatTools.API.BackgroundServices;   // вместо IIChatTools.API.Metrics
using IIChatTools.Services.Implementation.Agents;   // SubAgentRegistry
using Prometheus;

namespace IIChatTools.API
{
    /// <summary>
    /// Конфигурация приложения: сервисы (DI) и middleware-конвейер.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Создаёт экземпляр конфигурации.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <exception cref="ArgumentNullException">Если configuration равен null</exception>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Конфигурация приложения.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Регистрация всех сервисов приложения в контейнере DI.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        /// <exception cref="ArgumentNullException">Если services равен null</exception>
        public void ConfigureServices(IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // ============ 1. База данных ============
            // Провайдер и строка подключения выбираются в DbContextOptionsExtensions
            // на основе ключа Database:Provider (SqlServer / Sqlite / InMemory).
            services.AddAppDbContext(Configuration);

            // ============ 1.1. Health checks ============
            // /health/live  — процесс жив (без проверок)
            // /health/ready — критические зависимости (БД + Workspace)
            // /health       — полный отчёт (включая LM Studio)
            services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "db", "ready" })
                .AddCheck<WorkspaceHealthCheck>("workspace", tags: new[] { "workspace", "ready" })
                .AddCheck<LmStudioHealthCheck>("lmstudio", tags: new[] { "external", "lmstudio" });

            // Named HttpClient для health-check LM Studio (таймаут из конфигурации).
            // Наследует HttpClientFactoryOptions (прокси из KI-002).
            services.AddHttpClient("LmStudioHealth")
                .ConfigureHttpClient(c => c.Timeout = System.TimeSpan.FromSeconds(
                    Configuration.GetValue<int>("HealthChecks:LmStudio:TimeoutSeconds", 3)));


            // ============ 1.2. Rate limiting ============
            // Реализация через собственный middleware (см. RateLimitingMiddleware).
            // Регистрация только конфигурации; сам middleware подключается в Configure.
            services.Configure<RateLimitingOptions>(Configuration.GetSection("RateLimiting"));

            // ============ 1.3. Metrics (Prometheus) ============
            // Фоновый сервис обновляет gauge PendingApprovals/ActiveUsers.
            services.AddHostedService<MetricsRefreshBackgroundService>();

            // ============ 1.4. Audit retention ============
            // BackgroundService: чистка AuditLogs и JSONL-файлов по retention policy.
            services.AddHostedService<AuditRetentionService>();

            // ============ 1.5. Chat retention (v1.3 Фаза 2.2.5) ============
            // BackgroundService: удаление чатов старше Chat:Retention:DefaultDays.
            services.AddHostedService<ChatRetentionService>();

            // ============ 2. Identity ============
            services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

            // ============ 3. Аутентификация (JWT + Cookie) ============
            // .NET Core 3.1: глобально отключаем маппинг длинных URI-имён claim'ов
            // (role, name, nameidentifier) в короткие (role, unique_name, nameid).
            System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler
                .DefaultInboundClaimTypeMap.Clear();

            // ВАЖНО: AddIdentity (выше) внутри себя вызывает AddAuthentication с
            // DefaultAuthenticateScheme = Identity.Application и DefaultChallengeScheme = Identity.Application.
            // Если эти поля не переопределить, authorize-middleware при проверке
            // [Authorize(Policy=...)] будет аутентифицировать cookie, а не Bearer → 401.
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = "SmartScheme";
                options.DefaultAuthenticateScheme = "SmartScheme";
                options.DefaultChallengeScheme = "SmartScheme";
            })
            .AddPolicyScheme("SmartScheme", "Bearer or Cookie", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authorization = context.Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authorization) &&
                        authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        return "JwtBearer";
                    }
                    return Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
                };
            })
            .AddJwtBearer("JwtBearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = Configuration["Jwt:Issuer"],
                    ValidAudience = Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(Configuration["Jwt:Key"]))
                };

                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync(
                            "{\"success\":false,\"message\":\"Unauthorized\"}");
                    }
                };
            });
            
            // ============ 4. MVC + локализация ============
            
            //services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddLocalization(options => options.ResourcesPath = "");
            services.AddControllersWithViews()
                .AddNewtonsoftJson(options =>
                {
                    // KI-071: Sqlite + EF Core возвращают DateTime с Kind=Unspecified.
                    // По умолчанию Newtonsoft.Json сериализует такие даты без суффикса Z,
                    // и JS new Date() парсит их как local → расхождение с реальным UTC
                    // на величину смещения (в Москве UTC+3 → «3 ч назад» для свежего чата).
                    // DateTimeZoneHandling.Utc трактует Unspecified как UTC и добавляет Z.
                    // Все даты в проекте сохраняются через DateTime.UtcNow — это безопасно.
                    options.SerializerSettings.DateTimeZoneHandling =
                        Newtonsoft.Json.DateTimeZoneHandling.Utc;
                })
                .AddViewLocalization()
                .AddDataAnnotationsLocalization();

            /*
            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { "en", "ru" };
                options.SetDefaultCulture("en")
                       .AddSupportedCultures(supportedCultures)
                       .AddSupportedUICultures(supportedCultures);
            });
            */
            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { "en", "ru" };

                options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en", "en");
                options.SupportedCultures = supportedCultures
                    .Select(c => new System.Globalization.CultureInfo(c)).ToList();
                options.SupportedUICultures = options.SupportedCultures;

                options.RequestCultureProviders.Clear();
                options.RequestCultureProviders.Add(new Microsoft.AspNetCore.Localization.QueryStringRequestCultureProvider());
                options.RequestCultureProviders.Add(new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());
                options.RequestCultureProviders.Add(new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider());
            });
            
            // ============ 5. HttpClient с настройкой прокси ============
            services.AddHttpClient();

            // Настраиваем прокси для всех клиентов, создаваемых IHttpClientFactory.
            // В .NET Core 3.1 ConfigurePrimaryHttpMessageHandler недоступен через
            // AddHttpClient() без имени, поэтому используем HttpClientFactoryOptions.
            services.Configure<Microsoft.Extensions.Http.HttpClientFactoryOptions>(options =>
            {
                options.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    var handler = new System.Net.Http.HttpClientHandler
                    {
                        UseProxy = true,
                        AllowAutoRedirect = true,
                        AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                            | System.Net.DecompressionMethods.Deflate
                    };

                    // Явно передаём прокси с credentials — иначе IHttpClientFactory
                    // не отправит Basic Auth на корпоративный прокси (407).
                    var proxyUrl = Configuration["Browser:ProxyServer"];
                    var proxyUser = Configuration["Browser:ProxyUsername"];
                    var proxyPass = Configuration["Browser:ProxyPassword"];

                    // Проверяем, что это РЕАЛЬНЫЙ прокси URL, а не placeholder
                    // «CHANGE_ME_VIA_USER_SECRETS» из appsettings (KI-059).
                    if (IsRealProxyUrl(proxyUrl))
                    {
                        var webProxy = new System.Net.WebProxy(proxyUrl)
                        {
                            BypassProxyOnLocal = true,
                            BypassList = new[]
                            {
                                @"localhost",
                                @"127\.0\.0\.1",
                                @"::1",
                                @".*\.local"
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(proxyUser))
                        {
                            webProxy.Credentials = new System.Net.NetworkCredential(proxyUser, proxyPass);
                        }

                        handler.Proxy = webProxy;
                    }

                    builder.PrimaryHandler = handler;
                });
            });

            // ============ 6. Инфраструктурные сервисы ============
            services.AddSingleton<AppUptimeTracker>();
            services.AddScoped<IDependencyChecker, DependencyChecker>();

            services.AddSingleton<IFileAuditService, FileAuditService>();
            services.AddScoped<IAuditService, CompositeAuditService>();

            services.AddScoped<IAuditQueryService, AuditQueryService>();
            services.AddScoped<IApprovalService, ApprovalService>();
            // v1.4.x (KI-076): статистика запусков суб-агентов.
            services.AddScoped<IAgentStatsService, AgentStatsService>();
            services.AddSingleton<AppUptimeTracker>();
            services.AddScoped<IDependencyChecker, DependencyChecker>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IAuditQueryService, AuditQueryService>();
            services.AddScoped<IApprovalService, ApprovalService>();
            services.AddScoped<IJwtService, JwtService>();
            services.AddScoped<IStatusService, StatusService>();
            services.AddScoped<IWorkspaceResolver, WorkspaceResolver>();
            services.AddScoped<IAppSettingsService, AppSettingsService>();
            services.AddScoped<IUserAdminService, UserAdminService>();
            // v1.4.x (KI-067): per-user настройки (override глобальных).
            services.AddScoped<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<IProcessRunner, ProcessRunner>();

            // ============ Chat service (v1.3) ============
            services.AddScoped<IChatService, ChatService>();

            // ============ SubAgent registry (v1.4 Фаза 1, KI-052) ============
            // Singleton: реестр читается из appsettings один раз. В Фазе 6 — Update/Reset
            // будут ходить в AppSettings (нужна потокобезопасность).
            services.AddSingleton<ISubAgentRegistry, SubAgentRegistry>();

            // ============ Token counter (v1.4.x, KI-049) ============
            // Singleton: токенизатор тяжело инициализируется, потокобезопасен.
            services.AddSingleton<ITokenCounter, TokenCounter>();

            // ============ Chat title service (v1.3 Фаза 2.2.1) ============
            // AI-генерация короткого названия из первого сообщения (ChatGPT-style).
            services.AddScoped<IChatTitleService, ChatTitleService>();

            // ============ Chat stream service (v1.3 Фаза 1.5) ============
            services.AddScoped<IChatStreamService, ChatStreamService>();            

            // ============ Chat approval coordinator (v1.3 Фаза 1.7) ============
            // Singleton — связывает SSE-стрим и REST-endpoint в разных HTTP-scope.
            services.AddSingleton<IChatApprovalCoordinator, ChatApprovalCoordinator>();

            // ============ 7. Реестр инструментов ============
            services.AddScoped<IToolRegistry, ToolRegistry>();

            // ============ 8. Клиент LM Studio и суб-агент ============
            // v1.5.0 (KI-083, Фаза 1): ILmStudioClient переведён в Singleton —
            // для совместимости с EmbeddingService (Singleton). LmStudioClient
            // не держит состояния, использует IHttpClientFactory (Singleton-совместим).
            services.AddSingleton<ILmStudioClient, LmStudioClient>();
            services.AddScoped<ISubAgentService, SubAgentService>();

            // v1.5.0 (KI-083, Фаза 1): сервис эмбеддингов для RAG (Singleton).
            services.AddSingleton<IEmbeddingService, EmbeddingService>();

            // v1.5.0 (KI-083, Шаг 2C): векторное хранилище для RAG (Singleton).
            // InMemoryVectorStore реализует IDisposable — хост освободит
            // ReaderWriterLockSlim каждого индекса при shutdown.
            services.AddSingleton<IVectorStore, InMemoryVectorStore>();

            // v1.5.0 (KI-083, Шаг 3C): стратегии чанкинга + resolver.
            // Все три — stateless, регистрируются как Singleton через интерфейс.
            // Резолвер собирает их в словарь Name → Strategy.
            // ВАЖНО: не инжектить IChunkingStrategy напрямую — DI вернёт
            // последнюю зарегистрированную (Fixed). Для выбора — IChunkingStrategyResolver.
            services.AddSingleton<IChunkingStrategy, RecursiveChunkingStrategy>();
            services.AddSingleton<IChunkingStrategy, SentenceChunkingStrategy>();
            services.AddSingleton<IChunkingStrategy, FixedChunkingStrategy>();
            services.AddSingleton<IChunkingStrategyResolver, ChunkingStrategyResolver>();

            // Фабрика для разрыва DI-цикла: ConsultSecondaryAgentTool → ISubAgentService → IToolRegistry
            services.AddScoped<Func<ISubAgentService>>(sp => () => sp.GetRequiredService<ISubAgentService>());


            // ============ 9. Менеджер браузерных сессий (singleton) ============
            services.AddSingleton<IBrowserSessionManager, BrowserSessionManager>();

            // ============ 10. Регистрация инструментов ============
            RegisterFileSystemTools(services);
            RegisterCodeExecutionTools(services);
            RegisterWebTools(services);
            RegisterGitTools(services);
            RegisterGitHubTools(services);
            RegisterBrowserTools(services);
            RegisterUtilityTools(services);
            RegisterSubAgentTools(services);

            // v1.4.0 Фаза 3 (KI-052): 6 специализированных агентов
            RegisterSpecializedAgentTools(services);

            // ============ 11. Политики авторизации ============
            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            });
        }

        /// <summary>
        /// Конфигурация middleware-конвейера.
        /// </summary>
        /// <param name="app">Построитель приложения</param>
        /// <param name="env">Окружение хостинга</param>
        /// <exception cref="ArgumentNullException">Если app или env равны null</exception>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (env == null) throw new ArgumentNullException(nameof(env));

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error");
                app.UseHsts();
            }

            var locOptions = app.ApplicationServices
                .GetRequiredService<IOptions<RequestLocalizationOptions>>()
                .Value;
            app.UseRequestLocalization(locOptions);

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // Prometheus HTTP-метрики — как можно раньше, чтобы захватить все запросы,
            // включая те, что отклонены rate-limiting (429).
            app.UseHttpMetrics();

            // Rate limiting — собственный middleware (SDK 10.0.401 не даёт AddRateLimiter).
            // Идёт ПОСЛЕ UseAuthentication, чтобы видеть User (claims), и ДО UseEndpoints.
            // Health-эндпоинты исключены внутри middleware по префиксу /health/*.
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<RateLimitingMiddleware>();

            app.UseEndpoints(endpoints =>
            {
                // Default route — все контроллеры, БЕЗ явной политики по умолчанию
                // (политики применяются через атрибуты [EnableRateLimiting] на контроллерах).
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                // Prometheus /metrics — публичный, без авторизации (для скрейпера).
                endpoints.MapMetrics("/metrics");                    

                // Health checks — анонимные, БЕЗ rate limiting.
                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = _ => false,
                    ResponseWriter = HealthCheckResponseWriter.WriteAsync
                });
                //.DisableRateLimiting();

                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("ready"),
                    ResponseWriter = HealthCheckResponseWriter.WriteAsync
                });
                //.DisableRateLimiting();


                endpoints.MapHealthChecks("/health", new HealthCheckOptions
                {
                    ResponseWriter = HealthCheckResponseWriter.WriteAsync
                });
                //.DisableRateLimiting();
            });
        }

        // ============ Группы инструментов ============

        /// <summary>
        /// Регистрирует инструменты файловой системы.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterFileSystemTools(IServiceCollection services)
        {
            services.AddScoped<ITool, ListDirectoryTool>();
            services.AddScoped<ITool, ChangeDirectoryTool>();
            services.AddScoped<ITool, MakeDirectoryTool>();
            services.AddScoped<ITool, ReadFileTool>();
            services.AddScoped<ITool, SaveFileTool>();
            services.AddScoped<ITool, DeletePathTool>();
            services.AddScoped<ITool, ReplaceTextInFileTool>();
            services.AddScoped<ITool, DeleteFilesByPatternTool>();
            services.AddScoped<ITool, MoveFileTool>();
            services.AddScoped<ITool, CopyFileTool>();
            services.AddScoped<ITool, FindFilesTool>();
            services.AddScoped<ITool, GetFileMetadataTool>();
            services.AddScoped<ITool, FuzzyFindLocalFilesTool>();
        }

        /// <summary>
        /// Регистрирует инструменты выполнения кода.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterCodeExecutionTools(IServiceCollection services)
        {
            services.AddScoped<ITool, RunJavaScriptTool>();
            services.AddScoped<ITool, RunPythonTool>();
            services.AddScoped<ITool, ExecuteCommandTool>();
        }

        /// <summary>
        /// Регистрирует веб-инструменты.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterWebTools(IServiceCollection services)
        {
            services.AddScoped<ITool, WebSearchTool>();
            services.AddScoped<ITool, WikipediaSearchTool>();
            services.AddScoped<ITool, FetchWebContentTool>();
        }

        /// <summary>
        /// Регистрирует Git-инструменты.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterGitTools(IServiceCollection services)
        {
            services.AddScoped<ITool, GitStatusTool>();
            services.AddScoped<ITool, GitDiffTool>();
            services.AddScoped<ITool, GitLogTool>();
            services.AddScoped<ITool, GitAddTool>();
            services.AddScoped<ITool, GitCommitTool>();
            services.AddScoped<ITool, GitCheckoutTool>();
            services.AddScoped<ITool, GitPushTool>();
        }

        /// <summary>
        /// Регистрирует GitHub-инструменты.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterGitHubTools(IServiceCollection services)
        {
            services.AddScoped<ITool, GhAuthStatusTool>();
            services.AddScoped<ITool, GhCreateIssueTool>();
            services.AddScoped<ITool, GhListIssuesTool>();
            services.AddScoped<ITool, GhViewCommentsTool>();
            services.AddScoped<ITool, GhCreatePrTool>();
            services.AddScoped<ITool, GhListPrsTool>();
            services.AddScoped<ITool, GhViewPrDiffTool>();
        }

        /// <summary>
        /// Регистрирует браузерные инструменты.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterBrowserTools(IServiceCollection services)
        {
            services.AddScoped<ITool, BrowserSessionOpenTool>();
            services.AddScoped<ITool, BrowserSessionControlTool>();
            services.AddScoped<ITool, BrowserSessionCloseTool>();
            services.AddScoped<ITool, BrowserOpenPageTool>();
        }

        /// <summary>
        /// Регистрирует утилитарные инструменты.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterUtilityTools(IServiceCollection services)
        {
            services.AddScoped<ITool, GetSystemInfoTool>();
            services.AddScoped<ITool, SaveMemoryTool>();
        }

        /// <summary>
        /// Регистрирует инструмент делегирования суб-агенту.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterSubAgentTools(IServiceCollection services)
        {
            services.AddScoped<ITool, ConsultSecondaryAgentTool>();
        }

        /// <summary>
        /// Регистрирует инструменты-обёртки вокруг специализированных суб-агентов
        /// (v1.4.0 Фаза 3, KI-052). Всего 6 агентов.
        ///
        /// <para>
        /// В Chat они пока не видны (переключение — Фаза 5). Сейчас доступны только
        /// через прямые вызовы /api/tools/execute (для smoke-тестов).
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterSpecializedAgentTools(IServiceCollection services)
        {
            services.AddScoped<ITool, FileSystemAgentTool>();
            services.AddScoped<ITool, CodeAgentTool>();
            services.AddScoped<ITool, WebAgentTool>();
            services.AddScoped<ITool, GitAgentTool>();
            services.AddScoped<ITool, GitHubAgentTool>();
            services.AddScoped<ITool, PlannerAgentTool>();
        }

        /// <summary>
        /// Проверяет, что строка является РЕАЛЬНЫМ URL прокси, а не placeholder.
        /// Placeholder-значения (<c>CHANGE_ME_VIA_USER_SECRETS</c>, пустые строки)
        /// игнорируются — иначе <see cref="System.Net.WebProxy"/> пытается
        /// установить CONNECT-туннель к несуществующему хосту (KI-059).
        /// </summary>
        /// <param name="url">Значение из конфигурации</param>
        /// <returns>true, если URL валиден и не является placeholder</returns>
        private static bool IsRealProxyUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // Placeholder из appsettings.json (см. README → User Secrets).
            if (url.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Должен быть абсолютный URL со схемой http/https.
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}