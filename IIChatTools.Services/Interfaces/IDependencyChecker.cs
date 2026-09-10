using System.Collections.Generic;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Интерфейс для проверки наличия внешних зависимостей
    /// </summary>
    public interface IDependencyChecker
    {
        /// <summary>
        /// Проверяет все зависимости и возвращает словарь { имя зависимости -> версия (или null, если не найдена) }
        /// </summary>
        Task<Dictionary<string, string>> CheckAllAsync();

        /// <summary>
        /// Проверяет, установлен ли Git
        /// </summary>
        Task<bool> IsGitInstalledAsync();

        /// <summary>
        /// Проверяет, установлен ли GitHub CLI (gh)
        /// </summary>
        Task<bool> IsGhInstalledAsync();

        /// <summary>
        /// Проверяет, установлен ли Python 3
        /// </summary>
        Task<bool> IsPythonInstalledAsync();

        /// <summary>
        /// Проверяет, установлен ли Node.js
        /// </summary>
        Task<bool> IsNodeInstalledAsync();
    }
}