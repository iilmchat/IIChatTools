using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация проверки внешних зависимостей
    /// </summary>
    public class DependencyChecker : IDependencyChecker
    {
        private readonly ILogger<DependencyChecker> _logger;

        public DependencyChecker(ILogger<DependencyChecker> logger)
        {
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> CheckAllAsync()
        {
            var result = new Dictionary<string, string>();

            // Проверка Git
            var gitVersion = await GetVersionAsync("git", "--version");
            result["git"] = gitVersion;
            _logger.LogInformation(gitVersion != null ? "Git найден: {Version}" : "Git не найден", gitVersion ?? "отсутствует");

            // Проверка GitHub CLI
            var ghVersion = await GetVersionAsync("gh", "--version");
            result["gh"] = ghVersion;
            _logger.LogInformation(ghVersion != null ? "GitHub CLI найден: {Version}" : "GitHub CLI не найден", ghVersion ?? "отсутствует");

            // Проверка Python
            var pythonVersion = await GetVersionAsync("python3", "--version");
            result["python3"] = pythonVersion;
            _logger.LogInformation(pythonVersion != null ? "Python 3 найден: {Version}" : "Python 3 не найден", pythonVersion ?? "отсутствует");

            // Проверка Node.js
            var nodeVersion = await GetVersionAsync("node", "--version");
            result["node"] = nodeVersion;
            _logger.LogInformation(nodeVersion != null ? "Node.js найден: {Version}" : "Node.js не найден", nodeVersion ?? "отсутствует");

            return result;
        }

        public async Task<bool> IsGitInstalledAsync() => await GetVersionAsync("git", "--version") != null;
        public async Task<bool> IsGhInstalledAsync() => await GetVersionAsync("gh", "--version") != null;
        public async Task<bool> IsPythonInstalledAsync() => await GetVersionAsync("python3", "--version") != null;
        public async Task<bool> IsNodeInstalledAsync() => await GetVersionAsync("node", "--version") != null;

        private async Task<string> GetVersionAsync(string command, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Команда {Command} завершилась с кодом {ExitCode}: {Error}", command, process.ExitCode, error);
                    return null;
                }

                var versionLine = string.IsNullOrEmpty(output) ? error : output;
                return versionLine?.Trim().Split('\n')[0];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при проверке зависимости {Command}", command);
                return null;
            }
        }
    }
}