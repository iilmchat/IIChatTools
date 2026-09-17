# ============================================================
# test-tool-calls.ps1
# Проверяет, умеет ли модель возвращать tool_calls.
# Прямой запрос к LM Studio, минуя IIChatTools.API.
# ============================================================
# © 2026 RuChating (iilmchat) · IIChatTools v1.0.2

$model = "gemma-4-12b-coder-fable5-composer2.5-v1"
$url   = "http://localhost:8034/v1/chat/completions"

$body = @{
    model = $model
    messages = @(
        @{
            role = "system"
            content = "Ты — ассистент IIChatTools. Для получения информации о файлах обязательно используй инструмент list_directory. Не выдумывай содержимое каталогов."
        },
        @{
            role = "user"
            content = "Посмотри список файлов в текущей папке. Вызови list_directory с path='.'."
        }
    )
    tools = @(
        @{
            type = "function"
            function = @{
                name = "list_directory"
                description = "Возвращает список файлов и подкаталогов в указанной директории"
                parameters = @{
                    type = "object"
                    properties = @{
                        path = @{
                            type = "string"
                            description = "Относительный путь к каталогу. По умолчанию — корень workspace."
                        }
                    }
                    required = @("path")
                }
            }
        }
    )
    tool_choice = "auto"
    temperature = 0.3
    max_tokens = 8192
    stream = $false
} | ConvertTo-Json -Depth 10 -Compress

Write-Host "Запрос к $model ..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $resp = Invoke-RestMethod -Uri $url -Method Post `
        -ContentType "application/json" -Body $body
    $sw.Stop()

    Write-Host "Ответ за $($sw.ElapsedMilliseconds) мс" -ForegroundColor Green
    Write-Host ""
    Write-Host "=== Полный ответ ===" -ForegroundColor Yellow
    $resp | ConvertTo-Json -Depth 20

    Write-Host ""
    Write-Host "=== Ключевые поля ===" -ForegroundColor Yellow
    $choice = $resp.choices[0]
    Write-Host "finish_reason: $($choice.finish_reason)"

    $msg = $choice.message
    Write-Host "content:       $($msg.content)"
    Write-Host "reasoning:     $($msg.reasoning_content -replace "`n",' ' | Select-Object -First 1)..."

    $tc = $msg.tool_calls
    if ($tc -and $tc.Count -gt 0) {
        Write-Host ""
        Write-Host "✅ tool_calls: НАЙДЕНО $($tc.Count) вызов(ов)" -ForegroundColor Green
        foreach ($call in $tc) {
            Write-Host "  - id:       $($call.id)"
            Write-Host "    function: $($call.function.name)"
            Write-Host "    args:     $($call.function.arguments)"
        }
    } else {
        Write-Host ""
        Write-Host "❌ tool_calls: пусто — модель НЕ вызвала инструмент" -ForegroundColor Red
    }
}
catch {
    $sw.Stop()
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Тело ответа: $($_.ErrorDetails.Message)" -ForegroundColor DarkRed
}