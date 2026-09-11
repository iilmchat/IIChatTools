using System;
using System.Text;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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

            /*
            // ============ 1. База данных ============
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));
            */

            // ============ 1. База данных ============
            services.AddAppDbContext(Configuration);

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

            // ============ 3. Аутентификация (JWT — именованная схема) ============
            services.AddAuthentication()
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
                });

            // ============ 4. MVC + локализация ============
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddControllersWithViews()
                .AddNewtonsoftJson()
                .AddViewLocalization()
                .AddDataAnnotationsLocalization();

            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { "en", "ru" };
                options.SetDefaultCulture("en")
                       .AddSupportedCultures(supportedCultures)
                       .AddSupportedUICultures(supportedCultures);
            });

            // ============ 5. HttpClient ============
            services.AddHttpClient();

            // ============ 6. Инфраструктурные сервисы ============
            services.AddSingleton<AppUptimeTracker>();
            services.AddScoped<IDependencyChecker, DependencyChecker>();

            services.AddSingleton<IFileAuditService, FileAuditService>();
            services.AddScoped<IAuditService, CompositeAuditService>();

            services.AddScoped<IAuditQueryService, AuditQueryService>();
            services.AddScoped<IApprovalService, ApprovalService>();
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
            services.AddSingleton<IProcessRunner, ProcessRunner>();

            // ============ 7. Реестр инструментов ============
            services.AddScoped<IToolRegistry, ToolRegistry>();

            /*
            // ============ 8. Клиент LM Studio и суб-агент ============
            services.AddScoped<ILmStudioClient, LmStudioClient>();
            services.AddScoped<ISubAgentService, SubAgentService>();
            */
            // ============ 8. Клиент LM Studio и суб-агент ============
            services.AddScoped<ILmStudioClient, LmStudioClient>();
            services.AddScoped<ISubAgentService, SubAgentService>();

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

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
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
    }
}