using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Implementation.ChatTools;      // ← ДОБАВИТЬ
using IIChatTools.Services.Implementation.Debate;         // v1.11.0 (KI-126, Шаг 1B): AgentDebateSessionService
using IIChatTools.Services.Implementation.Rag;            // v1.5.0 (KI-083): EmbeddingService
using IIChatTools.Services.DTO.SqlAgent;                  // v1.7.0 (KI-097): SqlAgentOptions
using IIChatTools.Services.DTO.Mail;                      // v1.8.0 (KI-107): Mail Agent
using IIChatTools.Services.DTO.ExternalLlm;               // v1.8.1 (KI-109): External-LLM
using IIChatTools.Services.DTO.Cache;                     // v1.8.2: ToolResultCacheOptions
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.DTO.Rag;                       // v1.13.x (KI-203): OcrOptions
using IIChatTools.Services.Implementation.ExternalLlm;    // v1.8.1 (KI-109): ExternalLlmClient и т.д.
using IIChatTools.Services.Implementation.Cache;          // v1.8.2: ToolResultCache
using IIChatTools.Services.Implementation.Rag.Parsers;    // v1.5.0 (KI-083): PlainTextParser
using IIChatTools.Services.Implementation.Tools.Browser;
using IIChatTools.Services.Implementation.Tools.Debate;   // v1.11.0 (KI-126, Шаг 1D)
using IIChatTools.Services.Implementation.Tools.CodeExecution;
using IIChatTools.Services.Implementation.Tools.FileSystem;
using IIChatTools.Services.Implementation.Tools.Git;
using IIChatTools.Services.Implementation.Tools.GitHub;
using IIChatTools.Services.Implementation.Speech;
using IIChatTools.Services.Implementation.SqlAgent;       // v1.7.0 (KI-097): SqlAgent
using IIChatTools.Services.Implementation.VisionAgent;    // v1.12.0 (KI-131, Ф2.2): LocalHarnessVisionBackend
using IIChatTools.Services.DTO.VisionAgent;               // v1.12.0 (KI-131): VisionAgentOptions
using IIChatTools.Services.Implementation.Mail;           // v1.8.0 (KI-107): Mail Agent
using IIChatTools.Services.Implementation.Tools.Mail;     // v1.8.0 (KI-107): Mail tools
using IIChatTools.Services.Implementation.Tools.ExternalLlm;  // v1.8.1 (KI-109): External-LLM tools
using IIChatTools.Services.Implementation.Tools.Rag;
using IIChatTools.Services.Implementation.Tools.SqlAgent;   // v1.7.0 (KI-097): DatabaseAgentTool
using IIChatTools.Services.Implementation.Tools.SubAgent;
using IIChatTools.Services.Implementation.Tools.Utils;
using IIChatTools.Services.Implementation.Tools.VisionAgent; // v1.12.0 (KI-131, Ф7): VisionAgentTool
using IIChatTools.Services.Implementation.Tools.Web;
using IIChatTools.API.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;              // ← добавить
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;   // v1.8.2: MemoryCacheOptions
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
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

            // ============ 3.5. Multipart + Kestrel limits (v1.5.0, KI-083, Шаг 6B) ============
            // Поднимаем лимиты для загрузки файлов (multipart) до 40 MB:
            //   32 MB (Rag:Attachments:MaxFileSizeBytes) + запас на multipart-overhead.
            // Kestrel по умолчанию 30 MB, FormOptions — 128 MB.
            // 30 MB < 32 MB, поэтому 32 MB файл обрезался бы до нашей валидации.
            const long MultipartLimitBytes = 41_943_040;   // 40 MB
            services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = MultipartLimitBytes;
            });
            services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = MultipartLimitBytes;
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

            // v1.5.0 (KI-083, Шаг 6A): сервис вложений чата.
            // Scoped — работает с AppDbContext + IDocumentIngestionService.
            services.AddScoped<IChatAttachmentService, ChatAttachmentService>();

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

            // ============ Debate session service (v1.11.0, KI-126, Шаг 1B) ============
            // Scoped — работает с AppDbContext (создание / отмена сессии, чтение статуса).
            // Concurrency-guard (max 3 активных на пользователя) — внутри сервиса.
            services.AddScoped<IAgentDebateSessionService, AgentDebateSessionService>();

            // ============ Chat approval coordinator (v1.3 Фаза 1.7) ============
            // Singleton — связывает SSE-стрим и REST-endpoint в разных HTTP-scope.
            services.AddSingleton<IChatApprovalCoordinator, ChatApprovalCoordinator>();

            // ============ Agent debate coordinator (v1.11.0, KI-126, Шаг 1E) ============
            // Singleton — Human-in-the-loop между раундами Actor-Critic.
            // По образцу ChatApprovalCoordinator (RULES § 4.23).
            services.AddSingleton<IAgentDebateCoordinator, AgentDebateCoordinator>();

            // ============ 7. Реестр инструментов ============
            services.AddScoped<IToolRegistry, ToolRegistry>();

            // ============ 7.1. Tool result cache (v1.8.2) ============
            // Слой 2: кэш результатов инструментов (web_search, wikipedia_search,
            // RAG-поиск). Whitelist — appsettings:ToolCache:Tools.
            // Singleton — обёртка над IMemoryCache. Scoped ToolRegistry
            // зависит от Singleton IToolResultCache (валидно).
            services.Configure<ToolResultCacheOptions>(Configuration.GetSection("ToolCache"));

            // SizeLimit читаем напрямую из IConfiguration (не из IOptions<ToolResultCacheOptions>):
            // AddMemoryCache принимает Action<MemoryCacheOptions>, а не фабрику —
            // IServiceProvider здесь недоступен. Значение клампится как в ToolResultCache.
            var toolCacheSizeLimit = Math.Clamp(
                Configuration.GetValue<int>("ToolCache:SizeLimit", 10_000),
                100, 1_000_000);

            services.AddMemoryCache(options =>
            {
                options.SizeLimit = toolCacheSizeLimit;
            });

            services.AddSingleton<IToolResultCache, ToolResultCache>();

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

            // v1.5.0 (KI-083, Шаг 4B): парсеры документов + реестр.
            // Парсеры — Singleton, stateless. Порядок регистрации = порядок обхода
            // в registry (первый матч по расширению побеждает).
            // v1.7.1 (KI-104): + PdfParser (.pdf), + DocxParser (.docx).
            // Расширения не пересекаются с PlainTextParser — порядок не важен,
            // но для детерминизма: PlainText → Pdf → Docx.
            services.AddSingleton<IRagDocumentParser, PlainTextParser>();
            services.AddSingleton<IRagDocumentParser, PdfParser>();
            services.AddSingleton<IRagDocumentParser, DocxParser>();
            services.AddSingleton<IRagDocumentParserRegistry, RagDocumentParserRegistry>();

            // v1.13.x (KI-203): OCR для сканов PDF.
            // Singleton: один TesseractEngine на всё приложение.
            // Native lib — Windows-only; на Linux IsReady=false (graceful).
            services.Configure<OcrOptions>(Configuration.GetSection("Rag:Ingestion:Ocr"));
            services.AddSingleton<IOcrService, TesseractOcrService>();

            // v1.5.0 (KI-083, Шаг 4C.2): сервис индексации документов.
            // Scoped — работает с AppDbContext (DocumentChunks).
            // Зависимости (парсеры/embedding/vector store) — Singleton, инжектятся.
            services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();

            // v1.13.x (KI-204): трекер прогресса OCR (in-memory).
            // Singleton — общий ConcurrentDictionary + AsyncLocal.
            // Cleanup timer — TTL 5 мин (по образцу KI-043).
            services.AddSingleton<IOcrProgressTracker, OcrProgressTracker>();

            // v1.5.0 (KI-083, Шаг 5A): сервис поиска по векторным индексам.
            // Scoped — читает DocumentChunk из БД для enrichment.
            services.AddScoped<IRetrievalService, RetrievalService>();

            // v1.5.0 (KI-083, Шаг 7A): администрирование RAG Knowledge Base.
            // Scoped — работает с AppDbContext, IDocumentIngestionService, IAppSettingsService.
            services.AddScoped<IAdminKnowledgeService, AdminKnowledgeService>();

            // v1.5.0 (KI-083, Шаг 7C.1): per-user Workspace Index.
            // Singleton — запускает фоновую индексацию через Task.Run + IServiceScopeFactory.
            // Scoped-зависимости (AppDbContext, IUserSettingsService, IDocumentIngestionService,
            // IWorkspaceResolver) резолвятся внутри scope на каждый вызов / фоновый прогон.
            services.AddSingleton<IWorkspaceIndexService, WorkspaceIndexService>();

            // ============ SqlAgent / Database Agent (v1.7.0, KI-097, Фаза 2) ============
            // Baseline-конфиг из appsettings:SqlAgent. Runtime-overrides применяются
            // в Program.LoadSqlAgentOverridesAsync (после старта хоста).
            // SqlConnectionProvider — Singleton: читает актуальные значения
            // из SqlAgentOptionsProvider при каждом вызове (не кэширует).
            services.Configure<SqlAgentOptions>(Configuration.GetSection("SqlAgent"));
            services.AddSingleton<SqlAgentOptionsProvider>();
            services.AddSingleton<ISqlConnectionProvider, SqlConnectionProvider>();
            // v1.7.0 (KI-097, Фаза 3): валидатор SQL — Singleton (stateless).
            services.AddSingleton<ISqlQueryValidator, SqlQueryValidator>();

            // v1.7.0 (KI-097, KI-100): провайдер путей приложения (ContentRootPath).
            // Разрывает зависимость Services → API: SqlConnectionProvider резолвит
            // относительные Sqlite-пути от ContentRootPath, а не от CWD (KI-100).
            services.AddSingleton<IAppPathProvider>(sp =>
                new AppPathProvider(
                    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath));

            // v1.7.0 (KI-097, Фаза 4): оркестратор Database Agent (4 операции).
            services.AddScoped<ISqlAgentService, SqlAgentService>();

            // v1.7.0 (KI-097, Фаза 6A): admin-сервис для управления подключениями
            // (persist override'ов в AppSettings + runtime-применение).
            services.AddScoped<IAdminSqlAgentService, AdminSqlAgentService>();

            // ============ Mail Agent (v1.8.0, KI-107, Фаза 2) ============
            // Baseline-конфиг из appsettings:Mail. Credentials (Username / Password) —
            // только через User Secrets / env (не в appsettings.json).
            // IMailAccountProvider — Singleton (v1.8.0 — Global; v1.8.x — PerUser, KI-108).
            // IMailClient — Singleton (per-call connect → operation → disconnect;
            //   IMAP-пул с TTL отложен — KI-107-more).
            // Tools (send_email и др.) регистрируются в Фазе 3 при Mail:Enabled = true.
            services.Configure<MailOptions>(Configuration.GetSection("Mail"));
            services.Configure<MailRateLimitOptions>(Configuration.GetSection("Mail:RateLimit"));
            services.AddSingleton<IMailAccountProvider, GlobalMailAccountProvider>();
            services.AddSingleton<IMailClient, MailKitClient>();

            // v1.8.0 (KI-107, Фаза 4): rate limiter + attachment service.
            // RateLimiter — Singleton (in-memory state + Timer cleanup по KI-043).
            // AttachmentService — Scoped (зависит от IWorkspaceResolver).
            services.AddSingleton<IMailRateLimiter, InMemoryMailRateLimiter>();
            services.AddScoped<IMailAttachmentService, MailAttachmentService>();

            // ============ Speech Recognition (v1.13.0, KI-140) ============
            // Офлайн-распознавание речи через Whisper.net (whisper.cpp bindings).
            // Singleton — модель (~142 MB) загружается лениво при первом запросе,
            // кэшируется на всё время жизни приложения.
            // Переиспользует IAppPathProvider (v1.7.0, KI-100) для резолва ModelPath.
            // См. DESIGN_SPEECH_RECOGNITION.md § 3.6.
            services.Configure<SpeechOptions>(Configuration.GetSection("Speech"));
            services.AddSingleton<ISpeechRecognitionService, WhisperNetTranscriptionService>();

            // ============ Vision Agent (v1.12.0, KI-131, Ф2.1-Ф2.2) ============
            // Computer-use pattern: VL-модель описывает экран, Planner LLM решает
            // следующее действие, backend выполняет (клик / ввод / скролл).
            // См. DESIGN_VISION_AGENT.md § 4.2.
            //
            // Ф2.2 — только LocalHarnessVisionBackend (скелет).
            // SandboxVisionBackend — Ф3, VncMcpVisionBackend — Ф4.
            // IVisionAgentService — Ф6, VisionAgentTool — Ф7.
            services.Configure<VisionAgentOptions>(Configuration.GetSection("VisionAgent"));
            RegisterVisionAgentTools(services, Configuration);

            // ============ External-LLM Agent (v1.8.1, KI-109, Фаза 2.6) ============
            // Baseline-конфиг из appsettings:ExternalLlm. API-ключи — только через
            // User Secrets / env (по ApiKeySecretName). Fail-fast валидация конфига —
            // внутри ExternalProviderRegistry (при Enabled = true).
            //
            // Все 4 сервиса — Singleton (stateless / per-user state в ConcurrentDictionary
            // с Timer cleanup по образцу KI-043). Tools (3 шт.) регистрируются в Фазе 3
            // (RegisterExternalLlmTools) — только при ExternalLlm:Enabled = true.
            services.Configure<ExternalLlmOptions>(Configuration.GetSection("ExternalLlm"));
            services.AddSingleton<IExternalProviderRegistry, ExternalProviderRegistry>();
            services.AddSingleton<IExternalLlmCircuitBreaker, ExternalLlmCircuitBreaker>();
            services.AddSingleton<IExternalLlmBudgetTracker, ExternalLlmBudgetTracker>();
            services.AddSingleton<IExternalLlmClient, ExternalLlmClient>();

            // v1.8.1 (KI-109, Фаза 3): 3 tool'а для агента external_llm_agent.
            // Регистрируются только при ExternalLlm:Enabled = true — иначе LLM
            // не должен их видеть (DESIGN § 3.1).
            //
            // ВАЖНО: Chat НЕ видит эти tool'ы напрямую. Они доступны только
            // через агента external_llm_agent (Фаза 4) — попадают в его
            // AllowedTools в SubAgents:external_llm_agent. Это НЕ требует правок
            // ChatStreamService (RULES § 4.44 применим только к top-level ITool
            // в Chat, а не к наследникам AgentToolBase).
            if (Configuration.GetValue<bool>("ExternalLlm:Enabled"))
            {
                RegisterExternalLlmTools(services);
            }

            // Фабрики для разрыва DI-циклов (ADR-002):
            // - ConsultSecondaryAgentTool → ISubAgentService → IToolRegistry
            // - CodeAgentWithReviewTool → IToolRegistry → IEnumerable<ITool> → CodeAgentWithReviewTool
            services.AddScoped<Func<ISubAgentService>>(sp => () => sp.GetRequiredService<ISubAgentService>());
            services.AddScoped<Func<IToolRegistry>>(sp => () => sp.GetRequiredService<IToolRegistry>());


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

            // v1.5.0 (KI-083, Шаг 5B): RAG-tools (search_knowledge_base, search_chat_history).
            RegisterRagTools(services);

            // v1.11.0 (KI-126, Шаг 1D): top-level Actor-Critic оркестратор.
            // Не наследник AgentToolBase (DESIGN § 5.1) — по образцу DatabaseAgentTool.
            // Требует явного добавления в allowedNames ChatStreamService (RULES § 4.44).
            services.AddScoped<ITool, CodeAgentWithReviewTool>();

            // v1.7.0 (KI-097, Фаза 5): Database Agent tool.
            // Не регистрируется при SqlAgent:Enabled = false (DESIGN § 3.4).
            RegisterSqlAgentTools(services, Configuration);

            // v1.8.0 (KI-107, Фаза 3A): Mail tools (list_emails, read_email, send_email).
            // Не регистрируются при Mail:Enabled = false (DESIGN_MAIL_AGENT § 3.1).
            RegisterMailTools(services, Configuration);

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

            // v1.11.0 (KI-126, Шаг 1C): агент-критик для Actor-Critic.
            // Не требует approval (read-only: получает код, возвращает JSON).
            // См. DESIGN_MULTI_AGENT_DEBATE.md § 2.2, § 6.
            services.AddScoped<ITool, CodeReviewerAgentTool>();

            // v1.8.0 (KI-107, Фаза 5): почтовый агент (mail_agent).
            // Регистрируется безусловно (как 6 других агентов); видимость в Chat
            // управляется через SubAgents:mail_agent:Enabled.
            // Внутренние mail-tools регистрируются отдельно (RegisterMailTools)
            // только при Mail:Enabled = true.
            services.AddScoped<ITool, MailAgentTool>();

            // v1.8.1 (KI-109, Фаза 4): агент внешних LLM (external_llm_agent).
            // Регистрируется безусловно; видимость в Chat — через
            // SubAgents:external_llm_agent:Enabled.
            // Внутренние tools (ask_external_llm и др.) регистрируются отдельно
            // (RegisterExternalLlmTools) только при ExternalLlm:Enabled = true.
            services.AddScoped<ITool, ExternalLlmAgentTool>();
        }

        /// <summary>
        /// Регистрирует RAG-tools (v1.5.0, KI-083, Шаги 5B + 5C):
        /// <c>search_knowledge_base</c>, <c>search_chat_history</c>,
        /// <c>search_workspace</c>.
        ///
        /// <para>
        /// Все три — read-only (<c>RequiresApprovalByDefault = false</c>).
        /// Зависимости (<see cref="IRetrievalService"/>, <c>IUserSettingsService</c>)
        /// — Scoped, инструменты тоже Scoped.
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterRagTools(IServiceCollection services)
        {
            services.AddScoped<ITool, SearchKnowledgeBaseTool>();
            services.AddScoped<ITool, SearchChatHistoryTool>();
            services.AddScoped<ITool, SearchWorkspaceTool>();
        }

        /// <summary>
        /// Регистрирует Database Agent tool (v1.7.0, KI-097, Фаза 5).
        ///
        /// <para>
        /// Если <c>SqlAgent:Enabled = false</c> — инструмент не регистрируется,
        /// LLM физически его не видит (DESIGN § 3.4). Глобальный флаг
        /// читается из конфигурации на этапе старта приложения.
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        /// <param name="configuration">Конфигурация (для чтения SqlAgent:Enabled)</param>
        private static void RegisterSqlAgentTools(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var enabled = configuration.GetValue<bool>("SqlAgent:Enabled", defaultValue: true);
            if (!enabled)
            {
                return;
            }

            services.AddScoped<ITool, DatabaseAgentTool>();
        }

        /// <summary>
        /// Регистрирует mail-tools (v1.8.0, KI-107, Фаза 3A).
        ///
        /// <para>
        /// Если <c>Mail:Enabled = false</c> — инструменты не регистрируются,
        /// LLM физически их не видит (DESIGN_MAIL_AGENT § 3.1).
        /// </para>
        ///
        /// <para>
        /// Фаза 3A — 3 инструмента: <c>list_emails</c>, <c>read_email</c>,
        /// <c>send_email</c>. Фаза 3B добавит <c>search_emails</c>,
        /// <c>delete_email</c>, <c>move_email</c>, <c>mark_as_read</c>.
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        /// <param name="configuration">Конфигурация (для чтения Mail:Enabled)</param>
        private static void RegisterMailTools(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var enabled = configuration.GetValue<bool>("Mail:Enabled", defaultValue: false);
            if (!enabled)
            {
                return;
            }

            // Фаза 3A (read + mutating):
            services.AddScoped<ITool, ListEmailsTool>();
            services.AddScoped<ITool, ReadEmailTool>();
            services.AddScoped<ITool, SendEmailTool>();

            // Фаза 3B (read + mutating):
            services.AddScoped<ITool, SearchEmailsTool>();
            services.AddScoped<ITool, DeleteEmailTool>();
            services.AddScoped<ITool, MoveEmailTool>();
            services.AddScoped<ITool, MarkAsReadTool>();
        }

        /// <summary>
        /// Регистрирует Vision Agent (v1.12.0, KI-131, Ф2.1-Ф2.2).
        ///
        /// <para>
        /// Если <c>VisionAgent:Enabled = false</c> — backend не регистрируется,
        /// DI не содержит <see cref="IVisionBackend"/>. Chat не видит инструмент
        /// (VisionAgentTool появится в Ф7).
        /// </para>
        ///
        /// <para>
        /// Ф2.2 — только <c>LocalHarnessVisionBackend</c> (скелет, все методы —
        /// <c>NotImplementedException</c>). Sandbox — Ф3, RemoteVnc — Ф4.
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов.</param>
        /// <param name="configuration">Конфигурация (для чтения <c>VisionAgent:Enabled</c> и <c>Backend:Mode</c>).</param>
        private static void RegisterVisionAgentTools(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var enabled = configuration.GetValue<bool>("VisionAgent:Enabled", defaultValue: false);
            if (!enabled)
            {
                return;
            }

            // CA1416: LocalHarnessVisionBackend помечен [SupportedOSPlatform("windows")],
            // потому что использует System.Drawing.Common (Windows-only в рантайме).
            // Проект таргетит plain net10.0 (для совместимости с Linux-сборкой Docker),
            // поэтому анализатор ругается при регистрации в DI. Подавляем локально:
            // на Linux backend фактически не будет создан — RegisterVisionAgentTools
            // вызывается только при VisionAgent:Enabled = true, а на Linux
            // Mode остаётся "local-harness" по дефолту → runtime-ошибка в ScreenshotAsync,
            // а не при сборке.
#pragma warning disable CA1416
            // Все три backend'а регистрируются как Scoped (DESIGN § 4.2).
            // Пока только Local; Sandbox и VncMcp — Ф3, Ф4.
            services.AddScoped<LocalHarnessVisionBackend>();

            // Выбор backend'а по VisionAgent:Backend:Mode.
            services.AddScoped<IVisionBackend>(sp =>
            {
                var mode = configuration["VisionAgent:Backend:Mode"] ?? "local-harness";
                return mode switch
                {
                    // "sandbox"    => sp.GetRequiredService<SandboxVisionBackend>(),
                    // "remote-vnc" => sp.GetRequiredService<VncMcpVisionBackend>(),
                    _ => sp.GetRequiredService<LocalHarnessVisionBackend>()
                };
            });

            // Ф5.1-Ф5.4 (KI-131): конкретные клиенты + Auto* + выбор по Provider.
            // Регистрируем все 3 уровня: LmStudio / External / Auto.
            // Финальный IVisionLlmClient / IPlannerLlmClient резолвится через
            // switch по VisionAgent:{VisionLlm,PlannerLlm}:Provider (Ф5.4).
            services.AddSingleton<LmStudioVisionClient>();
            services.AddSingleton<ExternalVisionClient>();
            services.AddSingleton<AutoVisionClient>(sp => new AutoVisionClient(
                sp.GetRequiredService<IOptions<VisionAgentOptions>>(),
                () => sp.GetRequiredService<LmStudioVisionClient>(),
                () => sp.GetRequiredService<ExternalVisionClient>(),
                sp.GetRequiredService<ILogger<AutoVisionClient>>()));

            services.AddSingleton<LmStudioPlannerClient>();
            services.AddSingleton<ExternalPlannerClient>();
            services.AddSingleton<AutoPlannerClient>(sp => new AutoPlannerClient(
                sp.GetRequiredService<IOptions<VisionAgentOptions>>(),
                () => sp.GetRequiredService<LmStudioPlannerClient>(),
                () => sp.GetRequiredService<ExternalPlannerClient>(),
                sp.GetRequiredService<ILogger<AutoPlannerClient>>()));

            // Выбор по Provider: "lmstudio" (default) / "external" / "auto".
            services.AddSingleton<IVisionLlmClient>(sp =>
            {
                var provider = configuration["VisionAgent:VisionLlm:Provider"] ?? "lmstudio";
                return provider.ToLowerInvariant() switch
                {
                    "external" => sp.GetRequiredService<ExternalVisionClient>(),
                    "auto" => sp.GetRequiredService<AutoVisionClient>(),
                    _ => sp.GetRequiredService<LmStudioVisionClient>()
                };
            });

            services.AddSingleton<IPlannerLlmClient>(sp =>
            {
                var provider = configuration["VisionAgent:PlannerLlm:Provider"] ?? "lmstudio";
                return provider.ToLowerInvariant() switch
                {
                    "external" => sp.GetRequiredService<ExternalPlannerClient>(),
                    "auto" => sp.GetRequiredService<AutoPlannerClient>(),
                    _ => sp.GetRequiredService<LmStudioPlannerClient>()
                };
            });

            // Ф6.1 (KI-131): валидатор действий (blocked keys / hotkeys,
            // clamp text/deltaY, проверка target в ui_elements).
            services.AddSingleton<IVisionActionValidator, VisionActionValidator>();

            // Ф6.4 (KI-131): rate limiter (5 задач / 5 мин per-user).
            // Singleton — in-memory state + Timer cleanup (по образцу KI-043).
            services.AddSingleton<IVisionRateLimiter, InMemoryVisionRateLimiter>();

            // Ф6.5 (KI-131): хранилище скриншотов в workspace пользователя.
            // Scoped — зависит от IWorkspaceResolver (Scoped).
            services.AddScoped<IVisionScreenshotStore, VisionScreenshotStore>();

            // Ф6.6 (KI-131): очистка устаревших скриншотов (TTL 1 ч).
            // Cleaner — Scoped (AppDbContext + IWorkspaceResolver).
            // RetentionService — HostedService (тонкая обёртка по таймеру).
            services.AddScoped<IVisionScreenshotCleaner, VisionScreenshotCleaner>();
            services.AddHostedService<VisionRetentionService>();

            // Ф6.7 (KI-131): overlay launcher (on-screen indicator).
            // Оба класса регистрируются как Singleton (stateless). Runtime-switch:
            //   - WpfVisionOverlayLauncher.IsAvailable = true (Windows + exe найден)
            //     → WPF overlay (реальный on-screen indicator с STOP-кнопкой).
            //   - иначе → Noop (Linux, отсутствует exe — degraded mode).
            //
            // WpfVisionOverlayLauncher сам по себе [SupportedOSPlatform("windows")]? Нет —
            // IsAvailable проверяет OperatingSystem.IsWindows() внутри, поэтому
            // регистрация безопасна и на Linux.
            services.AddSingleton<NoopVisionOverlayLauncher>();
            services.AddSingleton<WpfVisionOverlayLauncher>();
            services.AddSingleton<IVisionOverlayLauncher>(sp =>
            {
                var wpf = sp.GetRequiredService<WpfVisionOverlayLauncher>();
                return wpf.IsAvailable
                    ? (IVisionOverlayLauncher)wpf
                    : sp.GetRequiredService<NoopVisionOverlayLauncher>();
            });

            // KI-161 (v1.13.x): CDP-attach. IChromeCdpSession — Scoped
            // (per-task). Реальный инстанс создаётся внутри
            // LocalHarnessVisionBackend.OpenAsync (через new, т.к.
            // backend'у нужна конкретная CDP-сессия с правильным
            // browserUrl и жизненным циклом задачи). Регистрация в DI —
            // для тестов (мок IChromeCdpSession) и потенциальных
            // альтернативных потребителей.
            services.AddScoped<PuppeteerSharpCdpSession>();
            services.AddScoped<IChromeCdpSession>(sp =>
                sp.GetRequiredService<PuppeteerSharpCdpSession>());

            // KI-161 (v1.13.x): VisionCoordinateProvider — Singleton
            // (stateless, bounds-center из VL-описания, KI-190).
            // DomCoordinateProvider НЕ в DI — создаётся через new
            // внутри LocalHarnessVisionBackend (нужна конкретная
            // CDP-сессия задачи, DESIGN § 2.5).
            services.AddSingleton<VisionCoordinateProvider>();

            // Ф6.2 (KI-131): оркестратор loop'а Vision Agent.
            // Scoped — зависит от Scoped IVisionBackend + IVisionScreenshotStore.
            services.AddScoped<IVisionAgentService, VisionAgentService>();

            // Ф7 (KI-131): top-level ITool vision_agent.
            // НЕ наследник AgentToolBase (DESIGN § 4.1) — собственный loop
            // через IVisionAgentService + прямой доступ к IVisionBackend для
            // одиночных действий. RULES § 4.44: обязательно добавить в
            // allowedNames в ChatStreamService (см. ниже).
            services.AddScoped<ITool, VisionAgentTool>();
#pragma warning restore CA1416
        }

        /// <summary>
        /// Регистрирует External-LLM tools (v1.8.1, KI-109, Фаза 3):
        /// <c>ask_external_llm</c>, <c>list_external_providers</c>,
        /// <c>check_internet_connection</c>.
        ///
        /// <para>
        /// Не регистрируются при <c>ExternalLlm:Enabled = false</c>
        /// (DESIGN_EXTERNAL_LLM § 3.1). Chat их не видит напрямую —
        /// только через агента <c>external_llm_agent</c> (Фаза 4).
        /// </para>
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        private static void RegisterExternalLlmTools(IServiceCollection services)
        {
            services.AddScoped<ITool, AskExternalLlmTool>();
            services.AddScoped<ITool, ListExternalProvidersTool>();
            services.AddScoped<ITool, CheckInternetConnectionTool>();
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
