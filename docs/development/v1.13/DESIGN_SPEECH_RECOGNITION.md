# DESIGN — Распознавание речи в чате (Whisper.net, офлайн)

**Версия:** v1.13.1
**Дата:** 2026-10-04 (обновлено)
**Статус:** MVP Released (v1.13.1, fix1-fix11)
**Связанные документы:** [RULES.md](../../RULES.md) · [DESIGN v1.11 (Actor-Critic)](../v1.11/DESIGN_MULTI_AGENT_DEBATE.md) · [ARCHITECTURE.md](../../ARCHITECTURE.md) · KI-140 · KI-144 · KI-145 · KI-146

---

## § 0. Резюме

Добавляем **офлайн-распознавание речи** в Chat UI. Пользователь нажимает
🎤 → говорит → нажимает Stop → текст автоматически вставляется в `<textarea>`.
Вся обработка — **локально**, без интернета, без Python, без облачных API.

**Ключевые решения:**
- **Движок:** [Whisper.net](https://github.com/sandrohanea/whisper.net) — .NET-биндинги к `whisper.cpp` (C++). MIT.
- **Модель:** `ggml-base.bin` (~142 MB, sweet spot для русского).
- **Формат аудио:** WAV 16 kHz mono (клиент кодирует через Web Audio API).
- **Никаких внешних зависимостей** (ffmpeg / Python / NAudio) — только NuGet `Whisper.net` + native runtime.
- **Приватность:** аудио не покидает сервер; нет утечек в Google / OpenAI / etc.
- **Offline:** работает в РФ без VPN, без доступа к интернету.

### v1.13.1 — изменения после MVP (fix1-fix11)

MVP v1.13.0 прошёл реальные smoke-тесты (Chrome + Yandex Browser + Windows
+ Plantronics .Audio 478 USB), и в ходе них выявился ряд проблем в
браузерных API и UX. Все исправления — v1.13.1:

- **fix5-fix6:** захват PCM переписан с `MediaRecorder` + `ScriptProcessorNode`
  на `MediaStreamTrackProcessor` (WebCodecs). Chrome pruning-ил граф
  Web Audio даже с `destination` — `onaudioprocess` фирес, но буфер пустой.
- **fix5:** фильтр служебных маркеров Whisper (`[BLANK_AUDIO]`, `[музыка]`),
  `NoSpeechThreshold=0.85`, парсинг WAV-заголовка (`TryReadWavDurationMs`).
- **fix7:** логирование `label` + `deviceId` устройства + сообщение об
  ошибке с именем микрофона (диагностика virtual audio device).
- **fix8 (KI-145):** device picker в `/profile → 🎤 Аудио` + сохранение
  `deviceId` в `UserSettings` + fallback на системный default.
- **fix9 (KI-144):** warning при выборе virtual audio device
  (Steam Streaming / VB-Cable / VoiceMeeter / OBS Virtual Audio).
- **fix10:** VAD (auto-stop по тишине) + хоткей `Ctrl+Shift+Space`.
- **fix11:** VAD-параметры в `appsettings.json` + адаптивный порог тишины
  (не работает на тихих микрофонах с фиксированным `SilenceRms`).

Подробная таблица изменений — § 13.

---

## § 1. Контекст и мотивация

### § 1.1. Проблема

Сейчас ввод сообщений в чат — только клавиатура. Голосовой ввод (как в
ChatGPT / DeepSeek) значительно ускоряет работу и снижает нагрузку на
руки при длинных сообщениях.

### § 1.2. Почему офлайн (а не Web Speech API / OpenAI Whisper API)

| Вариант | Offline | Приватность | Работает в РФ | Стоимость |
|:---|:---:|:---:|:---:|:---:|
| **Web Speech API** (Chrome) | ❌ | ❌ (уходит в Google) | ❌ (нужен VPN) | Бесплатно |
| **OpenAI Whisper API** | ❌ | ❌ | ❌ | $0.006 / мин |
| **Whisper.net (наш выбор)** | ✅ | ✅ | ✅ | Бесплатно |

**Требование проекта:** «сервис должен работать и без интернета» (запрос
пользователя, 2026-10-02). Поэтому — только локальный движок.

### § 1.3. Почему Whisper.net (а не альтернативы)

| Библиотека | Движок | Зрелость | Скорость | .NET 10 |
|:---|:---|:---:|:---:|:---:|
| **Whisper.net** | whisper.cpp (C++) | ⭐⭐⭐ | Средняя | ✅ |
| FasterWhisper.NET | CTranslate2 (C++) | ⭐⭐ | Высокая | ✅ |
| LMSupply.Transcriber | ONNX Runtime | ⭐⭐ | Средняя | ✅ |

**Выбор — Whisper.net.** Причины:
1. **Зрелость** — самая проверенная библиотека, большое сообщество.
2. **Полный контроль** — управление моделью, стримингом, языком.
3. **Документация** — примеры для NAudio, Blazor, диаризации.
4. **Простая интеграция** — 1 NuGet + native runtime (без внешних CLI).

**Не LM Studio:** LM Studio не поддерживает STT-модели (только LLM + embeddings).

---

## § 2. Архитектура

### § 2.1. Диаграмма потока

```
┌──────────────────────────────────────────────────────────────┐
│  Browser /chat                                                │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 1. User нажимает 🎤                                      │ │
│  │ 2. getUserMedia({ audio: true }) — permission            │ │
│  │ 3. MediaRecorder → Blob (audio/webm;codecs=opus)         │ │
│  │ 4. User нажимает Stop                                    │ │
│  │ 5. blob.arrayBuffer() → AudioContext.decodeAudioData()   │ │
│  │ 6. encodeWav(audioBuffer) → WAV 16 kHz mono              │ │
│  │ 7. POST /api/speech/transcribe (multipart/form-data)     │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────┬───────────────────────────────────┘
                           │ HTTP multipart
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  IIChatTools.API / SpeechController                          │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 1. Валидация (size ≤ 10 MB, content-type audio/wav)      │ │
│  │ 2. if (!Speech:Enabled) → 503                            │ │
│  │ 3. ISpeechRecognitionService.TranscribeAsync(stream)     │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────┬───────────────────────────────────┘
                           │ DI
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  IIChatTools.Services / WhisperNetTranscriptionService       │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 1. WhisperFactory.FromPath("ggml-base.bin") — Singleton  │ │
│  │ 2. processor = factory.CreateBuilder()                   │ │
│  │                  .WithLanguage("ru").Build()             │ │
│  │ 3. await foreach (segment in processor.ProcessAsync(wav))│ │
│  │ 4. Собрать segments.Text → полный текст                  │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                           │
                           ▼
                    { text: "..." }
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  Browser /chat                                                │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ 8. textarea.value = text                                 │ │
│  │ 9. autoResizeTextarea() + focus                          │ │
│  │ 10. (опционально) — toast «Готово»                       │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### § 2.2. Ключевые решения

1. **Клиент кодирует WAV** — не сервер. Это устраняет необходимость в
   NAudio / ffmpeg на бэкенде. Web Audio API + ~40 строк JS — надёжно
   работает во всех современных браузерах.
2. **WhisperFactory — Singleton.** Модель (142 MB) загружается один раз
   при первом обращении, кэшируется на всё время жизни приложения.
3. **Processor создаётся на каждый запрос.** Это thread-safe паттерн
   Whisper.net: `factory.CreateBuilder()` — дешёвый, состояние
   процессора не переиспользуется.
4. **Язык** — `ru` по умолчанию (настройка в `appsettings.json`).
5. **No streaming.** Ждём окончания записи → отправляем разом. Streaming
   (реал-тайм) — Phase 4 (опционально).

---

## § 3. Backend

### § 3.1. NuGet-зависимости

Добавить в `Directory.Build.props`:

```xml
<WhisperNetVersion>1.8.1</WhisperNetVersion>
```

Добавить в `IIChatTools.Services.csproj`:

```xml
<PackageReference Include="Whisper.net" Version="$(WhisperNetVersion)" />
<PackageReference Include="Whisper.net.Runtime" Version="$(WhisperNetVersion)" />
```

**Пояснение:**
- `Whisper.net` — managed API (MIT, ~200 KB).
- `Whisper.net.Runtime` — нативный `whisper.cpp` для CPU (Windows/Linux/macOS,
  ~5 MB). Для CUDA/Vulkan — отдельные пакеты (`Whisper.net.Runtime.Cuda` и т.д.),
  **не нужны в MVP**.
- **NAudio НЕ требуется** (клиент отправляет WAV).

### § 3.2. Конфигурация

**`appsettings.Development.json`:**

```jsonc
"Speech": {
  "Enabled": true,
  "ModelPath": "tools/whisper/ggml-base.bin",
  "Language": "auto",                     // dev — auto (проверка RU+EN)
  "MaxAudioSeconds": 60,
  "MaxFileSizeBytes": 10485760,           // 10 MB
  "TimeoutSeconds": 60,

  // v1.13.1-fix5: фильтр «галлюцинаций» Whisper на тишине
  "NoSpeechThreshold": 0.85,              // было whisper.cpp default 0.6
  "LogprobThreshold": -1.0,
  "Temperature": 0.0,
  "MinAudioDurationMs": 300,              // ранний выход на коротких записях

  // v1.13.1-fix10/11: VAD (авто-остановка по тишине) + адаптивный порог
  "Vad": {
    "Enabled": true,
    "AdaptiveEnabled": true,              // v1.13.1-fix11c
    "SilenceRms": 0.015,                  // legacy (используется при AdaptiveEnabled=false)
    "AbsoluteMinRms": 0.001,              // нижняя граница адаптивного порога
    "NoiseMultiplier": 2.0,               // множитель над minObservedRms
    "SilenceTimeoutMs": 2000,
    "MinRecordingMs": 700,
    "PollIntervalMs": 200
  }
}
```

**`appsettings.json` (prod):**

```jsonc
"Speech": {
  "Enabled": false,                       // prod — включается осознанно админом
  "ModelPath": "tools/whisper/ggml-base.bin",
  "Language": "ru",                       // prod — фиксированный RU (основной сценарий)
  "MaxAudioSeconds": 60,
  "MaxFileSizeBytes": 10485760,
  "TimeoutSeconds": 60
}
```

**Почему `auto` в dev и `ru` в prod:** Whisper определяет язык за ~1 сек
(первый segment), стоимость незначительна. В dev `auto` удобнее для
проверки смешанной речи (RU + EN термины). В prod `ru` фиксирует
распознавание — не тратит первый segment на детект и не «дрожит» при
коротких записях (2-3 сек).

**DTO `SpeechOptions`** (`IIChatTools.Services/DTO/Speech/SpeechOptions.cs`):

```csharp
namespace IIChatTools.Services.DTO.Speech
{
    /// <summary>
    /// Настройки офлайн-распознавания речи (Whisper.net).
    /// Секция <c>Speech</c> в appsettings.json.
    /// </summary>
    public class SpeechOptions
    {
        /// <summary>Включено ли распознавание речи (default: false).</summary>
        public bool Enabled { get; set; }

        /// <summary>Путь к файлу модели (GGML). Относительный — от ContentRootPath.</summary>
        public string ModelPath { get; set; } = "tools/whisper/ggml-base.bin";

        /// <summary>Язык распознавания (ru | en | auto). По умолчанию — ru.</summary>
        public string Language { get; set; } = "ru";

        /// <summary>Максимальная длительность аудио (секунд).</summary>
        public int MaxAudioSeconds { get; set; } = 60;

        /// <summary>Максимальный размер WAV-файла (байт). Default 10 MB.</summary>
        public long MaxFileSizeBytes { get; set; } = 10_485_760;

        /// <summary>Таймаут транскрибации (секунд).</summary>
        public int TimeoutSeconds { get; set; } = 60;
    }
}
```

### § 3.3. `ISpeechRecognitionService`

**`IIChatTools.Services/Interfaces/ISpeechRecognitionService.cs`:**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис офлайн-распознавания речи (Whisper.net).
    /// Singleton — модель загружается один раз.
    /// </summary>
    public interface ISpeechRecognitionService
    {
        /// <summary>
        /// Транскрибирует WAV-поток (16 kHz mono PCM) в текст.
        /// </summary>
        /// <param name="wavStream">Поток с WAV-файлом (позиция в начале).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Результат с текстом и метаданными.</returns>
        Task<TranscriptionResult> TranscribeAsync(
            Stream wavStream,
            CancellationToken cancellationToken = default);

        /// <summary>Готов ли сервис (модель загружена).</summary>
        bool IsReady { get; }
    }
}
```

**`IIChatTools.Services/DTO/Speech/TranscriptionResult.cs`:**

```csharp
namespace IIChatTools.Services.DTO.Speech
{
    /// <summary>Результат транскрибации аудио.</summary>
    public class TranscriptionResult
    {
        /// <summary>Распознанный текст (может быть пустым, если речь не обнаружена).</summary>
        public string Text { get; set; }

        /// <summary>Язык, определённый моделью (например, "ru").</summary>
        public string Language { get; set; }

        /// <summary>Длительность аудио (мс).</summary>
        public long DurationMs { get; set; }

        /// <summary>Длительность обработки (мс).</summary>
        public long ProcessingMs { get; set; }
    }
}
```

### § 3.4. `WhisperNetTranscriptionService`

**`IIChatTools.Services/Implementation/Speech/WhisperNetTranscriptionService.cs`:**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace IIChatTools.Services.Implementation.Speech
{
    /// <summary>
    /// Реализация распознавания речи через Whisper.net (whisper.cpp).
    /// Singleton: модель загружается лениво при первом вызове и кэшируется.
    /// </summary>
    public sealed class WhisperNetTranscriptionService : ISpeechRecognitionService, IDisposable
    {
        private readonly SpeechOptions _options;
        private readonly IAppPathProvider _pathProvider;
        private readonly ILogger<WhisperNetTranscriptionService> _logger;

        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private WhisperFactory _factory;

        public bool IsReady => _factory != null;

        public WhisperNetTranscriptionService(
            IOptions<SpeechOptions> options,
            IAppPathProvider pathProvider,
            ILogger<WhisperNetTranscriptionService> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TranscriptionResult> TranscribeAsync(
            Stream wavStream,
            CancellationToken cancellationToken = default)
        {
            if (wavStream == null)
                throw new ArgumentNullException(nameof(wavStream));

            await EnsureInitializedAsync(cancellationToken);

            var sw = Stopwatch.StartNew();

            // Создаём процессор на каждый запрос (thread-safe паттерн Whisper.net).
            using var processor = _factory.CreateBuilder()
                .WithLanguage(_options.Language)
                .Build();

            var sb = new StringBuilder();
            string detectedLanguage = null;

            await foreach (var segment in processor.ProcessAsync(wavStream, cancellationToken))
            {
                sb.Append(segment.Text);
                detectedLanguage ??= segment.Language;
            }

            sw.Stop();

            var text = sb.ToString().Trim();
            _logger.LogInformation(
                "Whisper: распознано {Chars} символов за {Ms} мс (язык: {Lang})",
                text.Length, sw.ElapsedMilliseconds, detectedLanguage ?? _options.Language);

            return new TranscriptionResult
            {
                Text = text,
                Language = detectedLanguage ?? _options.Language,
                DurationMs = 0,   // заполним на контроллере (по WAV-заголовку) — Phase 2
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }

        // v1.13.1-fix5: StripWhisperMarkers — удаляет служебные маркеры
        // Whisper ([BLANK_AUDIO], [MUSIC], [музыка], …) из segment.Text.
        // v1.13.1-fix5: TryReadWavDurationMs — парсинг WAV-заголовка для
        // раннего выхода на файлах короче MinAudioDurationMs.
        // Оба метода — private static в этом же классе (полный код см. в
        // Implementation/Speech/WhisperNetTranscriptionService.cs).

        private async Task EnsureInitializedAsync(CancellationToken ct)
        {
            if (_factory != null) return;

            await _initLock.WaitAsync(ct);
            try
            {
                if (_factory != null) return;

                var modelPath = _options.ModelPath;
                if (!Path.IsPathRooted(modelPath))
                    modelPath = Path.Combine(_pathProvider.ContentRootPath, modelPath);

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException(
                        $"Модель Whisper не найдена: {modelPath}. " +
                        "Запустите scripts/setup/download-whisper-model.ps1.");
                }

                _logger.LogInformation("Загрузка модели Whisper: {Path}", modelPath);
                var sw = Stopwatch.StartNew();
                _factory = WhisperFactory.FromPath(modelPath);
                sw.Stop();
                _logger.LogInformation(
                    "Модель загружена за {Ms} мс", sw.ElapsedMilliseconds);
            }
            finally
            {
                _initLock.Release();
            }
        }

        public void Dispose()
        {
            _factory?.Dispose();
            _initLock?.Dispose();
        }
    }
}
```

### § 3.5. `SpeechController`

**`IIChatTools.API/Controllers/SpeechController.cs`:**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Speech;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API офлайн-распознавания речи (Whisper.net).
    /// </summary>
    [ApiController]
    [Route("api/speech")]
    [Authorize]
    public class SpeechController : ControllerBase
    {
        private readonly ISpeechRecognitionService _speech;
        private readonly SpeechOptions _options;
        private readonly ILogger<SpeechController> _logger;

        public SpeechController(
            ISpeechRecognitionService speech,
            IOptions<SpeechOptions> options,
            ILogger<SpeechController> logger)
        {
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Транскрибирует WAV-аудио в текст.
        /// </summary>
        /// <param name="file">WAV-файл (16 kHz mono, multipart/form-data).</param>
        /// <param name="ct">Токен отмены.</param>
        /// <remarks>
        /// <c>RequestSizeLimit(20 MB)</c> — жёсткий hard cap на уровне ASP.NET Core
        /// (запас над реальным лимитом 10 MB). Реальная валидация — ниже,
        /// по <c>Speech:MaxFileSizeBytes</c> (конфигурируемый).
        ///
        /// <para>
        /// ВНИМАНИЕ: в <c>Startup.cs</c> (v1.5.0, KI-083) уже установлен глобальный
        /// <c>KestrelServerOptions.Limits.MaxRequestBodySize = 40 MB</c>. Атрибут
        /// <c>[RequestSizeLimit]</c> применяется <b>поверх</b> — итоговый лимит для
        /// этого endpoint'а = 20 MB.
        /// </para>
        /// </remarks>
        [HttpPost("transcribe")]
        [RequestSizeLimit(20 * 1024 * 1024)]   // hard cap; реальный лимит — в Speech:MaxFileSizeBytes
        public async Task<IActionResult> TranscribeAsync(
            [FromForm] IFormFile file,
            CancellationToken ct)
        {
            if (!_options.Enabled)
            {
                return Ok(new { success = false, message = "Распознавание речи отключено." });
            }

            if (file == null || file.Length == 0)
            {
                return Ok(new { success = false, message = "Файл не передан." });
            }

            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Ok(new { success = false, message =
                    $"Файл больше {_options.MaxFileSizeBytes / 1024 / 1024} MB." });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _speech.TranscribeAsync(stream, ct);

                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    return Ok(new { success = false, message =
                        "Речь не обнаружена. Попробуйте ещё раз." });
                }

                return Ok(new { success = true, data = new
                {
                    text = result.Text,
                    language = result.Language,
                    processingMs = result.ProcessingMs
                }});
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Модель Whisper не найдена");
                return Ok(new { success = false, message =
                    "Модель распознавания речи не загружена. Обратитесь к администратору." });
            }
            catch (OperationCanceledException)
            {
                return Ok(new { success = false, message = "Транскрибация отменена." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка транскрибации");
                return Ok(new { success = false, message = "Внутренняя ошибка распознавания." });
            }
        }
    }
}
```

### § 3.6. Регистрация в DI

**`Startup.cs`:**

```csharp
// ============ Speech Recognition (v1.13, KI-140) ============
services.Configure<SpeechOptions>(Configuration.GetSection("Speech"));
services.AddSingleton<ISpeechRecognitionService, WhisperNetTranscriptionService>();
```

**ВАЖНО:** `IAppPathProvider` уже зарегистрирован (v1.7, KI-100) — переиспользуем.

---

## § 4. Frontend

### § 4.1. UI

Кнопка 🎤 в `.chat-input-box` **слева от 📎** (по аналогии с DeepSeek / ChatGPT).

```
┌──────────────────────────────────────────────────────────┐
│  [🎤] [📎]  Введите сообщение…                    [↑]    │
└──────────────────────────────────────────────────────────┘
```

**Состояния кнопки:**

| Состояние | Иконка | Цвет | Поведение |
|:---|:---:|:---|:---|
| Idle | 🎤 | Обычный | Клик → начало записи |
| Recording | 🔴 (пульсирует) | Красный | Клик → стоп + транскрибация |
| Transcribing | ⏳ (спиннер) | Серый | disabled |
| Error | ⚠️ | Красный | tooltip с ошибкой, через 3 сек — Idle |
| Disabled | 🎤 (полупрозрачный) | — | Speech:Enabled = false |

**Дополнительно во время записи:** показываем таймер `● 00:07` над полем ввода.

**Дополнительно во время записи:** показываем таймер `● 00:07` над полем ввода.

**v1.13.1-fix8/9 (KI-145):** карточка «🎤 Аудио» в `/profile`:

```
┌─ 🎤 Аудио ─────────────────────────────────────────────┐
│ Микрофон для голосового ввода в /chat.                  │
│                                                         │
│ Разрешите доступ, чтобы выбрать устройство:             │
│ [ Разрешить доступ к микрофону → ]                     │
│                                                         │
│ ───────────────────────────────────────────────────    │
│ Микрофон: [ Системный по умолчанию ▾ ]                  │
│           [ Протестировать ]                            │
│                                                         │
│ ⓘ Выбирайте физический микрофон. Steam Streaming /     │
│   VB-Cable / VoiceMeeter / OBS Virtual Audio дают      │
│   валидный трек, но нулевой сигнал.                    │
└─────────────────────────────────────────────────────────┘
```

Если пользователь не выбрал микрофон и Chrome отдаёт virtual device —
`speech.js` показывает warning-toast со ссылкой на `/profile → Аудио` (fix9).

### § 4.2. `speech.js`

**`wwwroot/js/modules/speech.js`** — актуальная реализация (~800 строк),
полностью переписана в v1.13.1 (fix5-fix11). Ниже — архитектура и публичный API.

**Ключевые изменения v1.13.1:**

| Аспект | v1.13.0 (MVP) | v1.13.1 |
|:---|:---|:---|
| **Захват PCM** | `MediaRecorder` + `decodeAudioData` | `MediaStreamTrackProcessor` (WebCodecs, fix6) |
| **Fallback** | — | `ScriptProcessorNode` (Firefox < 128, старый Chromium) |
| **Device selection** | Системный default | `AudioInputDeviceId` из `UserSettings` (fix8) |
| **Auto-stop** | 60 сек (жёстко) | 60 сек из `Speech:MaxAudioSeconds` (fix11) + VAD по тишине (fix10) |
| **Хоткей** | — | `Ctrl+Shift+Space` (fix10) |
| **Диагностика** | — | `label` + `deviceId` в лог (fix7); warning при virtual device (fix9) |

**Публичный API (не менялся с v1.13.0):**

```javascript
export function initSpeechRecognition(button, textarea, container, enabledFlag);
export function disposeSpeechRecognition();
```

**Внутренние компоненты:**

- **Загрузка конфига** — `_loadRuntimeConfig()` читает `data-speech-*`
  на `#chat-messages` (RULES § 4.17): `MaxAudioSeconds`, `Vad:*`,
  `SpeechVirtualDeviceWarning`.
- **Device picker** — `_ensureAudioDeviceId()` (lazy fetch `/api/profile/settings`).
- **Захват** — `_readMicrophoneLoop()` (WebCodecs) или `_setupWebAudioFallback()`.
- **VAD** — `_startVad()` / `_vadTick()` / `_stopVad()` (fix10/11).
  Опрос RMS через `AnalyserNode` каждые `Vad:PollIntervalMs` мс.
  Адаптивный порог = `max(minObservedRms × NoiseMultiplier, AbsoluteMinRms)`.
- **Hotkey** — `_onGlobalKeydown()` (fix10) — `Ctrl+Shift+Space`.
- **Обработка** — `_processAndSend()`: сбор чанков → RMS → peak normalize →
  resample 16 kHz → WAV 16-bit mono → POST `/api/speech/transcribe`.

**Fallback-цепочки (defensive):**

1. `deviceId: { exact: savedId }` → `NotFoundError`/`OverconstrainedError`
   → retry с системным default + toast (fix8).
2. `MediaStreamTrackProcessor` → `TypeError`/`ReferenceError`
   → Web Audio + `ScriptProcessorNode` (fix6).
3. `Vad:Enabled=false` или `AudioContext` недоступен → VAD молча
   отключается, работает только manual stop + `MaxAudioSeconds` (fix10).
4. `data-speech-*` отсутствуют (устаревший HTML) → встроенные константы (fix11).

**Полный код:** `IIChatTools.API/wwwroot/js/modules/speech.js` (~800 строк).

### § 4.3. Правка `Index.cshtml`

Кнопка 🎤 **перед** 📎 + `data-speech-*` на `#chat-messages`:

```html
<!-- v1.13.1 (KI-140, fix11): Голосовой ввод -->
<button id="btn-speech"
        type="button"
        class="chat-input-action chat-input-action-speech"
        title="@Localizer["SpeechButtonTooltip"]"
        aria-label="@Localizer["SpeechButtonAria"]"
        data-label-recording="@Localizer["SpeechRecording"]"
        data-label-transcribing="@Localizer["SpeechTranscribing"]"
        data-label-error-permission="@Localizer["SpeechErrorPermission"]"
        data-label-error-browser="@Localizer["SpeechErrorBrowser"]"
        disabled>
    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"
         viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
        <path d="M5 3a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0z"/>
        <path d="M3.5 6.5A.5.5 0 0 1 4 7v1a4 4 0 0 0 8 0V7a.5.5 0 0 1 1 0v1a5 5 0 0 1-4.5 4.975V15h3a.5.5 0 0 1 0 1h-7a.5.5 0 0 1 0-1h3v-2.025A5 5 0 0 1 3 8V7a.5.5 0 0 1 .5-.5z"/>
    </svg>
</button>
```

### § 4.3.1. `data-speech-*` на `#chat-messages` (v1.13.1-fix11)

Razor читает `Configuration.GetValue<T>()` и инжектит в `data-*`
(RULES § 4.17 — никакого хардкода в JS):

```razor
data-speech-max-record-ms="@(Configuration.GetValue<int>("Speech:MaxAudioSeconds", 60) * 1000)"
data-speech-vad-enabled="@(Configuration.GetValue<bool>("Speech:Vad:Enabled", true) ? "true" : "false")"
data-speech-vad-silence-rms="@(Configuration.GetValue<double>("Speech:Vad:SilenceRms", 0.015).ToString(System.Globalization.CultureInfo.InvariantCulture))"
data-speech-vad-adaptive-enabled="@(Configuration.GetValue<bool>("Speech:Vad:AdaptiveEnabled", true) ? "true" : "false")"
data-speech-vad-noise-multiplier="@(Configuration.GetValue<double>("Speech:Vad:NoiseMultiplier", 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture))"
data-speech-vad-absolute-min-rms="@(Configuration.GetValue<double>("Speech:Vad:AbsoluteMinRms", 0.001).ToString(System.Globalization.CultureInfo.InvariantCulture))"
data-speech-vad-silence-timeout-ms="@(Configuration.GetValue<int>("Speech:Vad:SilenceTimeoutMs", 2000))"
data-speech-vad-min-recording-ms="@(Configuration.GetValue<int>("Speech:Vad:MinRecordingMs", 700))"
data-speech-vad-poll-interval-ms="@(Configuration.GetValue<int>("Speech:Vad:PollIntervalMs", 200))"
```

**Важно (fix11a):** `.ToString(CultureInfo.InvariantCulture)` для `double`.
Без этого в ru-RU `0.015` → `"0,015"` → `parseFloat("0,015")` = **0** →
VAD-порог = 0 → auto-stop не срабатывает (RMS всегда ≥ 0). Отдельная
защита — в `speech.js:_loadRuntimeConfig.num()` (запятая → точка).

### § 4.4. CSS (`chat.css`)

```css
/* v1.13 (KI-140): голосовой ввод */
.chat-input-action-speech {
    background: transparent;
    color: var(--bs-secondary-color);
}

.chat-input-action-speech:hover:not(:disabled) {
    background: var(--bs-secondary-bg);
    color: var(--bs-primary);
}

/* Запись — красная пульсирующая */
.chat-input-action-speech.recording {
    background: var(--bs-danger);
    color: var(--bs-white);
    animation: speech-pulse 1.2s infinite ease-in-out;
}

.chat-input-action-speech.recording::after {
    content: attr(data-timer);
    position: absolute;
    top: -28px;
    right: 0;
    background: var(--bs-danger);
    color: var(--bs-white);
    padding: 2px 6px;
    border-radius: .2rem;
    font-size: .7rem;
    font-family: var(--bs-font-monospace);
}

@keyframes speech-pulse {
    0%, 100% { box-shadow: 0 0 0 0 rgba(var(--bs-danger-rgb), .7); }
    50%      { box-shadow: 0 0 0 8px rgba(var(--bs-danger-rgb), 0); }
}

/* Транскрибация — disabled + спиннер */
.chat-input-action-speech.transcribing {
    opacity: .6;
    cursor: wait;
}

/* Ошибка — красный на 3 сек */
.chat-input-action-speech.error {
    background: var(--bs-danger-bg-subtle);
    color: var(--bs-danger);
}

.chat-input-action-speech:disabled {
    opacity: .4;
    cursor: not-allowed;
}
```

### § 4.5. Локализация (RU + EN)

Новые ключи в `SharedResources.resx` + `.ru.resx`.

**v1.13.0 (6 ключей):**

| Ключ | RU | EN |
|:---|:---|:---|
| `SpeechButtonTooltip` | Голосовой ввод | Voice input |
| `SpeechButtonAria` | Начать запись | Start recording |
| `SpeechRecording` | Идёт запись… | Recording… |
| `SpeechTranscribing` | Распознаю… | Transcribing… |
| `SpeechErrorPermission` | Нет доступа к микрофону | Microphone access denied |
| `SpeechErrorBrowser` | Браузер не поддерживает запись | Browser doesn't support recording |

**v1.13.1-fix7/9 (диагностика виртуальных устройств):**

| Ключ | RU | EN |
|:---|:---|:---|
| `SpeechDeviceFallback` | Сохранённый микрофон недоступен. Использован системный. Проверьте Профиль → Аудио. | Saved microphone is unavailable. Using system default. Check Profile → Audio. |
| `SpeechVirtualDeviceWarning` | Выбран виртуальный микрофон — возможен нулевой сигнал. Выберите физический в Профиль → Аудио. | Virtual microphone detected — may produce zero signal. Choose a physical device in Profile → Audio. |

**v1.13.1-fix8 (карточка 🎤 Аудио в /profile):**

| Ключ | RU | EN |
|:---|:---|:---|
| `ProfileAudioSection` | Аудио | Audio |
| `ProfileAudioHint` | Микрофон для голосового ввода в /chat. | Microphone for voice input in /chat. |
| `ProfileAudioPermissionHint` | Чтобы увидеть список устройств, разрешите доступ. | To see the device list, allow access. |
| `ProfileAudioRequestPermission` | Разрешить доступ к микрофону | Allow microphone access |
| `ProfileAudioDeviceLabel` | Микрофон | Microphone |
| `ProfileAudioDefaultOption` | Системный по умолчанию | System default |
| `ProfileAudioTestButton` | Протестировать | Test |
| `ProfileAudioTestRecording` | Идёт запись… | Recording… |
| `ProfileAudioTestSuccess` | ✓ Работает · RMS: {0} · maxAbs: {1} | ✓ Working · RMS: {0} · maxAbs: {1} |
| `ProfileAudioTestFailed` | ✗ Сигнал нулевой — микрофон не передаёт данные | ✗ Zero signal — microphone not transmitting |
| `ProfileAudioVirtualWarning` | Выбирайте физический микрофон. Steam Streaming / VB-Cable / VoiceMeeter / OBS Virtual Audio дают валидный трек, но нулевой сигнал. | Choose a physical microphone. Steam Streaming / VB-Cable / VoiceMeeter / OBS Virtual Audio give a valid track but zero signal. |
| `ProfileAudioSaveSuccess` | Микрофон сохранён | Microphone saved |
| `ProfileAudioSaveError` | Не удалось сохранить настройку | Failed to save setting |
| `ProfileAudioPermissionDenied` | Доступ к микрофону не разрешён | Microphone access denied |
| `ProfileAudioPermissionGranted` | Доступ разрешён | Access granted |

### § 4.6. `profile-audio.js` (v1.13.1-fix8, KI-145)

**`wwwroot/js/modules/profile-audio.js`** (~350 строк) — модуль карточки
🎤 Аудио в `/profile`. Публичный API:

```javascript
export function initProfileAudioCard();
```

**Функционал:**

- `loadSavedDeviceId()` — fetch `/api/profile/settings`, читает `audioInputDeviceId`.
- `hasMicrophonePermission()` — `navigator.permissions.query({name:'microphone'})`,
  fallback на `enumerateDevices` с непустым `label`.
- `onRequestPermission()` — `getUserMedia({audio:true})` → stop → `enumerateDevices()`.
- `populateDevices()` — заполняет `<select>` + опция «Системный по умолчанию».
- `onDeviceChanged()` — `PUT /api/profile/audio-device`.
- `onTestDevice()` — 2 сек через `MediaStreamTrackProcessor` → `RMS` / `maxAbs`.

Состояния UI: **A** (нет разрешения — кнопка «Разрешить доступ»),
**B** (разрешение есть — `<select>` + «Протестировать»).

### § 4.7. `chat.js` — `initSpeechRecognition()` (v1.13.1)

В `initChatPage()` (после `bindEvents()`):

```javascript
const messagesEl = document.getElementById('chat-messages');
initSpeechRecognition(
    document.getElementById('btn-speech'),
    document.getElementById('chat-input'),
    messagesEl,
    messagesEl?.dataset.speechEnabled || 'false');
```

Никаких изменений с v1.13.0 — добавлена только передача `container`
для `data-*`-конфига (fix11).

## § 5. Модель и скрипт скачивания

### § 5.1. Файлы модели

**Модели Whisper (GGML, совместимы с whisper.cpp):**

| Модель | Размер | Скорость (CPU) | Качество RU | Рекомендация |
|:---|:---:|:---:|:---:|:---|
| `tiny` | 75 MB | ~10× realtime | Плохое | Тест / очень слабый CPU |
| **`base`** | **142 MB** | **~3-5× realtime** | **Хорошее** | **MVP (default)** |
| `small` | 466 MB | ~1-2× realtime | Отличное | Если есть GPU / мощный CPU |
| `medium` | 1.5 GB | ~0.5× realtime | Отличное | Только GPU |
| `large-v3` | 2.9 GB | Медленно | Лучшее | Продакшен с GPU |

**Источник:** https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{size}.bin

### § 5.2. Скрипт скачивания

**`scripts/setup/download-whisper-model.ps1`** (новый):

```powershell
<#
.SYNOPSIS
    Скачивает модель Whisper GGML для офлайн-распознавания речи.

.PARAMETER ModelSize
    Размер модели: tiny | base | small | medium | large-v3.
    По умолчанию — base (142 MB).

.PARAMETER Force
    Перезаписать существующий файл.
#>
param(
    [ValidateSet('tiny','base','small','medium','large-v3')]
    [string]$ModelSize = 'base',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$targetDir = Join-Path $repoRoot 'tools\whisper'
$targetFile = Join-Path $targetDir "ggml-$ModelSize.bin"

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

if ((Test-Path $targetFile) -and -not $Force) {
    Write-Host "Модель уже существует: $targetFile" -ForegroundColor Yellow
    Write-Host "Используйте -Force для перезаписи." -ForegroundColor Yellow
    exit 0
}

$url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$ModelSize.bin"
Write-Host "Скачивание модели: $ModelSize" -ForegroundColor Cyan
Write-Host "URL: $url"
Write-Host "Размер: ~$([math]::Round((@{tiny=75;base=142;small=466;medium=1500;'large-v3'=2900}[$ModelSize]) )) MB"

Invoke-WebRequest -Uri $url -OutFile $targetFile -UseBasicParsing

$size = [math]::Round((Get-Item $targetFile).Length / 1MB, 1)
Write-Host "Готово: $targetFile ($size MB)" -ForegroundColor Green
```

**Запуск:**

```powershell
cd <repo-root>
pwsh -ExecutionPolicy Bypass -File scripts/setup/download-whisper-model.ps1
# или для small:
pwsh -ExecutionPolicy Bypass -File scripts/setup/download-whisper-model.ps1 -ModelSize small
```

### § 5.3. `.gitignore`

Добавить:

```
# v1.13 (KI-140): Whisper-модели (не коммитим бинарники)
tools/whisper/*.bin
tools/whisper/*.ggml
```

---

## § 6. Безопасность и приватность

| Аспект | Мера |
|:---|:---|
| **Аудио не покидает сервер** | Whisper.net работает in-process, никаких облачных API |
| **Permission** | `getUserMedia` требует явного разрешения браузера |
| **Ограничение размера** | `MaxFileSizeBytes = 10 MB` (≈ 5 мин при 16 kHz mono 16-bit) |
| **Ограничение длительности** | `MaxAudioSeconds = 60` — auto-stop **на клиенте** (`MAX_RECORDING_MS` в `speech.js`, § 4.2). Серверный hard-cap — `RequestSizeLimit(20 MB)` + валидация `MaxFileSizeBytes = 10 MB` (§ 3.5). |
| **Таймаут** | 60 сек на транскрибацию (защита от зависаний) |
| **Формат** | Только WAV (валидация MIME + magic bytes `RIFF`) |
| **Cleanup** | Поток обрабатывается in-memory, не сохраняется на диск |

**НЕ логируем:** аудио, текст транскрибации, длительность записи.
Логируем только: длительность обработки, длина текста, язык.

---

## § 7. API endpoints

| Метод | URL | Назначение |
|:---|:---|:---|
| `POST` | `/api/speech/transcribe` | Multipart WAV → `{ success, data: { text, language, processingMs } }` |
| `GET` | `/api/profile/settings` | v1.13.1-fix8 — читает `audioInputDeviceId` (read-only поле в `UserSettingsDto`) |
| `PUT` | `/api/profile/audio-device` | v1.13.1-fix8 — сохранить выбранный микрофон. **Отдельный endpoint** (не через `/api/profile/settings`: `AudioInputDeviceId=null` = «сбросить на default», а не «не трогать»). |

**Формат ответа:**

```json
{
  "success": true,
  "data": {
    "text": "Привет, как дела?",
    "language": "ru",
    "processingMs": 1240
  }
}
```

**Ошибка:**

```json
{
  "success": false,
  "message": "Речь не обнаружена. Попробуйте ещё раз."
}
```

---

## § 8. Фазы работ

### § 8.1. Фаза 1 — Backend (~1.5 ч)

| Шаг | Что | Оценка |
|:---|:---|:---:|
| 1A | NuGet `Whisper.net` + `Whisper.net.Runtime`, `Directory.Build.props` | 15 мин |
| 1B | DTO `SpeechOptions`, `TranscriptionResult` | 10 мин |
| 1C | `ISpeechRecognitionService` + `WhisperNetTranscriptionService` | 40 мин |
| 1D | `SpeechController` + endpoint | 20 мин |
| 1E | DI-регистрация + `appsettings` | 10 мин |
| 1F | Smoke: загрузка модели + тест через curl | 15 мин |

### § 8.2. Фаза 2 — Frontend (~1 ч)

| Шаг | Что | Оценка |
|:---|:---|:---:|
| 2A | `speech.js` (MediaRecorder + WAV-encoder) | 40 мин |
| 2B | Кнопка 🎤 в `Index.cshtml` + data-* | 10 мин |
| 2C | CSS (idle / recording / transcribing / error) | 20 мин |
| 2D | Локализация RU + EN (6 ключей) | 10 мин |

### § 8.3. Фаза 3 — Скрипт + Smoke (~30 мин)

| Шаг | Что | Оценка |
|:---|:---|:---:|
| 3A | `scripts/setup/download-whisper-model.ps1` | 15 мин |
| 3B | `.gitignore` для `tools/whisper/*.bin` | 5 мин |
| 3C | End-to-end smoke в `/chat` (RU/EN, разные фразы) | 10 мин |

### § 8.4. Фаза 4 — Опционально (~30-60 мин)

- **VAD (Voice Activity Detection)** — auto-stop по тишине 2 сек.
  Реализуется **на клиенте** через Web Audio API `AnalyserNode` с порогом
  амплитуды (~30 строк JS). Не требует изменений backend.
- **Хоткей `Ctrl+Shift+Space`** — start/stop записи.
- **Стриминг** — промежуточная транскрибация в реальном времени
  (требует переделки API на chunked upload + пересборка транскрипта).
- **GPU** — `Whisper.net.Runtime.Cuda` (если есть NVIDIA GPU).

**Приоритет Ф4:** VAD > hotkey > GPU > streaming (по соотношению
«эффект / трудозатраты»).

**Итого MVP (Фазы 1-3):** ~3 ч.

### § 8.5. Фаза 5 — fix1-fix11 (v1.13.1, ~6 ч)

Серия исправлений после первого production smoke. Все — под тегом v1.13.1.

| Fix | Проблема | Решение |
|:---|:---|:---|
| fix1-fix4 | `decodeAudioData` в Chromium возвращает занулённый AudioBuffer для WebM/Opus от MediaRecorder | Переход на прямой захват PCM (в финале — `MediaStreamTrackProcessor`) |
| fix5 | Whisper галлюцинирует `[BLANK_AUDIO]` / `[музыка]` на тишине | `StripWhisperMarkers` + `NoSpeechThreshold=0.85` + `TryReadWavDurationMs` |
| fix6 | Chrome pruning-ит `ScriptProcessorNode` даже с `destination` | `MediaStreamTrackProcessor` (WebCodecs) — основной путь; `ScriptProcessorNode` — fallback |
| fix7 | Непонятно, какой микрофон используется | Лог `label` + `deviceId` + сообщение об ошибке с именем устройства |
| fix8 | Нет UI для выбора микрофона (KI-145) | Карточка 🎤 Аудио в `/profile` + `PUT /api/profile/audio-device` + `AudioInputDeviceId` в `UserSettings` |
| fix9 | Chrome выбирает virtual audio device по умолчанию (KI-144) | Warning-toast если `label` матчит `/(steam\|vb[-\s]?cable\|virtual\|voicemeeter\|obs)/i` и устройство не выбрано явно |
| fix10 | Manual stop после каждой фразы | VAD (auto-stop по тишине) + хоткей `Ctrl+Shift+Space` |
| fix11 | VAD-параметры хардкод; фиксированный порог не работает на тихих микрофонах (KI-146) | `Speech:Vad` в `appsettings.json` + `CultureInfo.InvariantCulture` + **адаптивный порог** |

**Оценка:** ~6 ч. **Коммитов:** 5 (`fix5/6`, `fix7`, `fix8`, `fix9+KI-146`, `fix10+fix11`).

---

## § 9. Тестирование

### § 9.1. Unit

- **`SpeechControllerTests`** (8 тестов, v1.13.1):
  - `TranscribeAsync_Disabled_ReturnsFail`
  - `TranscribeAsync_EmptyFile_ReturnsFail`
  - `TranscribeAsync_TooLargeFile_ReturnsFail`
  - `TranscribeAsync_ValidFile_ReturnsText` (mock `ISpeechRecognitionService`)
  - `TranscribeAsync_EmptyResult_ReturnsFail`
  - `TranscribeAsync_ModelNotFound_ReturnsFail`
  - `TranscribeAsync_Cancelled_ReturnsFail`
  - `TranscribeAsync_MinDuration_ReturnsEmpty`

**Общий счёт (v1.13.1):** 1016/1016 (5 Skip — реальные внешние API
DeepSeek / OpenAI / Groq / Together / Ollama / Anthropic / Gemini).

- **`WhisperNetTranscriptionServiceTests`** (3 теста, требует модель):
  - `TranscribeAsync_ShortWav_ReturnsText` — 1-2 сек WAV "привет".
  - `TranscribeAsync_EmptyWav_ReturnsEmpty`.
  - `IsReady_AfterInitialization_ReturnsTrue`.
  - **Skip если модель не найдена** (`[Fact(Skip = "Whisper model not available")]`).

### § 9.2. Integration (опционально)

- E2E через `WebApplicationFactory`: POST WAV → получить текст.
- Требует реальную модель → Skip в CI.

### § 9.3. Smoke (ручной)

1. `/chat` → нажать 🎤.
2. Разрешить доступ к микрофону (browser permission).
3. Сказать: «Привет, как дела?».
4. Нажать 🎤 (stop) или подождать auto-stop.
5. Проверить: текст появился в textarea, чекер → отправить.
6. Повторить по-английски (при Language=auto).

---

## § 10. Риски и ограничения

| Риск | Митигация |
|:---|:---|
| **CPU-нагрузка** (3-5× realtime на CPU) | Модель `base`; для больших CPU — `tiny` |
| **Большая модель в git** (142 MB) | `.gitignore` + скрипт скачивания |
| **Docker-образ + 5 MB** (native libs) | Приемлемо; в Docker обрабатываем — модель монтируется через volume |
| **Размер WAV** (10 MB ≈ 5 мин 16 kHz mono) | MaxAudioSeconds = 60 (UI auto-stop — Phase 4) |
| **Точность на шумной записи** | Ограничение модели; `small`/`medium` — Phase 4 |
| **Firefox / Safari** — MediaRecorder работает, но WebM/Opus поддержка может отличаться | Fallback: если `MediaRecorder` не поддерживает WebM — используем OGG/Opus (Web Audio API всё равно декодирует) |
| **HTTPS required** для `getUserMedia` | Уже настроено (проект работает через `https://localhost:5001`) |

---

## § 11. Принятые решения и отложенные вопросы

### § 11.1. Принятые решения (согласовано 2026-10-03)

1. **Модель по умолчанию** — `base` (142 MB, sweet spot для русского).
2. **Язык** — `"auto"` в `appsettings.Development.json`, `"ru"` в prod
   `appsettings.json` (§ 3.2). Whisper определяет язык за ~1 сек, dev
   удобнее для проверки RU+EN.
3. **Позиция кнопки** — слева от 📎 в `.chat-input-box` (§ 4.1).
4. **Поведение** — вставлять в `<textarea>` (в конец, если есть текст),
   не отправлять автоматически (§ 4.2).
5. **Ресемплинг WAV** — через `OfflineAudioContext` (§ 4.2),
   **не** `AudioContext({ sampleRate: 16000 })` (Chrome игнорирует hint).
6. **Auto-stop по 60 сек** — enforced на клиенте
   (`MAX_RECORDING_MS`, § 4.2). Синхронизировано с `Speech:MaxAudioSeconds`.
7. **`RequestSizeLimit`** — 20 MB hard cap (§ 3.5), реальный лимит —
   `Speech:MaxFileSizeBytes = 10 MB` (валидация в контроллере).

### § 11.1a. Принятые решения (v1.13.1, 2026-10-04)

8. **Захват PCM** — `MediaStreamTrackProcessor` (WebCodecs API,
   Chromium 94+). Причина: Chrome pruning-ил `ScriptProcessorNode` даже
   с `destination` — `onaudioprocess` фирес, буфер пустой. Fallback —
   `ScriptProcessorNode` для Firefox < 128 / Safari / старого Chromium
   (fix6).
9. **Device picker в /profile → 🎤 Аудио.** Пользователь может явно
   выбрать микрофон; `deviceId` сохраняется в `UserSettings`
   (`Audio.InputDeviceId`). `speech.js` использует его в `getUserMedia`
   (fix8, KI-145). Fallback при недоступности — системный default +
   toast.
10. **Warning при virtual audio device.** Если `label` трека матчит
    `/(steam|vb[-\s]?cable|virtual|voicemeeter|obs)/i` и пользователь
    не сделал явный выбор — один раз показываем toast со ссылкой на
    `/profile → Аудио` (fix9, KI-144).
11. **VAD на клиенте.** Auto-stop по тишине через `AnalyserNode`
    (не требует backend). Параметры — `Speech:Vad` в `appsettings.json`
    (fix10-fix11).
12. **Адаптивный порог тишины.** По умолчанию
    `Vad:AdaptiveEnabled=true` — порог = `max(minObservedRms × 2.0, 0.001)`.
    Причина: фиксированный `SilenceRms=0.015` не работает на тихих
    микрофонах (речь RMS 0.006–0.010) — VAD считал её тишиной и обрывал
    запись через 2 сек (fix11c).
13. **Хоткей `Ctrl+Shift+Space`** — toggle start/stop записи.
    `preventDefault` (в textarea этот шорткат вводит `&nbsp;`). Не
    конфликтует с `Ctrl+B/F/K` в `chat.js` (там без `shiftKey`) (fix10).
14. **`CultureInfo.InvariantCulture` для `double` в Razor-`data-*`** —
    ru-RU отдаёт `"0,015"`, `parseFloat` возвращает `0`. Fix11a.
15. **Отдельный endpoint `PUT /api/profile/audio-device`** — не через
    `PUT /api/profile/settings`. Причина: `AudioInputDeviceId=null` =
    «сбросить на default», а `RetentionDays=null` = «использовать
    глобальный». Разная семантика `null`.

### § 11.2. Отложенные вопросы (Phase 4, опционально)

1. ~~**VAD** (auto-stop по тишине)~~ — **Done (v1.13.1, fix10/11)**.
2. ~~**Хоткей `Ctrl+Shift+Space`**~~ — **Done (v1.13.1, fix10)**.
3. **GPU** (`Whisper.net.Runtime.Cuda`) — зависит от железа.
4. **Streaming** (промежуточная транскрибация) — требует переделки API.
   Приоритет: низкий.
5. **Fallback-полировка + дедупликация** — KI-146 (Planned).

### § 11.3. Технический долг (не блокер MVP)

1. **Fail-fast при старте**: сейчас `WhisperFactory.FromPath` вызывается
   лениво — если модель отсутствует, ошибка возникнет только при первом
   `POST /api/speech/transcribe`. **Улучшение:** resolve
   `ISpeechRecognitionService` в `Program.cs` при `Speech:Enabled = true`
   (по образцу `ExternalProviderRegistry`, KI-109).
2. **Валидация формата WAV**: `SpeechController` проверяет только
   `file.Length`, но не magic bytes (`RIFF` + `WAVE`). Браузеры, как правило,
   отправляют корректный WAV, но защита от подмены — в follow-up.
3. **`TranscriptionResult.DurationMs = 0`**: длительность аудио сейчас не
   заполняется (в § 3.4 — комментарий «заполним на контроллере по WAV-заголовку»).
   Не критично для UI, но полезно для метрик.

---

## § 12. Ссылки

- [Whisper.net GitHub](https://github.com/sandrohanea/whisper.net)
- [whisper.cpp GitHub](https://github.com/ggerganov/whisper.cpp)
- [Whisper models (HuggingFace)](https://huggingface.co/ggerganov/whisper.cpp)
- [MediaStreamTrackProcessor (WebCodecs)](https://developer.mozilla.org/en-US/docs/Web/API/MediaStreamTrackProcessor)
- [Web Audio API — decodeAudioData](https://developer.mozilla.org/en-US/docs/Web/API/BaseAudioContext/decodeAudioData)
- [MediaRecorder API](https://developer.mozilla.org/en-US/docs/Web/API/MediaRecorder)
- [KI-140](../KNOWN_ISSUES.md#ki-140) — Speech Recognition
- [KI-144](../KNOWN_ISSUES.md#ki-144) — virtual audio device
- [KI-145](../KNOWN_ISSUES.md#ki-145) — device picker
- [KI-146](../KNOWN_ISSUES.md#ki-146) — fallback + дедупликация

---

## § 13. История v1.13.1 (fix1-fix11)

| Fix | Дата | Файлы | Суть |
|:---|:---|:---|:---|
| fix1-fix4 | 2026-10-04 | `speech.js` | `decodeAudioData` в Chromium возвращает занулённый буфер для WebM/Opus → прямой захват PCM |
| fix5 | 2026-10-04 | `WhisperNetTranscriptionService.cs`, `SpeechOptions.cs` | Фильтр `[BLANK_AUDIO]` / `[музыка]` + `NoSpeechThreshold=0.85` + `TryReadWavDurationMs` |
| fix6 | 2026-10-04 | `speech.js` | Chrome pruning `ScriptProcessorNode` → `MediaStreamTrackProcessor` (WebCodecs) |
| fix7 | 2026-10-04 | `speech.js` | Лог `label` + `deviceId` + сообщение об ошибке с именем микрофона. **KI-144** (Documented) |
| fix8 | 2026-10-04 | `speech.js`, `ProfileController.cs`, `UserSettingsDto.cs`, `AudioDeviceUpdateRequest.cs`, `Views/Profile/Index.cshtml`, `profile-audio.js`, `.resx` ×2 | Device picker в `/profile → 🎤 Аудио` + `PUT /api/profile/audio-device`. **KI-145** (Fixed) |
| fix9 | 2026-10-04 | `speech.js`, `Views/Chat/Index.cshtml`, `.resx` ×2 | Warning при выборе virtual audio device. **KI-146** (Planned) |
| fix10 | 2026-10-04 | `speech.js` | VAD + хоткей `Ctrl+Shift+Space` |
| fix11 | 2026-10-04 | `speech.js`, `SpeechOptions.cs`, `appsettings*.json`, `Views/Chat/Index.cshtml` | VAD-параметры в `appsettings.json` + адаптивный порог + `CultureInfo.InvariantCulture` для `double` в Razor-`data-*` |

**Тесты:** 1011 → **1016** (+5: `SpeechControllerTests`).
**CI:** ✅ green. **Docker:** ✅ `ghcr.io/iilmchat/iichattools:v1.13.1`.

**Известные ограничения v1.13.1:**

- `deviceId` меняется при переподключении USB → сохранённое значение
  может стать невалидным (fallback → default + toast). См. KI-146.
- Fallback через `getUserMedia({audio})` может снова вернуть virtual
  device (Steam Streaming) — toast показывается, запись будет тихой.
  См. KI-146.
- Chrome может показать одно физическое устройство как 3 разных
  `deviceId` с префиксами в label. См. KI-146.

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.13.1**