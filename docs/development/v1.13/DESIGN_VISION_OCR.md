# DESIGN v1.13 — Vision Agent: OCR-fallback для мелкого текста (KI-137)

**Версия:** 1.0 (Draft)
**Дата:** 2026-10-08
**Статус:** Draft — ожидает согласования
**Связанные KI:** KI-137 (этот документ), KI-131 (Vision Agent), KI-203 (RAG OCR), KI-161 (CDP-attach), KI-160 (нестабильность VL), KI-190 (bounds-center)
**Целевой релиз:** v1.13.x
**Связанные документы:** [DESIGN_VISION_AGENT.md](../v1.12/DESIGN_VISION_AGENT.md) · [DESIGN_VISION_CDP_ATTACH.md](DESIGN_VISION_CDP_ATTACH.md) · [RULES.md](../RULES.md) · [KNOWN_ISSUES.md](../../KNOWN_ISSUES.md)

---

## § 1. Контекст

### § 1.1. Проблема: VL-модели плохо читают мелкий текст

VL-модели (Qwen2.5-VL-7B, Ministral-3B) дают общее описание экрана, но не могут **точно воспроизвести текст** мелких элементов:

| Тип текста | Что видит VL | Что нужно |
|---|---|---|
| Шрифты 8-10 px в формах | «поле ввода с текстом» | Точный placeholder / value |
| Капча (не для обхода — для чтения) | «изображение с текстом» | Код |
| Плотные таблицы | «таблица» | Числа по ячейкам |
| Кнопка с длинным label | «кнопка» | Точный label (для выбора target) |
| Текст ошибки в форме | «сообщение об ошибке» | Точный текст ошибки |

**Следствия:**
- **Planner LLM** не может выбрать `target` по тексту, которого не видит VL. Кликнет «в никуда».
- **Chat LLM** не может ответить пользователю «в поле написано X», если X не распознан.

**Downscaled PNG не помогает:** `ScreenshotAsync` уменьшает 1920×1200 → 1024×640. Шрифт 8 px становится 4.3 px — Tesseract тоже не распознает.

### § 1.2. Цели

| # | Цель | Метрика |
|---|---|---|
| 1 | Planner получает **точный текст** кнопок / полей / ссылок | `ui_elements[].label` обогащён OCR-словами (`Source="ocr"|"merged"`) |
| 2 | Chat LLM получает **общий текст** со экрана | `ScreenDescriptionDto.OcrText` + `VisionTaskResultDto.OcrText` заполнены |
| 3 | **Graceful fallback** — если OCR не готов / не сработал | `IsReady=false` → OCR не запускается, поведение = v1.13.1 |
| 4 | **Не ломает существующее** | 0 изменений в smoke-сценариях без OCR |
| 5 | **Всё конфигурируемо** (RULES: уходим от хардкода) | Секция `VisionAgent:Ocr:*` |

### § 1.3. Что НЕ входит в KI-137

- **KI-138** — PII masking (blur регионов до VL, отдельный KI).
- **KI-139 / KI-141** — External VL providers (multimodal).
- **KI-163** — Set-of-Mark (Tesseract-based детектор).
- **OCR-first для текстовых элементов** (вариант C из обсуждения) — дороже (N VL-вызовов).
- **Shadow-DOM / iframe / canvas** — OCR не помогает; правильный инструмент — CDP (KI-161).

### § 1.4. Индустрия

OCR-поверх-скриншота — стандарт в:

- **Anthropic Computer Use** — screenshot + OCR + multimodal.
- **Playwright + accessibility tree** — DOM tree как fallback.
- **PaddleOCR + RPA** (UiPath, Automation Anywhere) — промышленный стандарт.

**Tesseract** — Apache 2.0, .NET-обёртка (`Tesseract 5.2.0`), уже используется в проекте (**KI-203**, `TesseractOcrService` для PDF-сканов).

### § 1.5. Что уже есть в проекте

- **`IOcrService`** (`Interfaces/`) — `RecognizeAsync` (текст), `RecognizeWithLayoutAsync` (слова + bbox).
- **`TesseractOcrService`** (`Implementation/Rag/`) — Singleton, `Lazy<TesseractEngine>`, thread-safe через `lock`.
- **`PageTextLayerDto`** / **`WordBoxDto`** — координаты в natural PNG pixels, top-left origin, y↓.
- **`OcrOptions`** — секция `Rag:Ingestion:Ocr` (для PDF).
- **`LocalHarnessVisionBackend.ScreenshotAsync`** — GDI + downscale.
- **`VisionImageResizer.Resize`** / **`CalculateTargetSize`** — downscale PNG.
- **`VisionAgentService.RunTaskAsync`** — основной loop.

---

## § 2. Решение

### § 2.1. Архитектура

Поток данных:

    VisionAgentService.RunTaskAsync (main loop)
    │
    ├── ocrWasRun = false
    ├── forceOcrOnNextDescribe = false
    ├── ocrEnabled = Ocr.Enabled && _ocr.IsReady
    │
    └── for step in 1..maxSteps:
          png = backend.ScreenshotAsync()        ← downscaled PNG
          screen = visionLlm.DescribeAsync(png)  ← VL-описание
          lastScreen = screen

          ── Trigger A + reactive flag ──
          if ocrEnabled && !ocrWasRun:
              if ShouldRunOcrA(screen) || forceOcrOnNextDescribe:
                  screen = EnrichWithOcrAsync(screen)
                  ocrWasRun = true
          forceOcrOnNextDescribe = false

          action = plannerLlm.PlanNextAsync(screen)
          ... validate ...

          ── Trigger B ──
          if action == "fail":
              if ocrEnabled && !ocrWasRun && Ocr.TriggerB:
                  forceOcrOnNextDescribe = true
                  continue      ← next iteration сделает OCR
              result.Error = action.Reason
              break

          ... execute action ...

    result.OcrText = lastScreen?.OcrText

И внутри `EnrichWithOcrAsync(screen)`:

    EnrichWithOcrAsync(screen)
    │
    ├── 1. fullPng = backend.ScreenshotFullResolutionAsync()
    │      → FullResolutionScreenshotDto { Png, Width, Height }
    │
    ├── 2. ocrLayer = _ocr.RecognizeWithLayoutAsync(
    │          fullPng.Png, fullPng.Width, fullPng.Height)
    │      → PageTextLayerDto { Width, Height, Words: [{Text,X,Y,W,H}] }
    │
    ├── 3. (scaleX, scaleY) = backend.GetScreenshotScale()
    │      full-res → screenshot-space: x_shot = x_ocr / scaleX
    │
    ├── 4. merged = OcrVlMergeHelper.Merge(
    │          screen, ocrLayer, scaleX, scaleY, MergeMaxDistancePx)
    │      • UiElementDto.Label обновлён (matched words, склейка " ")
    │      • UiElementDto.Source = "ocr" | "merged" | "vl"
    │      • ScreenDescriptionDto.OcrText заполнен
    │      • ScreenDescriptionDto.OcrWordsCount заполнен
    │
    └── 5. return merged

### § 2.2. Ключевые решения

1. **Full-resolution PNG для OCR.** Downscaled 1024×640 не годится — 8-10 px шрифты станут 4-5 px. Новый метод `ScreenshotFullResolutionAsync()` в `IVisionBackend` (default → `null`).

2. **OCR запускается на следующем шаге после `fail`.** Не вложенный цикл, не повторный `PlanNext` в том же шаге. Проще: `fail` → флаг `forceOcrOnNextDescribe` → next iteration.

3. **Merge через центр bbox, не IoU.** VL-bbox грубый (±20-30 px), IoU низкий даже для одного элемента. Центр OCR-слова ближе `MergeMaxDistancePx` к центру VL-элемента → принадлежит ему.

4. **Не матчившиеся OCR-слова в `ui_elements` не попадают.** Planner'у 100+ слов не нужны — раздуют prompt (уже есть `MaxElements=8` в `ScreenDescriptionParser`). Сохраняются в `ScreenDescriptionDto.OcrText`.

5. **Один OCR-проход на задачу.** Флаг `ocrWasRun` — второй `fail` уже не запускает OCR.

6. **Все триггеры и пороги — в конфиге** (`VisionAgent:Ocr:*`). Хардкод — только безопасные default'ы (`Enabled=false` в prod).

7. **Своя секция `VisionAgent:Ocr`.** Не переиспользуем `Rag:Ingestion:Ocr` — там `RenderDpi` / `MinTextCharsPerPage` про PDF-страницы, семантика другая.

8. **Graceful fallback.** `IsReady=false` / `Enabled=false` / OCR упал / backend вернул `null` → `screen` без изменений.

### § 2.3. Триггеры

**Trigger A (проактивный):**

VL вернул `ui_elements`, где:
- список пуст (`Count == 0`) **ИЛИ**
- хотя бы один label пустой / короче `ShortLabelThreshold`.

`ShortLabelThreshold` — конфиг, default `3`. Пустой `ui_elements` — тоже триггер (OCR даст хоть что-то для Planner'а).

**Trigger B (реактивный):**

Planner вернул `action="fail"` **И** OCR ещё не запускался в этой задаче.

Ключевые слова не ищем — qwen3-4b вариативна («не вижу текста» / «не могу найти» / «недоступно»). Просто `fail`.

**Ограничение:** OCR запускается **один раз** за задачу. Второй `fail` — уже `break`.

---

## § 3. Архитектура: файлы

### § 3.1. Новые файлы

**DTO** (`IIChatTools.Services/DTO/VisionAgent/`):

| Файл | Назначение |
|---|---|
| `FullResolutionScreenshotDto.cs` | PNG + width + height (возврат `ScreenshotFullResolutionAsync`) |
| `VisionOcrOptions.cs` | Секция `VisionAgent:Ocr` |

**Implementation** (`IIChatTools.Services/Implementation/VisionAgent/`):

| Файл | Назначение |
|---|---|
| `OcrVlMergeHelper.cs` | Static helper: merge `PageTextLayerDto` в `ScreenDescriptionDto` |

**Документация**:

| Файл | Назначение |
|---|---|
| `docs/development/v1.13/DESIGN_VISION_OCR.md` | Этот документ |

### § 3.2. Изменяемые файлы

| Файл | Что меняется |
|---|---|
| `Interfaces/IVisionBackend.cs` | +`ScreenshotFullResolutionAsync()` (default → `null`) |
| `Implementation/VisionAgent/LocalHarnessVisionBackend.cs` | Override `ScreenshotFullResolutionAsync` — GDI full-res без downscale |
| `DTO/VisionAgent/UiElementDto.cs` | +`Source` (string: `"vl"` default / `"ocr"` / `"merged"`) |
| `DTO/VisionAgent/ScreenDescriptionDto.cs` | +`OcrText` (string, nullable) + `OcrWordsCount` (int) |
| `DTO/VisionAgent/VisionAgentOptions.cs` | +`Ocr` (секция `VisionOcrOptions`) |
| `DTO/VisionAgent/VisionTaskResultDto.cs` | +`OcrText` (string, nullable) — для Chat LLM |
| `Implementation/VisionAgent/VisionAgentService.cs` | Loop: триггеры A/B, `ShouldRunOcrA`, `EnrichWithOcrAsync`, `+IOcrService` в конструктор |
| `IIChatTools.API/appsettings.json` + `.Development.json` | +`VisionAgent:Ocr` |
| `IIChatTools.Tests/Fakes/FakeVisionBackend.cs` | +override `ScreenshotFullResolutionAsync` |
| `IIChatTools.Tests/UnitTests/VisionAgent/OcrVlMergeHelperTests.cs` | Новый файл (unit-тесты) |
| `CHANGELOG.md`, `KNOWN_ISSUES.md`, `README.md` | После реализации |

---

## § 4. Контракты

### § 4.1. `IVisionBackend` — новый default-метод

```csharp
public interface IVisionBackend : IAsyncDisposable
{
    // ... существующие 11 методов ...

    /// <summary>
    /// Full-resolution скриншот экрана — без downscale. Нужен для OCR мелкого
    /// текста (8-10 px шрифты не видны на downscaled 1280×720).
    ///
    /// <para>
    /// <b>Default:</b> <c>null</c> — backend не поддерживает full-res
    /// (OCR будет пропущен). Backend'ы без поддержки могут не переопределять.
    /// </para>
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// <c>null</c> — не поддерживается. Иначе — PNG + размеры в пикселях
    /// (natural resolution, без downscale).
    /// </returns>
    Task<FullResolutionScreenshotDto> ScreenshotFullResolutionAsync(
        CancellationToken ct = default)
        => Task.FromResult<FullResolutionScreenshotDto>(null);
}
```

**RULES § 4.46:** default interface method — вызывать через интерфейсную переменную. `VisionAgentService` получает `_backend` как `IVisionBackend` — ок.

**RULES § 4.34:** существующие fakes (`FakeVisionBackend`) не сломаются — default вернёт `null`. Но для unit-тестов OCR нужно override'ить. Заведён отдельный fake.

### § 4.2. `FullResolutionScreenshotDto`

```csharp
namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Full-resolution скриншот (KI-137): PNG + размеры в пикселях.
    /// Используется OCR'ом (для распознавания мелкого текста).
    /// </summary>
    /// <remarks>
    /// v1.13.x (KI-137). См. DESIGN_VISION_OCR.md § 4.2.
    /// </remarks>
    public sealed class FullResolutionScreenshotDto
    {
        /// <summary>PNG-байты (полное разрешение экрана).</summary>
        public byte[] Png { get; set; }

        /// <summary>Ширина PNG в пикселях.</summary>
        public int Width { get; set; }

        /// <summary>Высота PNG в пикселях.</summary>
        public int Height { get; set; }
    }
}
```

### § 4.3. `UiElementDto` — новое поле `Source`

Точечный diff:

```
Найти (перед `public UiElementBoundsDto Bounds { get; set; }`):

        /// <summary>Прямоугольник элемента на скриншоте (для валидации координат).</summary>
        public UiElementBoundsDto Bounds { get; set; }

Заменить на:

        /// <summary>
        /// Источник данных об этом элементе (KI-137):
        /// <list type="bullet">
        ///   <item><c>"vl"</c> (default) — только Vision LLM;</item>
        ///   <item><c>"ocr"</c> — label добавлен OCR (у VL был пустой label);</item>
        ///   <item><c>"merged"</c> — label обновлён OCR (у VL был короткий label).</item>
        /// </list>
        /// </summary>
        public string Source { get; set; } = "vl";

        /// <summary>Прямоугольник элемента на скриншоте (для валидации координат).</summary>
        public UiElementBoundsDto Bounds { get; set; }
```

### § 4.4. `ScreenDescriptionDto` — новые поля

Точечный diff:

```
Найти:

        public List<UiElementDto> UiElements { get; set; } = new List<UiElementDto>();
    }

Заменить на:

        public List<UiElementDto> UiElements { get; set; } = new List<UiElementDto>();

        /// <summary>
        /// Общий текст со всего экрана (KI-137), распознанный OCR.
        /// Строки соединены через <c>\n</c>, порядок — сверху вниз.
        /// Может быть <c>null</c>, если OCR не запускался.
        /// Используется Chat LLM для финального ответа пользователю.
        /// </summary>
        public string OcrText { get; set; }

        /// <summary>
        /// Количество слов, распознанных OCR (KI-137).
        /// 0 — OCR не запускался / не нашёл слов.
        /// </summary>
        public int OcrWordsCount { get; set; }
    }
```

### § 4.5. `VisionTaskResultDto` — новое поле

Точечный diff (последнее поле в файле — `Base64`):

```
Найти (конец класса VisionTaskResultDto):

        public string Base64 { get; set; }
    }

Заменить на:

        public string Base64 { get; set; }

        /// <summary>
        /// OCR-текст последнего экрана (KI-137). Chat LLM использует для
        /// финального ответа. <c>null</c> — OCR не запускался.
        /// </summary>
        public string OcrText { get; set; }
    }
```

### § 4.6. `VisionOcrOptions`

```csharp
namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Настройки OCR-fallback в Vision Agent (секция <c>VisionAgent:Ocr</c>).
    /// </summary>
    /// <remarks>
    /// v1.13.x (KI-137). См. DESIGN_VISION_OCR.md § 4.6.
    /// </remarks>
    public class VisionOcrOptions
    {
        /// <summary>
        /// Включён ли OCR-fallback в Vision Agent.
        /// Default: <c>true</c> в dev, <c>false</c> в prod.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>Путь к tessdata (относительный — от ContentRoot).</summary>
        public string TessDataPath { get; set; } = "tools/tessdata";

        /// <summary>Языки (Tesseract формат: <c>rus+eng</c>).</summary>
        public string Languages { get; set; } = "rus+eng";

        /// <summary>
        /// Trigger A — проактивный. Запускать OCR, если пустой <c>ui_elements</c>
        /// или хотя бы один label короче <see cref="ShortLabelThreshold"/>.
        /// Default: <c>true</c>.
        /// </summary>
        public bool TriggerA { get; set; } = true;

        /// <summary>
        /// Trigger B — реактивный. Запускать OCR, если Planner вернул
        /// <c>action="fail"</c> (один раз за задачу). Default: <c>true</c>.
        /// </summary>
        public bool TriggerB { get; set; } = true;

        /// <summary>Порог «короткого label», символов. Default: <c>3</c>.</summary>
        public int ShortLabelThreshold { get; set; } = 3;

        /// <summary>
        /// Максимальное расстояние (px в screenshot-space) между центром
        /// OCR-слова и центром VL-элемента для merge. Default: <c>30</c>.
        /// </summary>
        public int MergeMaxDistancePx { get; set; } = 30;
    }
}
```

### § 4.7. `VisionAgentOptions` — новое поле

Точечный diff:

```
Найти:

        public VisionCoordinateProviderOptions CoordinateProvider { get; set; }
            = new VisionCoordinateProviderOptions();
    }

Заменить на:

        public VisionCoordinateProviderOptions CoordinateProvider { get; set; }
            = new VisionCoordinateProviderOptions();

        /// <summary>Конфигурация OCR-fallback (KI-137).</summary>
        public VisionOcrOptions Ocr { get; set; } = new VisionOcrOptions();
    }
```

### § 4.8. `OcrVlMergeHelper` — новый файл

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Merge OCR-слов в VL-описание (KI-137).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Стратегия: матчить OCR-слова с <c>ui_elements</c> VL через центр bbox
    /// (расстояние ≤ <c>maxDistancePx</c>). Не матчившиеся слова в
    /// <c>ui_elements</c> не попадают, но сохраняются в
    /// <see cref="ScreenDescriptionDto.OcrText"/>.
    /// </para>
    /// <para>
    /// <b>Source</b> элемента:
    /// <list type="bullet">
    ///   <item><c>"ocr"</c> — VL не дал label, OCR дал;</item>
    ///   <item><c>"merged"</c> — VL дал короткий label, OCR обогатил;</item>
    ///   <item><c>"vl"</c> (default) — не трогали.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Static, без состояния.</b> Тестируется без DI.
    /// </para>
    /// </remarks>
    public static class OcrVlMergeHelper
    {
        /// <summary>
        /// Обогащает VL-описание OCR-словами.
        /// </summary>
        /// <param name="screen">VL-описание. Мутируется.</param>
        /// <param name="ocrLayer">Результат OCR (координаты в full-res PNG).</param>
        /// <param name="screenshotScaleX">Коэффициент full-res / screenshot.</param>
        /// <param name="screenshotScaleY">Коэффициент full-res / screenshot.</param>
        /// <param name="maxDistancePx">
        /// Максимальное расстояние (screenshot-space) для merge. Default 30.
        /// </param>
        /// <returns>Тот же объект <paramref name="screen"/> (мутирован).</returns>
        public static ScreenDescriptionDto Merge(
            ScreenDescriptionDto screen,
            PageTextLayerDto ocrLayer,
            double screenshotScaleX,
            double screenshotScaleY,
            int maxDistancePx)
        {
            // 1. Защита от null.
            if (screen == null) screen = new ScreenDescriptionDto();

            if (ocrLayer?.Words == null || ocrLayer.Words.Count == 0)
            {
                screen.OcrWordsCount = 0;
                return screen;
            }

            // 2. Конвертация OCR-слов из full-res в screenshot-space.
            var scaleX = screenshotScaleX > 0 ? screenshotScaleX : 1.0;
            var scaleY = screenshotScaleY > 0 ? screenshotScaleY : 1.0;

            var wordsInShotSpace = ocrLayer.Words
                .Where(w => !string.IsNullOrWhiteSpace(w?.Text))
                .Select(w => new OcrWord
                {
                    Text = w.Text,
                    X = w.X / scaleX,
                    Y = w.Y / scaleY,
                    W = w.W / scaleX,
                    H = w.H / scaleY
                })
                .ToList();

            screen.OcrWordsCount = wordsInShotSpace.Count;

            // 3. OcrText — общий текст со экрана.
            screen.OcrText = BuildFullText(wordsInShotSpace);

            // 4. Нет VL-элементов — матчить не с чем.
            if (screen.UiElements == null || screen.UiElements.Count == 0)
            {
                return screen;
            }

            // 5. Матчинг: для каждого OCR-слова — ближайший VL-элемент.
            var wordsByElement = new Dictionary<UiElementDto, List<OcrWord>>();

            foreach (var word in wordsInShotSpace)
            {
                var wordCx = word.X + word.W / 2;
                var wordCy = word.Y + word.H / 2;

                UiElementDto bestElement = null;
                var bestDist = double.MaxValue;

                foreach (var el in screen.UiElements)
                {
                    if (el.Center == null) continue;
                    var dx = el.Center.X - wordCx;
                    var dy = el.Center.Y - wordCy;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestElement = el;
                    }
                }

                if (bestElement == null || bestDist > maxDistancePx) continue;

                if (!wordsByElement.TryGetValue(bestElement, out var list))
                {
                    list = new List<OcrWord>();
                    wordsByElement[bestElement] = list;
                }
                list.Add(word);
            }

            // 6. Обновление Label / Source.
            foreach (var kvp in wordsByElement)
            {
                var el = kvp.Key;
                var words = kvp.Value;

                var text = string.Join(" ", words
                    .OrderBy(w => w.Y)
                    .ThenBy(w => w.X)
                    .Select(w => w.Text));

                if (string.IsNullOrWhiteSpace(text)) continue;

                var hadLabel = !string.IsNullOrWhiteSpace(el.Label);
                el.Label = text.Trim();
                el.Source = hadLabel ? "merged" : "ocr";
            }

            return screen;
        }

        /// <summary>
        /// Собирает единый текст из OCR-слов: строки по overlap y-диапазона,
        /// внутри строки — сортировка по x.
        /// </summary>
        private static string BuildFullText(List<OcrWord> words)
        {
            if (words == null || words.Count == 0) return null;

            var sorted = words
                .OrderBy(w => w.Y)
                .ThenBy(w => w.X)
                .ToList();

            var lines = new List<List<OcrWord>>();
            const double LineTolerance = 5.0;   // px

            foreach (var w in sorted)
            {
                var line = lines.LastOrDefault();
                if (line != null)
                {
                    var lineY = line.Average(l => l.Y + l.H / 2);
                    var wordY = w.Y + w.H / 2;
                    if (Math.Abs(lineY - wordY) <= LineTolerance)
                    {
                        line.Add(w);
                        continue;
                    }
                }

                lines.Add(new List<OcrWord> { w });
            }

            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var lineText = string.Join(" ", line
                    .OrderBy(w => w.X)
                    .Select(w => w.Text));
                sb.AppendLine(lineText);
            }

            return sb.ToString().TrimEnd();
        }

        private sealed class OcrWord
        {
            public string Text { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }
        }
    }
}
```

---

## § 5. Изменения в `LocalHarnessVisionBackend`

### § 5.1. `ScreenshotFullResolutionAsync` — новый метод

```csharp
/// <inheritdoc />
/// <remarks>
/// v1.13.x (KI-137): GDI-захват экрана БЕЗ downscale — для OCR мелкого текста.
/// Дублирует логику <see cref="ScreenshotAsync"/> до вызова
/// <c>VisionImageResizer.Resize</c>.
/// </remarks>
public Task<FullResolutionScreenshotDto> ScreenshotFullResolutionAsync(
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    var width = GetSystemMetrics(SM_CXSCREEN);
    var height = GetSystemMetrics(SM_CYSCREEN);
    if (width <= 0 || height <= 0)
    {
        throw new InvalidOperationException(
            $"Некорректные размеры экрана: {width}×{height}.");
    }

    byte[] pngBytes;
    using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
    {
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(
                sourceX: 0, sourceY: 0,
                destinationX: 0, destinationY: 0,
                blockRegionSize: new Size(width, height),
                copyPixelOperation: CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        pngBytes = ms.ToArray();
    }

    _logger.LogDebug(
        "VisionAgent: full-res screenshot {W}×{H}, {Bytes} байт",
        width, height, pngBytes.Length);

    return Task.FromResult(new FullResolutionScreenshotDto
    {
        Png = pngBytes,
        Width = width,
        Height = height
    });
}
```

**Проверка лимита:** `MaxScreenshotBytes` (default 2 MB) **не применяется** к full-res — это внутренний PNG для OCR, не уходит в VL. Если нужно ограничить — новый параметр `VisionAgent:Limits:MaxFullResolutionBytes`. Пока оставим без лимита (GDI-скриншот 1920×1200 ≈ 200-400 KB PNG).

---

## § 6. Изменения в `VisionAgentService`

### § 6.1. Конструктор — `+IOcrService`

Точечный diff:

```
Найти:

        private readonly IVisionOverlayLauncher _overlayLauncher;
        private readonly VisionAgentOptions _options;
        private readonly ILogger<VisionAgentService> _logger;

Заменить на:

        private readonly IVisionOverlayLauncher _overlayLauncher;
        private readonly IOcrService _ocr;                 // ← НОВОЕ (KI-137)
        private readonly VisionAgentOptions _options;
        private readonly ILogger<VisionAgentService> _logger;
```

В сигнатуре конструктора:

```
Найти:

            IVisionOverlayLauncher overlayLauncher,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionAgentService> logger)

Заменить на:

            IVisionOverlayLauncher overlayLauncher,
            IOcrService ocr,                                    // ← НОВОЕ (KI-137)
            IOptions<VisionAgentOptions> options,
            ILogger<VisionAgentService> logger)
```

В теле (перед `_options = options.Value;`):

```csharp
            _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
```

### § 6.2. `RunTaskAsync` — флаги перед loop'ом

Точечный diff:

```
Найти:

            var maxSteps = request.MaxSteps.HasValue && request.MaxSteps.Value > 0
                ? Math.Min(request.MaxSteps.Value, _options.Limits.MaxSteps)
                : _options.Limits.MaxSteps;

            var history = new List<VisionStepDto>();

Заменить на:

            var maxSteps = request.MaxSteps.HasValue && request.MaxSteps.Value > 0
                ? Math.Min(request.MaxSteps.Value, _options.Limits.MaxSteps)
                : _options.Limits.MaxSteps;

            // KI-137: флаги OCR (один запуск на задачу).
            var ocrEnabled = _options.Ocr?.Enabled == true && _ocr.IsReady;
            var ocrWasRun = false;
            var forceOcrOnNextDescribe = false;
            ScreenDescriptionDto lastScreen = null;

            var history = new List<VisionStepDto>();
```

### § 6.3. В loop'е — триггер A

Точечный diff (после блока `4.2. Описание экрана`, перед `4.3. Следующее действие`):

```
Найти:

                    // 4.3. Следующее действие.
                    VisionActionDto action;

Заменить на:

                    // KI-137: сохраняем последний screen — для result.OcrText.
                    lastScreen = screen;

                    // 4.2.1. KI-137: OCR Trigger A (проактивный) + reactive flag (Trigger B).
                    if (ocrEnabled && !ocrWasRun)
                    {
                        var shouldRunA = _options.Ocr.TriggerA && ShouldRunOcrA(screen);
                        if (shouldRunA || forceOcrOnNextDescribe)
                        {
                            screen = await EnrichWithOcrAsync(screen, effectiveCts.Token)
                                .ConfigureAwait(false);
                            ocrWasRun = true;

                            _logger.LogInformation(
                                "VisionAgent[{TaskId}]: OCR запущен (TriggerA={A}, reactive={R}), " +
                                "words={Words}, ui_elements={Ui}",
                                taskId, shouldRunA, forceOcrOnNextDescribe,
                                screen.OcrWordsCount,
                                screen.UiElements?.Count ?? 0);
                        }
                    }
                    forceOcrOnNextDescribe = false;

                    // 4.3. Следующее действие.
                    VisionActionDto action;
```

### § 6.4. В loop'е — триггер B

Точечный diff (в блоке `4.6. done / fail`):

```
Найти:

                    if (actionType == "fail")
                    {
                        result.Success = false;
                        result.Error = string.IsNullOrWhiteSpace(effectiveAction.Reason)
                            ? "Planner LLM вернула fail."
                            : effectiveAction.Reason;
                        break;
                    }

Заменить на:

                    if (actionType == "fail")
                    {
                        // KI-137: OCR Trigger B (реактивный).
                        // Не break'аем сразу — если OCR ещё не запускался,
                        // устанавливаем флаг и продолжаем loop. Следующая
                        // итерация сделает OCR перед PlanNextAsync.
                        if (ocrEnabled && !ocrWasRun && _options.Ocr.TriggerB)
                        {
                            forceOcrOnNextDescribe = true;
                            _logger.LogInformation(
                                "VisionAgent[{TaskId}]: Planner вернул fail " +
                                "(reason='{Reason}'). Следующий шаг — OCR (KI-137, TriggerB).",
                                taskId,
                                string.IsNullOrWhiteSpace(effectiveAction.Reason)
                                    ? "(пусто)" : effectiveAction.Reason);
                            continue;
                        }

                        result.Success = false;
                        result.Error = string.IsNullOrWhiteSpace(effectiveAction.Reason)
                            ? "Planner LLM вернула fail."
                            : effectiveAction.Reason;
                        break;
                    }
```

### § 6.5. `result.OcrText` в `finally`

Точечный diff (в `finally` блоке, до `_logger.LogInformation("...завершено...")`):

```
Найти:

                _logger.LogInformation(
                    "VisionAgent[{TaskId}]: завершено (success={Success}, steps={Steps}, ms={Ms}): {Error}",
                    taskId, result.Success, history.Count, result.TotalDurationMs,
                    result.Error ?? "(нет)");

Заменить на:

                // KI-137: пробрасываем OCR-текст в результат — Chat LLM
                // использует его для финального ответа.
                result.OcrText = lastScreen?.OcrText;

                _logger.LogInformation(
                    "VisionAgent[{TaskId}]: завершено (success={Success}, steps={Steps}, ms={Ms}): {Error}",
                    taskId, result.Success, history.Count, result.TotalDurationMs,
                    result.Error ?? "(нет)");
```

### § 6.6. Новые приватные методы

```csharp
/// <summary>
/// KI-137: проверка Trigger A — нужно ли запустить OCR.
/// <list type="bullet">
///   <item><c>ui_elements</c> пуст → true;</item>
///   <item>хотя бы один label пустой / короче <c>ShortLabelThreshold</c> → true.</item>
/// </list>
/// </summary>
private bool ShouldRunOcrA(ScreenDescriptionDto screen)
{
    if (screen == null) return false;
    if (screen.UiElements == null || screen.UiElements.Count == 0) return true;

    var threshold = Math.Max(1, _options.Ocr?.ShortLabelThreshold ?? 3);
    return screen.UiElements.Any(el =>
        string.IsNullOrWhiteSpace(el.Label) ||
        el.Label.Trim().Length < threshold);
}

/// <summary>
/// KI-137: делает full-res скриншот, запускает OCR, merge'ит в VL-описание.
/// При любой ошибке возвращает исходный <paramref name="screen"/> без изменений.
/// </summary>
private async Task<ScreenDescriptionDto> EnrichWithOcrAsync(
    ScreenDescriptionDto screen,
    CancellationToken ct)
{
    try
    {
        var fullPng = await _backend
            .ScreenshotFullResolutionAsync(ct)
            .ConfigureAwait(false);

        if (fullPng == null || fullPng.Png == null || fullPng.Png.Length == 0)
        {
            _logger.LogDebug(
                "VisionAgent: ScreenshotFullResolutionAsync вернул null — OCR пропущен");
            return screen;
        }

        var ocrLayer = await _ocr
            .RecognizeWithLayoutAsync(fullPng.Png, fullPng.Width, fullPng.Height, ct)
            .ConfigureAwait(false);

        var (scaleX, scaleY) = _backend.GetScreenshotScale();
        var maxDist = Math.Max(1, _options.Ocr?.MergeMaxDistancePx ?? 30);

        var merged = OcrVlMergeHelper.Merge(screen, ocrLayer, scaleX, scaleY, maxDist);

        var matchedCount = merged.UiElements?
            .Count(el => string.Equals(el.Source, "ocr", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(el.Source, "merged", StringComparison.OrdinalIgnoreCase)) ?? 0;

        _logger.LogInformation(
            "VisionAgent: OCR завершён — {Words} слов, {Matched} элементов обогащено, " +
            "scale=({Sx:F3}, {Sy:F3})",
            merged.OcrWordsCount, matchedCount, scaleX, scaleY);

        return merged;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "VisionAgent: OCR Enrich упал — продолжаем с VL-описанием");
        return screen;
    }
}
```

---

## § 7. Изменения в `Startup.cs` (DI)

**Ничего нового регистрировать не нужно:** `IOcrService` → `TesseractOcrService` уже зарегистрирован как Singleton (для KI-203). `VisionAgentService` (Scoped) получает его через конструктор — DI-граф валиден (Singleton в Scoped).

**RULES § 4.51:** проверка DI-циклов — `VisionAgentService` → `IOcrService` (Singleton) → `IAppPathProvider` (Singleton). Циклов нет.

Однако — **`IOcrService` регистрируется безусловно**, вне `if (VisionAgent:Enabled)`. Это ок: OCR нужен и для RAG (KI-203). `VisionAgent` использует его через `Ocr.Enabled` (своя секция).

---

## § 8. Конфигурация

### § 8.1. `appsettings.json` (prod)

Точечный diff (в секции `VisionAgent`, после `Privacy`):

```
Найти:

        "Privacy": {
            "PersistScreenshots": false,
            "SaveToWorkspace": true,
            "MaskUrlBar": false,
            "DisableOverlayMask": true
        }
    },

Заменить на:

        "Privacy": {
            "PersistScreenshots": false,
            "SaveToWorkspace": true,
            "MaskUrlBar": false,
            "DisableOverlayMask": true
        },
        "Ocr": {
            "Enabled": false,
            "TessDataPath": "tools/tessdata",
            "Languages": "rus+eng",
            "TriggerA": true,
            "TriggerB": true,
            "ShortLabelThreshold": 3,
            "MergeMaxDistancePx": 30
        }
    },
```

### § 8.2. `appsettings.Development.json`

```
Найти:

        "Privacy": {
            "PersistScreenshots": false,
            "SaveToWorkspace": true,
            "MaskUrlBar": false,
            "DisableOverlayMask": true
        }
    },

Заменить на:

        "Privacy": {
            "PersistScreenshots": false,
            "SaveToWorkspace": true,
            "MaskUrlBar": false,
            "DisableOverlayMask": true
        },
        "Ocr": {
            "Enabled": true,
            "TessDataPath": "tools/tessdata",
            "Languages": "rus+eng",
            "TriggerA": true,
            "TriggerB": true,
            "ShortLabelThreshold": 3,
            "MergeMaxDistancePx": 30
        }
    },
```

---

## § 9. Безопасность / ограничения

### § 9.1. Что добавляется

- **OCR на full-res PNG.** Время: ~200-500 мс для 1920×1200 (rus+eng, 1 проход).
- **+1 проход OCR на задачу.** Флаг `ocrWasRun` ограничивает.
- **Singleton `TesseractEngine`, `lock`.** Thread-safe уже реализован (KI-203).

### § 9.2. Новые ограничения

| Ограничение | Митигация |
|---|---|
| tessdata нет / Linux native lib | `IsReady = false` → OCR пропущен |
| Плотный мелкий текст | Tesseract 70-90% точности — лучше, чем ничего |
| Canvas / WebGL (нет текста) | OCR бесполезен, но и не вреден (Trigger B + fail) |
| Кириллица | `rus.traineddata` уже есть (RAG-OCR) |
| Ошибка merge (слова попали не в тот элемент) | `MergeMaxDistancePx` конфигурируем, default 30 px консервативен |
| Ложные срабатывания OCR (шум) | `Source` в `ui_elements` показывает происхождение — Planner может игнорировать |

### § 9.3. Что НЕ ломается

- **Downscaled PNG для VL** — не меняется.
- **CDP-attach (KI-161)** — не конфликтует (разные слои: DOM-координаты vs текст).
- **RAG-OCR (KI-203)** — отдельная секция, отдельная логика.
- **Verify (KI-162)** / **bounds-center (KI-190)** — не трогаем.

### § 9.4. Сводная таблица

| Угроза | Митигация |
|---|---|
| OCR-слова «прилипают» к неверному элементу | `MergeMaxDistancePx` (30 px); VL-center vs OCR-center |
| OCR-текст попадает в Chat LLM — утечка? | Нет: `OcrText` — тот же экран, что VL видел. Ничего нового |
| OCR «тормозит» loop на слабой машине | 200-500 мс на full-res 1920×1200, один раз за задачу |
| OCR падает / timeout | `catch` в `EnrichWithOcrAsync` → работаем без OCR |

---

## § 10. План фаз

| Фаза | Что | Оценка |
|---|---|---|
| **Ф1** | DTO: `FullResolutionScreenshotDto`, `VisionOcrOptions`; поля `UiElementDto.Source`, `ScreenDescriptionDto.OcrText/OcrWordsCount`, `VisionTaskResultDto.OcrText`, `VisionAgentOptions.Ocr` | ~20 мин |
| **Ф2** | `IVisionBackend.ScreenshotFullResolutionAsync()` (default → `null`); override в `LocalHarnessVisionBackend` | ~30 мин |
| **Ф3** | `OcrVlMergeHelper` (+ `BuildFullText`, `OcrWord` internal) | ~40 мин |
| **Ф4** | `VisionAgentService`: `+IOcrService`, флаги, `ShouldRunOcrA`, `EnrichWithOcrAsync`, Trigger A/B в loop'е, `lastScreen` + `result.OcrText` | ~30 мин |
| **Ф5** | Fakes: `FakeVisionBackend.ScreenshotFullResolutionAsync` (настраиваемый PNG) + обновить `VisionAgentServiceTests` (конструктор) | ~20 мин |
| **Ф6** | Конфиг в `appsettings.json` + `.Development.json` | ~10 мин |
| **Ф7** | Тесты: `OcrVlMergeHelperTests` (unit, ~8-10 кейсов) | ~40 мин |
| **Ф8** | Smoke + docs (README, CHANGELOG, KNOWN_ISSUES) + KI-137 → Fixed | ~45 мин |
| **Итого** | | **~3.5 ч** |

---

## § 11. DoD (v1.13.x)

### § 11.1. Функциональные

- [ ] `ScreenshotFullResolutionAsync` возвращает PNG в natural resolution (без downscale).
- [ ] `VisionAgent:Ocr:Enabled=true` + `IsReady=true` → OCR запускается по Trigger A / B.
- [ ] `ScreenDescriptionDto.OcrText` заполнен; `UiElementDto.Source` = `"ocr"` / `"merged"` / `"vl"`.
- [ ] `VisionTaskResultDto.OcrText` содержит текст последнего экрана.
- [ ] OCR запускается **один раз** за задачу (`ocrWasRun`).
- [ ] `Enabled=false` → поведение идентично v1.13.1 (0 вызовов OCR).
- [ ] Linux / tessdata отсутствует → `IsReady=false`, OCR пропущен.

### § 11.2. Smoke (3 сценария)

1. **Портал Wikipedia** (страница с языковыми ссылками):
   - Задача: «Открой wikipedia.org и найди статью про Москву».
   - `Ocr.Enabled=true`, `TriggerA=true` → OCR запускается.
   - `screen.OcrText` содержит «Русский English Deutsch …».
   - `ui_elements[].label` для языковых ссылок обогащён (Source=`ocr`/`merged`).

2. **Форма с мелкими placeholder'ами:**
   - Задача: «Заполни форму: имя, email».
   - VL дал `input1`, `input2` (label пустой) → Trigger A.
   - OCR распознал «Ваше имя», «Email» → обновлены label'ы.

3. **OCR отключён (regression):**
   - `Ocr.Enabled=false` → 0 вызовов OCR, поведение v1.13.1.

### § 11.3. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — все существующие + новые.
- [ ] `OcrVlMergeHelperTests` — 8+ тестов:
  - `Merge_EmptyOcrLayer_NoChanges`
  - `Merge_EmptyScreen_FillsOcrText`
  - `Merge_MatchesByDistance_UpdatesLabel`
  - `Merge_ThresholdExceeded_NoMatch`
  - `Merge_EmptyLabel_SetsSourceOcr`
  - `Merge_ShortLabel_SetsSourceMerged`
  - `Merge_RespectsScreenshotScale`
  - `Merge_BuildFullText_MultipleLines`
- [ ] `FakeVisionBackend.ScreenshotFullResolutionAsync` возвращает настраиваемый PNG.
- [ ] `CHANGELOG.md` — запись в `[Unreleased]`.
- [ ] `README.md` — раздел Vision Agent: «OCR-fallback (v1.13.x, KI-137)».
- [ ] `KNOWN_ISSUES.md` — KI-137 → Fixed.
- [ ] `RULES.md` — если новые уроки (пока не ожидаются).

### § 11.4. Документация

- [ ] DESIGN_VISION_OCR.md (этот документ) — статус Draft → Implemented.
- [ ] `docs/KNOWN_ISSUES.md` — отметка KI-137.

---

## § 12. Ссылки

### § 12.1. KI

- **KI-137** (этот документ) — Vision Agent OCR-fallback.
- **KI-131** — Vision Agent (общий).
- **KI-161** — CDP-attach (не конфликтует, разные слои).
- **KI-162** — Coordinate-then-Verify (VL-координаты не улучшаются — OCR тоже не про координаты).
- **KI-190** — bounds-center (не меняется).
- **KI-203** — RAG OCR (общий сервис `IOcrService`, разные секции конфига).
- **KI-138** — PII masking (отдельная задача, критично для external VL KI-139).
- **KI-160** — VL-модель нестабильна (не решается OCR'ом).

### § 12.2. RULES

- **§ 4.34** — расширение интерфейса → grep по fake-заглушкам. `IVisionBackend` +1 default-метод — не ломает, но fakes проверить.
- **§ 4.46** — default interface method не виден через конкретный тип.
- **§ 4.51** — DI-циклы: `VisionAgentService` → `IOcrService` (Singleton) — цикла нет.

### § 12.3. Внешние источники

- **Tesseract.NET** — https://github.com/charlesw/tesseract
- **Anthropic Computer Use** — https://docs.anthropic.com/en/docs/agents-and-tools/computer-use
- **Tesseract PageIterator API** — https://github.com/charlesw/tesseract/wiki/PageIterator

### § 12.4. Внутренние

- `docs/development/v1.12/DESIGN_VISION_AGENT.md`
- `docs/development/v1.13/DESIGN_VISION_CDP_ATTACH.md`
- `docs/development/RULES.md`

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.13.1**