@echo off
echo Creating directory structure for IIChatTools...

:: === Корневые папки и файл решения ===
mkdir IIChatTools.API
mkdir IIChatTools.Data
mkdir IIChatTools.Services
mkdir IIChatTools.Tests
type nul > IIChatTools.sln

:: === IIChatTools.API ===
mkdir IIChatTools.API\Controllers
mkdir IIChatTools.API\Views
mkdir IIChatTools.API\Views\Home
mkdir IIChatTools.API\Views\Shared
mkdir IIChatTools.API\wwwroot
mkdir IIChatTools.API\wwwroot\css
mkdir IIChatTools.API\wwwroot\js
mkdir IIChatTools.API\wwwroot\js\modules
mkdir IIChatTools.API\wwwroot\lib

type nul > IIChatTools.API\Program.cs
type nul > IIChatTools.API\Startup.cs
type nul > IIChatTools.API\appsettings.json
type nul > IIChatTools.API\appsettings.Development.json

type nul > IIChatTools.API\Controllers\AuthController.cs
type nul > IIChatTools.API\Controllers\ToolsController.cs
type nul > IIChatTools.API\Controllers\ApprovalsController.cs
type nul > IIChatTools.API\Controllers\AdminController.cs
type nul > IIChatTools.API\Controllers\StatusController.cs

type nul > IIChatTools.API\Views\_ViewStart.cshtml
type nul > IIChatTools.API\Views\Home\Index.cshtml
type nul > IIChatTools.API\Views\Home\Status.cshtml
type nul > IIChatTools.API\Views\Home\Admin.cshtml
type nul > IIChatTools.API\Views\Home\Test.cshtml
type nul > IIChatTools.API\Views\Shared\_Layout.cshtml
type nul > IIChatTools.API\Views\Shared\_ValidationScriptsPartial.cshtml

type nul > IIChatTools.API\wwwroot\js\main.js
type nul > IIChatTools.API\wwwroot\js\modules\api.js
type nul > IIChatTools.API\wwwroot\js\modules\approvals.js
type nul > IIChatTools.API\wwwroot\js\modules\ui.js

:: === IIChatTools.Data ===
mkdir IIChatTools.Data\Entities
mkdir IIChatTools.Data\Migrations

type nul > IIChatTools.Data\AppDbContext.cs
type nul > IIChatTools.Data\SeedData.cs

type nul > IIChatTools.Data\Entities\BaseEntity.cs
type nul > IIChatTools.Data\Entities\ApplicationUser.cs
type nul > IIChatTools.Data\Entities\AuditLog.cs
type nul > IIChatTools.Data\Entities\AppSetting.cs
type nul > IIChatTools.Data\Entities\PendingAction.cs
type nul > IIChatTools.Data\Entities\AgentState.cs
type nul > IIChatTools.Data\Entities\MemoryEntry.cs

:: === IIChatTools.Services ===
mkdir IIChatTools.Services\Interfaces
mkdir IIChatTools.Services\Implementation
mkdir IIChatTools.Services\Tools
mkdir IIChatTools.Services\Tools\FileSystemTools
mkdir IIChatTools.Services\Tools\CodeExecutionTools
mkdir IIChatTools.Services\Tools\WebTools
mkdir IIChatTools.Services\Tools\GitTools
mkdir IIChatTools.Services\Tools\SubAgentTools
mkdir IIChatTools.Services\Resources

type nul > IIChatTools.Services\Interfaces\IDependencyChecker.cs
type nul > IIChatTools.Services\Interfaces\IAuditService.cs
type nul > IIChatTools.Services\Interfaces\IApprovalService.cs
type nul > IIChatTools.Services\Interfaces\IToolRegistry.cs

type nul > IIChatTools.Services\Implementation\DependencyChecker.cs
type nul > IIChatTools.Services\Implementation\AuditService.cs
type nul > IIChatTools.Services\Implementation\ApprovalService.cs
type nul > IIChatTools.Services\Implementation\ToolRegistry.cs
type nul > IIChatTools.Services\Implementation\PathHelper.cs

type nul > IIChatTools.Services\Resources\SharedResources.resx
type nul > IIChatTools.Services\Resources\SharedResources.ru.resx

:: === IIChatTools.Tests ===
mkdir IIChatTools.Tests\UnitTests
mkdir IIChatTools.Tests\IntegrationTests

echo.
echo Structure created successfully!
pause