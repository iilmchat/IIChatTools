# ============================================================
# test-tool-calls-multi.ps1
# Прогоняет несколько моделей через один tool-call тест.
# ============================================================
# © 2026 RuChating (iilmchat) · IIChatTools v1.0.2

$url = "http://localhost:8034/v1/chat/completions"

$models = @(
    "lmstudio-community/qwen2.5-coder-7b-instruct",
    "qwen/qwen2.5-coder-7b-instruct",
    "qwen/qwen3-4b-2507"
)

$systemPrompt = "Ты — ассистент IIChatTools. Для получения информации о файлах обязательно используй инструмент list_directory. Не выдумывай содержимое каталогов. Если пользователь просит посмотреть файлы — вызови инструмент."

$userPrompt = "Посмотри список файлов в текущей папке. Вызови list_directory с path='.'."

$toolDef = @(
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

$results = @()

foreach ($model in $models) {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "Модель: $model" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan

    $body = @{
        model = $model
        messages = @(
            @{ role = "system"; content = $systemPrompt },
            @{ role = "user";   content = $userPrompt }
        )
        tools = $toolDef
        tool_choice = "auto"
        temperature = 0.2
        max_tokens = 2048
        stream = $false
    } | ConvertTo-Json -Depth 10 -Compress

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "?"
    $finish = "?"
    $toolCallStr = ""
    $errorMsg = ""

    try {
        $resp = Invoke-RestMethod -Uri $url -Method Post `
            -ContentType "application/json; charset=utf-8" `
            -Body $body -TimeoutSec 120
        $sw.Stop()

        $choice = $resp.choices[0]
        $finish = $choice.finish_reason
        $tc = $choice.message.tool_calls

        if ($tc -and $tc.Count -gt 0) {
            $status = "OK"
            $toolCallStr = ($tc | ForEach-Object {
                "$($_.function.name)($($_.function.arguments))"
            }) -join "; "
            Write-Host "✅ tool_calls: $($tc.Count)" -ForegroundColor Green
            foreach ($c in $tc) {
                Write-Host "   - $($c.function.name)($($c.function.arguments))" -ForegroundColor Green
            }
        } else {
            $status = "NO_TOOL"
            Write-Host "❌ tool_calls пусто (finish_reason=$finish)" -ForegroundColor Red
            if ($choice.message.content) {
                Write-Host "   content (обрезка 200): $($choice.message.content.Substring(0, [Math]::Min(200, $choice.message.content.Length)))" -ForegroundColor DarkYellow
            }
        }

        # usage
        if ($resp.usage) {
            Write-Host "   tokens: prompt=$($resp.usage.prompt_tokens) completion=$($resp.usage.completion_tokens) reasoning=$($resp.usage.completion_tokens_details.reasoning_tokens)" -ForegroundColor DarkGray
        }
    }
    catch {
        $sw.Stop()
        $status = "ERROR"
        $errorMsg = $_.Exception.Message
        Write-Host "💥 ОШИБКА: $errorMsg" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "   body: $($_.ErrorDetails.Message.Substring(0, [Math]::Min(300, $_.ErrorDetails.Message.Length)))" -ForegroundColor DarkRed
        }
    }

    $results += [PSCustomObject]@{
        Model      = $model
        Status     = $status
        Finish     = $finish
        ToolCall   = $toolCallStr
        ElapsedMs  = $sw.ElapsedMilliseconds
        Error      = $errorMsg
    }
}

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Yellow
Write-Host "ИТОГИ" -ForegroundColor Yellow
Write-Host ("=" * 70) -ForegroundColor Yellow
$results | Format-Table -AutoSize -Wrap