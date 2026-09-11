# create_structure.ps1
$ErrorActionPreference = 'Stop'

Write-Host "Creating full IIChatTools project structure..." -ForegroundColor Cyan

# === Каталоги ===
$dirs = @(
  'IIChatTools.Data',
  'IIChatTools.Data\Entities',
  'IIChatTools.Data\Migrations',

  'IIChatTools.Services',
  'IIChatTools.Services\DTO',
  'IIChatTools.Services\DTO\Admin',
  'IIChatTools.Services\DTO\Browser',
  'IIChatTools.Services\DTO\SubAgent',
  'IIChatTools.Services\Extensions',
  'IIChatTools.Services\Interfaces',
  'IIChatTools.Services\Implementation',
  'IIChatTools.Services\Implementation\Tools',
  'IIChatTools.Services\Implementation\Tools\FileSystem',
  'IIChatTools.Services\Implementation\Tools\CodeExecution',
  'IIChatTools.Services\Implementation\Tools\Web',
  'IIChatTools.Services\Implementation\Tools\Git',
  'IIChatTools.Services\Implementation\Tools\GitHub',
  'IIChatTools.Services\Implementation\Tools\Browser',
  'IIChatTools.Services\Implementation\Tools\SubAgent',
  'IIChatTools.Services\Implementation\Tools\Utils',

  'IIChatTools.API',
  'IIChatTools.API\Controllers',
  'IIChatTools.API\DTO',
  'IIChatTools.API\ViewModels',
  'IIChatTools.API\Resources',
  'IIChatTools.API\Views',
  'IIChatTools.API\Views\Auth',
  'IIChatTools.API\Views\Home',
  'IIChatTools.API\Views\Shared',
  'IIChatTools.API\wwwroot',
  'IIChatTools.API\wwwroot\css',
  'IIChatTools.API\wwwroot\js',
  'IIChatTools.API\wwwroot\js\modules',

  'IIChatTools.Tests',
  'IIChatTools.Tests\UnitTests',
  'IIChatTools.Tests\IntegrationTests'
)

foreach ($d in $dirs) {
    if (-not (Test-Path -LiteralPath $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

# === Файлы ===
$files = @(
  'IIChatTools.sln',
  'Directory.Build.props',
  'global.json',
  'NuGet.Config',
  'README.md',

  # --- IIChatTools.Data ---
  'IIChatTools.Data\AppDbContext.cs',
  'IIChatTools.Data\SeedData.cs',
  'IIChatTools.Data\Entities\BaseEntity.cs',
  'IIChatTools.Data\Entities\ApplicationUser.cs',
  'IIChatTools.Data\Entities\AuditLog.cs',
  'IIChatTools.Data\Entities\AppSetting.cs',
  'IIChatTools.Data\Entities\PendingAction.cs',
  'IIChatTools.Data\Entities\AgentState.cs',
  'IIChatTools.Data\Entities\MemoryEntry.cs',

  # --- IIChatTools.Services / DTO ---
  'IIChatTools.Services\DTO\ToolResult.cs',
  'IIChatTools.Services\DTO\ToolDescriptor.cs',
  'IIChatTools.Services\DTO\ToolParameterDescriptor.cs',
  'IIChatTools.Services\DTO\ToolExecutionContext.cs',
  'IIChatTools.Services\DTO\StatusSnapshot.cs',
  'IIChatTools.Services\DTO\PagedResult.cs',
  'IIChatTools.Services\DTO\Admin\UserListDto.cs',
  'IIChatTools.Services\DTO\Admin\UserEditDto.cs',
  'IIChatTools.Services\DTO\Admin\SettingDto.cs',
  'IIChatTools.Services\DTO\Admin\WhitelistEntryDto.cs',
  'IIChatTools.Services\DTO\Browser\BrowserSessionInfo.cs',
  'IIChatTools.Services\DTO\SubAgent\SubAgentTaskRequest.cs',
  'IIChatTools.Services\DTO\SubAgent\SubAgentTaskResult.cs',
  'IIChatTools.Services\DTO\SubAgent\SubAgentStep.cs',

  # --- IIChatTools.Services / Extensions ---
  'IIChatTools.Services\Extensions\JObjectExtensions.cs',

  # --- IIChatTools.Services / Interfaces ---
  'IIChatTools.Services\Interfaces\ITool.cs',
  'IIChatTools.Services\Interfaces\IToolRegistry.cs',
  'IIChatTools.Services\Interfaces\IAuditService.cs',
  'IIChatTools.Services\Interfaces\IAuditQueryService.cs',
  'IIChatTools.Services\Interfaces\IApprovalService.cs',
  'IIChatTools.Services\Interfaces\IDependencyChecker.cs',
  'IIChatTools.Services\Interfaces\IJwtService.cs',
  'IIChatTools.Services\Interfaces\IStatusService.cs',
  'IIChatTools.Services\Interfaces\IWorkspaceResolver.cs',
  'IIChatTools.Services\Interfaces\IAppSettingsService.cs',
  'IIChatTools.Services\Interfaces\IUserAdminService.cs',
  'IIChatTools.Services\Interfaces\IProcessRunner.cs',
  'IIChatTools.Services\Interfaces\IBrowserSessionManager.cs',
  'IIChatTools.Services\Interfaces\ILmStudioClient.cs',
  'IIChatTools.Services\Interfaces\ISubAgentService.cs',

  # --- IIChatTools.Services / Implementation ---
  'IIChatTools.Services\Implementation\AppUptimeTracker.cs',
  'IIChatTools.Services\Implementation\AppVersionHolder.cs',
  'IIChatTools.Services\Implementation\AuditService.cs',
  'IIChatTools.Services\Implementation\AuditQueryService.cs',
  'IIChatTools.Services\Implementation\ApprovalService.cs',
  'IIChatTools.Services\Implementation\DependencyChecker.cs',
  'IIChatTools.Services\Implementation\PathHelper.cs',
  'IIChatTools.Services\Implementation\JwtService.cs',
  'IIChatTools.Services\Implementation\StatusService.cs',
  'IIChatTools.Services\Implementation\WorkspaceResolver.cs',
  'IIChatTools.Services\Implementation\AppSettingsService.cs',
  'IIChatTools.Services\Implementation\UserAdminService.cs',
  'IIChatTools.Services\Implementation\ProcessRunner.cs',
  'IIChatTools.Services\Implementation\BrowserSessionManager.cs',
  'IIChatTools.Services\Implementation\LmStudioClient.cs',
  'IIChatTools.Services\Implementation\SubAgentService.cs',
  'IIChatTools.Services\Implementation\ToolRegistry.cs',

  # --- IIChatTools.Services / Tools / FileSystem ---
  'IIChatTools.Services\Implementation\Tools\FileSystem\ListDirectoryTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\ChangeDirectoryTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\MakeDirectoryTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\ReadFileTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\SaveFileTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\DeletePathTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\ReplaceTextInFileTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\DeleteFilesByPatternTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\MoveFileTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\CopyFileTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\FindFilesTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\GetFileMetadataTool.cs',
  'IIChatTools.Services\Implementation\Tools\FileSystem\FuzzyFindLocalFilesTool.cs',

  # --- IIChatTools.Services / Tools / CodeExecution ---
  'IIChatTools.Services\Implementation\Tools\CodeExecution\RunJavaScriptTool.cs',
  'IIChatTools.Services\Implementation\Tools\CodeExecution\RunPythonTool.cs',
  'IIChatTools.Services\Implementation\Tools\CodeExecution\ExecuteCommandTool.cs',

  # --- IIChatTools.Services / Tools / Web ---
  'IIChatTools.Services\Implementation\Tools\Web\WebSearchTool.cs',
  'IIChatTools.Services\Implementation\Tools\Web\WikipediaSearchTool.cs',
  'IIChatTools.Services\Implementation\Tools\Web\FetchWebContentTool.cs',

  # --- IIChatTools.Services / Tools / Git ---
  'IIChatTools.Services\Implementation\Tools\Git\BaseGitTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitStatusTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitDiffTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitLogTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitAddTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitCommitTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitCheckoutTool.cs',
  'IIChatTools.Services\Implementation\Tools\Git\GitPushTool.cs',

  # --- IIChatTools.Services / Tools / GitHub ---
  'IIChatTools.Services\Implementation\Tools\GitHub\BaseGhTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhAuthStatusTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhCreateIssueTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhListIssuesTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhViewCommentsTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhCreatePrTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhListPrsTool.cs',
  'IIChatTools.Services\Implementation\Tools\GitHub\GhViewPrDiffTool.cs',

  # --- IIChatTools.Services / Tools / Browser ---
  'IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionOpenTool.cs',
  'IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionControlTool.cs',
  'IIChatTools.Services\Implementation\Tools\Browser\BrowserSessionCloseTool.cs',
  'IIChatTools.Services\Implementation\Tools\Browser\BrowserOpenPageTool.cs',

  # --- IIChatTools.Services / Tools / SubAgent ---
  'IIChatTools.Services\Implementation\Tools\SubAgent\RunSubAgentTool.cs',

  # --- IIChatTools.Services / Tools / Utils ---
  'IIChatTools.Services\Implementation\Tools\Utils\GetSystemInfoTool.cs',
  'IIChatTools.Services\Implementation\Tools\Utils\SaveMemoryTool.cs',

  # --- IIChatTools.API ---
  'IIChatTools.API\Program.cs',
  'IIChatTools.API\Startup.cs',
  'IIChatTools.API\AppVersion.cs',
  'IIChatTools.API\appsettings.json',
  'IIChatTools.API\appsettings.Development.json',
  'IIChatTools.API\Controllers\HomeController.cs',
  'IIChatTools.API\Controllers\AuthController.cs',
  'IIChatTools.API\Controllers\ToolsController.cs',
  'IIChatTools.API\Controllers\ApprovalsController.cs',
  'IIChatTools.API\Controllers\AdminController.cs',
  'IIChatTools.API\Controllers\StatusController.cs',
  'IIChatTools.API\DTO\ExecuteToolRequest.cs',
  'IIChatTools.API\ViewModels\LoginViewModel.cs',
  'IIChatTools.API\ViewModels\RegisterViewModel.cs',
  'IIChatTools.API\Resources\SharedResources.cs',
  'IIChatTools.API\Resources\SharedResources.resx',
  'IIChatTools.API\Resources\SharedResources.ru.resx',
  'IIChatTools.API\Views\_ViewStart.cshtml',
  'IIChatTools.API\Views\_ViewImports.cshtml',
  'IIChatTools.API\Views\Auth\Login.cshtml',
  'IIChatTools.API\Views\Auth\Register.cshtml',
  'IIChatTools.API\Views\Home\Index.cshtml',
  'IIChatTools.API\Views\Home\Status.cshtml',
  'IIChatTools.API\Views\Home\Admin.cshtml',
  'IIChatTools.API\Views\Home\Test.cshtml',
  'IIChatTools.API\Views\Home\Error.cshtml',
  'IIChatTools.API\Views\Shared\_Layout.cshtml',
  'IIChatTools.API\Views\Shared\_LoginPartial.cshtml',
  'IIChatTools.API\Views\Shared\_ApprovalModal.cshtml',
  'IIChatTools.API\Views\Shared\_ValidationScriptsPartial.cshtml',
  'IIChatTools.API\wwwroot\css\site.css',
  'IIChatTools.API\wwwroot\js\main.js',
  'IIChatTools.API\wwwroot\js\modules\api.js',
  'IIChatTools.API\wwwroot\js\modules\ui.js',
  'IIChatTools.API\wwwroot\js\modules\status.js',
  'IIChatTools.API\wwwroot\js\modules\approvals.js',
  'IIChatTools.API\wwwroot\js\modules\admin.js',
  'IIChatTools.API\wwwroot\js\modules\test.js',

  # --- IIChatTools.Tests ---
  'IIChatTools.Tests\UnitTests\LocalizationSyncTests.cs',
  'IIChatTools.Tests\UnitTests\PathHelperTests.cs',
  'IIChatTools.Tests\IntegrationTests\TestDbContextFactory.cs',
  'IIChatTools.Tests\IntegrationTests\ApprovalServiceTests.cs',
  'IIChatTools.Tests\IntegrationTests\ToolRegistryTests.cs'
)

foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath $f)) {
        New-Item -ItemType File -Path $f -Force | Out-Null
    }
}

Write-Host ""
Write-Host "Structure created successfully!" -ForegroundColor Green
Write-Host "Existing files were NOT overwritten." -ForegroundColor Yellow