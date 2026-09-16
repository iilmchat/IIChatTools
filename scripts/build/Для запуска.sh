dotnet restore
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API
dotnet run --project IIChatTools.API