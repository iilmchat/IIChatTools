New-Item -ItemType Directory -Force -Path D:\Projects\IIChatTools\LocalPackages
dotnet restore IIChatTools.sln --configfile NuGet.Config.online --packages D:\Projects\IIChatTools\LocalPackages --force