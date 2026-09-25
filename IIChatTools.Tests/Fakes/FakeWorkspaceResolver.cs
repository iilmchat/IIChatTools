using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Fake <see cref="IWorkspaceResolver"/> для тестов
    /// (v1.5.0, KI-083, Шаг 6A-тесты).
    ///
    /// <para>
    /// Возвращает путь <c>{Root}/users/{userId}</c>, создавая папку при необходимости.
    /// Реальный <c>WorkspaceResolver</c> читает <c>Workspace:RootPath</c> из конфига
    /// и добавляет <c>users/{userId}</c> — этот fake повторяет поведение с
    /// фиксированным корнем (временная папка, заданная тестом).
    /// </para>
    /// </summary>
    public sealed class FakeWorkspaceResolver : IWorkspaceResolver
    {
        private readonly string _root;

        /// <summary>
        /// Создаёт fake с указанным корневым путём.
        /// </summary>
        /// <param name="root">Корень (temp-dir теста)</param>
        public FakeWorkspaceResolver(string root)
        {
            _root = root;
        }

        /// <inheritdoc />
        public Task<string> GetWorkspacePathAsync(int userId)
        {
            var path = Path.Combine(_root, "users", userId.ToString());
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return Task.FromResult(path);
        }
    }
}