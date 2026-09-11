@echo off
setlocal

echo Creating full directory structure for IIChatTools...

:: ============================================
:: Создание каталогов
:: ============================================
mkdir IIChatTools.Data
mkdir IIChatTools.Data\Entities
mkdir IIChatTools.Data\Migrations

mkdir IIChatTools.Services
mkdir IIChatTools.Services\DTO
mkdir IIChatTools.Services\DTO\Admin
mkdir IIChatTools.Services\Extensions
mkdir IIChatTools.Services\Interfaces
mkdir IIChatTools.Services\Implementation
mkdir IIChatTools.Services\Implementation\Tools
mkdir IIChatTools.Services\Implementation\Tools\FileSystem

mkdir IIChatTools.API
mkdir IIChatTools.API\Controllers
mkdir IIChatTools.API\DTO
mkdir IIChatTools.API\ViewModels
mkdir IIChatTools.API\Resources
mkdir IIChatTools.API\Views
mkdir IIChatTools.API\Views\Auth
mkdir IIChatTools.API\Views\Home
mkdir IIChatTools.API\Views\Shared
mkdir IIChatTools.API\wwwroot
mkdir IIChatTools.API\wwwroot\css
mkdir IIChatTools.API\wwwroot\js
mkdir IIChatTools.API\wwwroot\js\modules

mkdir IIChatTools.Tests
mkdir IIChatTools.Tests\UnitTests

:: ============================================
:: Создание файлов (только если отсутствуют)
:: ============================================
for %%F in (
  "IIChatTools.sln"
  "Directory.Build.props"
  "global.json"
  "NuGet.Config"

  "IIChatTools.Data\IIChatTools.Data.csproj"
  "IIChatTools.Data\AppDbContext.cs"
  "IIChatTools.Data\SeedData.cs"
  "IIChatTools.Data\Entities\BaseEntity.cs"
  "IIChatTools.Data\Entities\ApplicationUser.cs"
  "IIChatTools.Data\Entities\AuditLog.cs"
  "IIChatTools.Data\Entities\AppSetting.cs"
  "IIChatTools.Data\Entities\PendingAction.cs"
  "IIChatTools.Data\Entities\AgentState.cs"
  "IIChatTools.Data\Entities\MemoryEntry.cs"

  "IIChatTools.Services\IIChatTools.Services.csproj"
  "IIChatTools.Services\DTO\ToolResult.cs"
  "IIChatTools.Services\DTO\ToolDescriptor.cs"
  "IIChatTools.Services\DTO\ToolParameterDescriptor.cs"
  "IIChatTools.Services\DTO\ToolExecutionContext.cs"
  "IIChatTools.Services\DTO\StatusSnapshot.cs"
  "IIChatTools.Services\DTO\PagedResult.cs"
  "IIChatTools.Services\DTO\Admin\UserListDto.cs"
  "IIChatTools.Services\DTO\Admin\UserEditDto.cs"
  "IIChatTools.Services\DTO\Admin\SettingDto.cs"
  "IIChatTools.Services\DTO\Admin\WhitelistEntryDto.cs"
  "IIChatTools.Services\Extensions\JObjectExtensions.cs"
  "IIChatTools.Services\Interfaces\ITool.cs"
  "IIChatTools.Services\Interfaces\IToolRegistry.cs"
  "IIChatTools.Services\Interfaces\IAuditService.cs"
  "IIChatTools.Services\Interfaces\IAuditQueryService.cs"
  "IIChatTools.Services\Interfaces\IApprovalService.cs"
  "IIChatTools.Services\Interfaces\IDependencyChecker.cs"
  "IIChatTools.Services\Interfaces\IJwtService.cs"
  "IIChatTools.Services\Interfaces\IStatusService.cs"
  "IIChatTools.Services\Interfaces\IWorkspaceResolver.cs"
  "IIChatTools.Services\Interfaces\IAppSettingsService.cs"
  "IIChatTools.Services\Interfaces\IUserAdminService.cs"
  "IIChatTools.Services\Implementation\AppUptimeTracker.cs"
  "IIChatTools.Services\Implementation\AppVersionHolder.cs"
  "IIChatTools.Services\Implementation\AuditService.cs"
  "IIChatTools.Services\Implementation\AuditQueryService.cs"
  "IIChatTools.Services\Implementation\ApprovalService.cs"
  "IIChatTools.Services\Implementation\DependencyChecker.cs"
  "IIChatTools.Services\Implementation\PathHelper.cs"
  "IIChatTools.Services\Implementation\JwtService.cs"
  "IIChatTools.Services\Implementation\StatusService.cs"
  "IIChatTools.Services\Implementation\WorkspaceResolver.cs"
  "IIChatTools.Services\Implementation\AppSettingsService.cs"
  "IIChatTools.Services\Implementation\UserAdminService.cs"
  "IIChatTools.Services\Implementation\ToolRegistry.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\ListDirectoryTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\ChangeDirectoryTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\MakeDirectoryTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\ReadFileTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\SaveFileTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\DeletePathTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\ReplaceTextInFileTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\DeleteFilesByPatternTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\MoveFileTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\CopyFileTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\FindFilesTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\GetFileMetadataTool.cs"
  "IIChatTools.Services\Implementation\Tools\FileSystem\FuzzyFindLocalFilesTool.cs"

  "IIChatTools.API\IIChatTools.API.csproj"
  "IIChatTools.API\Program.cs"
  "IIChatTools.API\Startup.cs"
  "IIChatTools.API\AppVersion.cs"
  "IIChatTools.API\appsettings.json"
  "IIChatTools.API\appsettings.Development.json"
  "IIChatTools.API\Controllers\HomeController.cs"
  "IIChatTools.API\Controllers\AuthController.cs"
  "IIChatTools.API\Controllers\ToolsController.cs"
  "IIChatTools.API\Controllers\ApprovalsController.cs"
  "IIChatTools.API\Controllers\AdminController.cs"
  "IIChatTools.API\Controllers\StatusController.cs"
  "IIChatTools.API\DTO\ExecuteToolRequest.cs"
  "IIChatTools.API\ViewModels\LoginViewModel.cs"
  "IIChatTools.API\ViewModels\RegisterViewModel.cs"
  "IIChatTools.API\Resources\SharedResources.cs"
  "IIChatTools.API\Resources\SharedResources.resx"
  "IIChatTools.API\Resources\SharedResources.ru.resx"
  "IIChatTools.API\Views\_ViewStart.cshtml"
  "IIChatTools.API\Views\_ViewImports.cshtml"
  "IIChatTools.API\Views\Auth\Login.cshtml"
  "IIChatTools.API\Views\Auth\Register.cshtml"
  "IIChatTools.API\Views\Home\Index.cshtml"
  "IIChatTools.API\Views\Home\Status.cshtml"
  "IIChatTools.API\Views\Home\Admin.cshtml"
  "IIChatTools.API\Views\Home\Test.cshtml"
  "IIChatTools.API\Views\Home\Error.cshtml"
  "IIChatTools.API\Views\Shared\_Layout.cshtml"
  "IIChatTools.API\Views\Shared\_LoginPartial.cshtml"
  "IIChatTools.API\Views\Shared\_ApprovalModal.cshtml"
  "IIChatTools.API\Views\Shared\_ValidationScriptsPartial.cshtml"
  "IIChatTools.API\wwwroot\css\site.css"
  "IIChatTools.API\wwwroot\js\main.js"
  "IIChatTools.API\wwwroot\js\modules\api.js"
  "IIChatTools.API\wwwroot\js\modules\ui.js"
  "IIChatTools.API\wwwroot\js\modules\status.js"
  "IIChatTools.API\wwwroot\js\modules\approvals.js"
  "IIChatTools.API\wwwroot\js\modules\admin.js"
  "IIChatTools.API\wwwroot\js\modules\test.js"

  "IIChatTools.Tests\IIChatTools.Tests.csproj"
  "IIChatTools.Tests\UnitTests\LocalizationSyncTests.cs"
) do (
  if not exist "%%~F" type nul > "%%~F"
)

echo.
echo Structure created successfully (existing files were not overwritten)!
pause
endlocal