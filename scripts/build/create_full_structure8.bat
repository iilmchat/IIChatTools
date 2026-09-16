@echo off
setlocal

echo Creating Sprint 8 directory structure for IIChatTools...

rem === Создание каталогов ===
mkdir "IIChatTools.Data" 2>nul
mkdir "IIChatTools.Data\Entities" 2>nul
mkdir "IIChatTools.Data\Migrations" 2>nul

mkdir "IIChatTools.Services" 2>nul
mkdir "IIChatTools.Services\DTO" 2>nul
mkdir "IIChatTools.Services\DTO\Browser" 2>nul
mkdir "IIChatTools.Services\DTO\Admin" 2>nul
mkdir "IIChatTools.Services\Extensions" 2>nul
mkdir "IIChatTools.Services\Interfaces" 2>nul
mkdir "IIChatTools.Services\Implementation" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\FileSystem" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\CodeExecution" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\Web" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\Git" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\GitHub" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\Browser" 2>nul
mkdir "IIChatTools.Services\Implementation\Tools\Utils" 2>nul

mkdir "IIChatTools.API" 2>nul
mkdir "IIChatTools.API\Controllers" 2>nul
mkdir "IIChatTools.API\DTO" 2>nul
mkdir "IIChatTools.API\ViewModels" 2>nul
mkdir "IIChatTools.API\Resources" 2>nul
mkdir "IIChatTools.API\Views" 2>nul
mkdir "IIChatTools.API\Views\Auth" 2>nul
mkdir "IIChatTools.API\Views\Home" 2>nul
mkdir "IIChatTools.API\Views\Shared" 2>nul
mkdir "IIChatTools.API\wwwroot" 2>nul
mkdir "IIChatTools.API\wwwroot\css" 2>nul
mkdir "IIChatTools.API\wwwroot\js" 2>nul
mkdir "IIChatTools.API\wwwroot\js\modules" 2>nul

mkdir "IIChatTools.Tests" 2>nul
mkdir "IIChatTools.Tests\UnitTests" 2>nul

rem === Создание файлов (только если отсутствуют) ===
for %%F in (
  "IIChatTools.sln"
  "Directory.Build.props"
  "global.json"
  "NuGet.Config"
  "IIChatTools.Data\AppDbContext.cs"
  "IIChatTools.Data\SeedData.cs"
  "IIChatTools.Data\Entities\BaseEntity.cs"
  "IIChatTools.Data\Entities\ApplicationUser.cs"
  "IIChatTools.Data\Entities\AuditLog.cs"
  "IIChatTools.Data\Entities\AppSetting.cs"
  "IIChatTools.Data\Entities\PendingAction.cs"
  "IIChatTools.Data\Entities\AgentState.cs"
  "IIChatTools.Data\Entities\MemoryEntry.cs"
  "IIChatTools.Services\DTO\ToolResult.cs"
  "IIChatTools.Services\DTO\ToolDescriptor.cs"
  "IIChatTools.Services\DTO\ToolParameterDescriptor.cs"
  "IIChatTools.Services\DTO\ToolExecutionContext.cs"
  "IIChatTools.Services\DTO\StatusSnapshot.cs"
  "IIChatTools.Services\DTO\PagedResult.cs"
  "IIChatTools.Services\DTO\Browser\BrowserSessionInfo.cs"
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
  "IIChatTools.Services\Interfaces\IProcessRunner.cs"
  "IIChatTools.Services\Interfaces\IBrowserSessionManager.cs"
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
  "IIChatTools.Services\Implementation\ProcessRunner.cs"
  "IIChatTools.Services\Implementation\BrowserSessionManager.cs"
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
  "IIChatTools.Services\Implementation\Tools\CodeExecution\RunJavaScriptTool.cs"
  "IIChatTools.Services\Implementation\Tools\CodeExecution\RunPythonTool.cs"
  "IIChatTools.Services\Implementation\Tools\CodeExecution\ExecuteCommandTool.cs"
  "IIChatTools.Services\Implementation\Tools\Web\WebSearchTool.cs"
  "IIChatTools.Services\Implementation\Tools\Web\WikipediaSearchTool.cs"
  "IIChatTools.Services\Implementation\Tools\Web\FetchWebContentTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\BaseGitTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitStatusTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitDiffTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitLogTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitAddTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitCommitTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitCheckoutTool.cs"
  "IIChatTools.Services\Implementation\Tools\Git\GitPushTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\BaseGhTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhAuthStatusTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhCreateIssueTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhListIssuesTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhViewCommentsTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhCreatePrTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhListPrsTool.cs"
  "IIChatTools.Services\Implementation\Tools\GitHub\GhViewPrDiffTool.cs"
  "IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionOpenTool.cs"
  "IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionControlTool.cs"
  "IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionCloseTool.cs"
  "IIChatTools.Services\Implementation\Tools\Browser\BrowserOpenPageTool.cs"
  "IIChatTools.Services\Implementation\Tools\Utils\GetSystemInfoTool.cs"
  "IIChatTools.Services\Implementation\Tools\Utils\SaveMemoryTool.cs"
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
  "IIChatTools.Tests\UnitTests\LocalizationSyncTests.cs"
) do (
  if not exist "%%~F" type nul > "%%~F"
)

echo.
echo Sprint 8 structure created successfully!
echo Existing files were NOT overwritten.
pause
endlocal