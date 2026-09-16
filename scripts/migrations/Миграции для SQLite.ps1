# Удалить папку Migrations
Remove-Item -Recurse -Force IIChatTools.Data\Migrations

# Переключить провайдер в appsettings.Development.json на Sqlite

# Создать миграцию
dotnet ef migrations add InitialSqlite --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations

# Применить
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API