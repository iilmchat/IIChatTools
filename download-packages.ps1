# Путь к NuGetWebDownloader.exe
$nugetDownloader = "G:\AI\NugetWebDownloader-1.0.0\NuGetWebDownloader.exe"

# Папка, куда будем складывать все .nupkg
$outputDir = "G:\AI\IIChatTools\LocalPackages"

# Временная папка для скачивания
$tempDir = "$env:TEMP\NuGetDownloads"

# Создаём папки, если их нет
if (!(Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# Ищем все .csproj в решении
$csprojFiles = Get-ChildItem -Path "G:\AI\IIChatTools" -Filter "*.csproj" -Recurse

# Собираем уникальные пакеты и версии
$packages = @{}

foreach ($csproj in $csprojFiles) {
    Write-Host "Сканирую: $($csproj.FullName)" -ForegroundColor Cyan
    [xml]$xml = Get-Content $csproj.FullName
    $packageRefs = $xml.SelectNodes("//PackageReference")
    foreach ($ref in $packageRefs) {
        $id = $ref.Include
        $version = $ref.Version
        if ($id -and $version) {
            $packages[$id] = $version
        }
    }
}

Write-Host "`nНайдено уникальных пакетов: $($packages.Count)" -ForegroundColor Yellow

# Скачиваем каждый пакет
foreach ($pkg in $packages.GetEnumerator()) {
    $id = $pkg.Key
    $ver = $pkg.Value
    Write-Host "Скачиваю $id $ver..." -ForegroundColor Green
    Set-Location $tempDir
    & $nugetDownloader $id $ver
}

# Копируем все .nupkg в локальную папку
Write-Host "`nКопирую .nupkg в $outputDir..." -ForegroundColor Yellow
Get-ChildItem -Path $tempDir -Filter "*.nupkg" | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

Write-Host "`nГотово! Все пакеты скопированы в $outputDir" -ForegroundColor Green