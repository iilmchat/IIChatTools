# Восстановление и сборка
dotnet restore IIChatTools.sln
dotnet build IIChatTools.sln -c Debug

# Применение миграций (из корня, где лежит IIChatTools.Data)
dotnet ef migrations add InitialCreate --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API

# Запуск API
dotnet run --project IIChatTools.API

# Запуск тестов
dotnet test IIChatTools.Tests