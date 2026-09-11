using System;
using System.Text;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Implementation;
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
using IIChatTools.Services.Implementation.Tools.FileSystem;

namespace IIChatTools.API
{
    /// <summary>
    /// Конфигурация приложения: сервисы и middleware-конвейер.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Создаёт экземпляр конфигурации.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Конфигурация приложения.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Регистрация сервисов в DI.
        /// </summary>
        /// <param name="services">Коллекция сервисов</param>
        /// <exception cref="ArgumentNullException">Если services равен null</exception>
        public void ConfigureServices(IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // 1. Контекст БД
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            // 2. Identity (регистрирует cookie-схему Identity.Application по умолчанию)
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

            // 3. Аутентификация: JWT добавляется как именованная схема "JwtBearer".
            //    Дефолтная схема (Identity.Application) остаётся для Razor-страниц.
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

            // 4. MVC с локализацией
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

            services.AddSingleton<AppUptimeTracker>();
            services.AddScoped<IStatusService, StatusService>();

            // 5. Сервисы приложения
            services.AddScoped<IDependencyChecker, DependencyChecker>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IApprovalService, ApprovalService>();
            services.AddScoped<IJwtService, JwtService>();
            // Регистрация IToolRegistry будет добавлена после реализации класса ToolRegistry
            // services.AddScoped<IToolRegistry, ToolRegistry>();


            // Резолвер рабочего пространства
            services.AddScoped<IWorkspaceResolver, WorkspaceResolver>();

            // Реестр инструментов
            services.AddScoped<IToolRegistry, ToolRegistry>();

            // Регистрация инструментов файловой системы
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

            // 6. Политики авторизации
            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            });

            // 7. HttpClient для внешних сервисов
            services.AddHttpClient();
        }

        /// <summary>
        /// Конфигурация middleware-конвейера.
        /// </summary>
        /// <param name="app">Построитель приложения</param>
        /// <param name="env">Окружение хостинга</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // Локализация
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
    }
}