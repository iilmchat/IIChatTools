param(
    [string]$ProjectRoot = "D:\Projects\IIChatTools",
    [string]$OutputDir   = "D:\IIChatTools-export"
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$sourceExtensions = @(
    '.cs', '.csproj', '.sln', '.props', '.json', '.config',
    '.cshtml', '.js', '.css', '.resx',
    '.ps1', '.sh', '.bat', '.md', '.editorconfig', '.gitattributes', '.gitignore'
)

$excludePaths = @(
    '\bin\', '\obj\', '\.git\', '\.vs\', '\.idea\',
    '\Data\', '\logs\', '\Workspace\',
    '\LocalPackages\', '\packages\',
    '\node_modules\', '\publish\',
    '\docs\development\archive\',
    'screenshot.png', 'screenshot.b64'
)

function Should-Skip {
    param([string]$Path)
    foreach ($pattern in $excludePaths) {
        if ($Path -like "*$pattern*") { return $true }
    }
    return $false
}

function Export-Group {
    param(
        [string]$GroupName,
        [string[]]$IncludePatterns
    )

    $outputFile = Join-Path $OutputDir "$GroupName.txt"
    $writer = New-Object System.IO.StreamWriter($outputFile, $false, [System.Text.Encoding]::UTF8)

    $writer.WriteLine("=" * 80)
    $writer.WriteLine("IIChatTools v1.0.2 -- group: $GroupName")
    $writer.WriteLine("Export date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $writer.WriteLine("=" * 80)
    $writer.WriteLine("")

    $fileCount = 0
    $totalBytes = 0

    foreach ($pattern in $IncludePatterns) {
        $fullPattern = Join-Path $ProjectRoot $pattern
        Get-ChildItem -Path $fullPattern -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object {
                $ext = $_.Extension.ToLower()
                ($sourceExtensions -contains $ext -or $_.Name -in @('.editorconfig', '.gitattributes', '.gitignore')) -and
                -not (Should-Skip $_.FullName)
            } |
            Sort-Object FullName |
            ForEach-Object {
                $relPath = $_.FullName.Substring($ProjectRoot.Length + 1).Replace('\', '/')
                $writer.WriteLine("")
                $writer.WriteLine("-" * 80)
                $writer.WriteLine("FILE: $relPath")
                $writer.WriteLine("-" * 80)

                try {
                    $c = Get-Content $_.FullName -Raw -Encoding UTF8 -ErrorAction Stop
                    if ($c) {
                        $writer.Write($c)
                        if (-not $c.EndsWith("`n")) { $writer.WriteLine("") }
                    }
                    $fileCount++
                    $totalBytes += $_.Length
                }
                catch {
                    $writer.WriteLine("[READ ERROR: $($_.Exception.Message)]")
                }
            }
    }

    $writer.WriteLine("")
    $writer.WriteLine("=" * 80)
    $writer.WriteLine("TOTAL files: $fileCount, source size: $([math]::Round($totalBytes / 1KB, 1)) KB")
    $writer.WriteLine("=" * 80)
    $writer.Close()

    $outSize = (Get-Item $outputFile).Length
    Write-Host "OK $GroupName.txt - $fileCount files, $([math]::Round($outSize / 1KB, 1)) KB" -ForegroundColor Green
}

Write-Host "Exporting IIChatTools sources..." -ForegroundColor Cyan
Write-Host "Source: $ProjectRoot"
Write-Host "Target: $OutputDir"
Write-Host ""

Export-Group -GroupName "01-root-and-data" -IncludePatterns @(
    ".",
    "IIChatTools.Data"
)

Export-Group -GroupName "02-services-dto-interfaces" -IncludePatterns @(
    "IIChatTools.Services\DTO",
    "IIChatTools.Services\Interfaces",
    "IIChatTools.Services\Extensions"
)

Export-Group -GroupName "03-services-impl-tools" -IncludePatterns @(
    "IIChatTools.Services\Implementation"
)

Export-Group -GroupName "04-api-web" -IncludePatterns @(
    "IIChatTools.API\Controllers",
    "IIChatTools.API\Views",
    "IIChatTools.API\wwwroot",
    "IIChatTools.API\Resources",
    "IIChatTools.API\ViewModels",
    "IIChatTools.API\DTO",
    "IIChatTools.API\Extensions"
)

Export-Group -GroupName "05-api-root-tests-docs" -IncludePatterns @(
    "IIChatTools.API",
    "IIChatTools.Tests",
    "docs"
)

Write-Host ""
Write-Host "Done! Files in: $OutputDir" -ForegroundColor Cyan
Write-Host ""
Get-ChildItem $OutputDir -Filter "*.txt" |
    Select-Object Name, @{N='SizeKB';E={[math]::Round($_.Length/1KB,1)}} |
    Format-Table -AutoSize
