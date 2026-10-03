# DESIGN — Распознавание речи в чате (Whisper.net, офлайн)

**Версия:** v1.13.0 (Draft)
**Дата:** 2026-10-02
**Статус:** Draft
**Связанные документы:** [RULES.md](../../RULES.md) · [DESIGN v1.11 (Actor-Critic)](../v1.11/DESIGN_MULTI_AGENT_DEBATE.md) · [ARCHITECTURE.md](../../ARCHITECTURE.md) · KI-140

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
  "TimeoutSeconds": 60
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

### § 4.2. `speech.js`

**`wwwroot/js/modules/speech.js`** (новый модуль, ~200 строк):

```javascript
/**
 * Модуль офлайн-распознавания речи (v1.13, KI-140).
 * Использует MediaRecorder + Web Audio API (для WAV-кодирования) + Whisper.net (backend).
 */

let _mediaRecorder = null;
let _audioChunks = [];
let _stream = null;
let _isRecording = false;
let _recordingStartTime = 0;
let _recordingTimer = null;

/**
 * Привязывает кнопку 🎤 и настраивает обработчики.
 * @param {HTMLButtonElement} button
 * @param {HTMLTextAreaElement} textarea
 */
export function initSpeechRecognition(button, textarea) {
    if (!button || !textarea) return;

    button.addEventListener('click', () => {
        if (_isRecording) stopRecording(button, textarea);
        else startRecording(button, textarea);
    });
}

async function startRecording(button, textarea) {
    if (!navigator.mediaDevices?.getUserMedia) {
        showError(button, 'Браузер не поддерживает запись аудио');
        return;
    }

    try {
        _stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        _audioChunks = [];
        _mediaRecorder = new MediaRecorder(_stream, { mimeType: 'audio/webm;codecs=opus' });

        _mediaRecorder.ondataavailable = (e) => {
            if (e.data.size > 0) _audioChunks.push(e.data);
        };
        _mediaRecorder.onstop = () => onRecordingStop(button, textarea);
        _mediaRecorder.start();

        _isRecording = true;
        _recordingStartTime = Date.now();
        setRecordingUI(button, true);
        startTimer(button, textarea);   // v1.13.0: textarea нужен для auto-stop
    } catch (ex) {
        showError(button, 'Нет доступа к микрофону');
        console.error('[speech] getUserMedia failed:', ex);
    }
}

function stopRecording(button, textarea) {
    if (!_mediaRecorder) return;
    _mediaRecorder.stop();
    _stream?.getTracks().forEach(t => t.stop());
    _stream = null;
    _isRecording = false;
    stopTimer();
    setRecordingUI(button, false);
    setTranscribingUI(button, true);
}

async function onRecordingStop(button, textarea) {
    try {
        const blob = new Blob(_audioChunks, { type: 'audio/webm' });
        const wavBlob = await webmToWav(blob);

        const fd = new FormData();
        fd.append('file', wavBlob, 'audio.wav');

        const response = await fetch('/api/speech/transcribe', {
            method: 'POST',
            credentials: 'same-origin',
            body: fd,
        });
        const res = await response.json();

        if (!res.success) {
            showError(button, res.message || 'Ошибка распознавания');
            return;
        }

        // Вставляем текст в textarea (в конец, если уже что-то есть).
        const prefix = textarea.value.trim() ? textarea.value + ' ' : '';
        textarea.value = prefix + res.data.text;
        textarea.dispatchEvent(new Event('input'));  // для autoResizeTextarea
        textarea.focus();
    } catch (ex) {
        console.error('[speech] transcribe failed:', ex);
        showError(button, 'Ошибка обработки аудио');
    } finally {
        setTranscribingUI(button, false);
    }
}

/**
 * Конвертирует WebM/Opus → WAV 16 kHz mono через Web Audio API.
 *
 * ВАЖНО: `new AudioContext({ sampleRate: 16000 })` — это hint, не гарантия.
 * Chrome игнорирует его для `decodeAudioData` — возвращает buffer в native rate
 * (обычно 48 kHz). Whisper.net ожидает WAV 16 kHz — поэтому делаем явный
 * ресемплинг через `OfflineAudioContext`.
 */
async function webmToWav(blob) {
    const arrayBuffer = await blob.arrayBuffer();

    // 1. Декодируем во временный AudioContext (native sample rate).
    const tempCtx = new AudioContext();
    let audioBuffer;
    try {
        audioBuffer = await tempCtx.decodeAudioData(arrayBuffer);
    } finally {
        await tempCtx.close();
    }

    // 2. Ресемплим до 16 kHz mono через OfflineAudioContext.
    //    Конструктор: (channels=1, length, sampleRate=16000).
    const targetRate = 16000;
    const length = Math.ceil(audioBuffer.duration * targetRate);
    const offlineCtx = new OfflineAudioContext(1, length, targetRate);
    const source = offlineCtx.createBufferSource();
    source.buffer = audioBuffer;
    source.connect(offlineCtx.destination);
    source.start();
    const resampled = await offlineCtx.startRendering();

    return encodeWav(resampled);
}

function encodeWav(audioBuffer) {
    const numChannels = 1;  // mono
    const sampleRate = audioBuffer.sampleRate;
    const samples = audioBuffer.getChannelData(0);  // уже mono
    const buffer = new ArrayBuffer(44 + samples.length * 2);
    const view = new DataView(buffer);

    writeString(view, 0, 'RIFF');
    view.setUint32(4, 36 + samples.length * 2, true);
    writeString(view, 8, 'WAVE');
    writeString(view, 12, 'fmt ');
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);          // PCM
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * numChannels * 2, true);  // byte rate
    view.setUint16(32, numChannels * 2, true);               // block align
    view.setUint16(34, 16, true);                            // bits per sample
    writeString(view, 36, 'data');
    view.setUint32(40, samples.length * 2, true);

    let offset = 44;
    for (let i = 0; i < samples.length; i++, offset += 2) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
    }

    return new Blob([view], { type: 'audio/wav' });
}

function writeString(view, offset, str) {
    for (let i = 0; i < str.length; i++)
        view.setUint8(offset + i, str.charCodeAt(i));
}

// UI helpers
function setRecordingUI(button, recording) {
    button.classList.toggle('recording', recording);
    button.setAttribute('aria-label', recording ? 'Остановить запись' : 'Голосовой ввод');
}

function setTranscribingUI(button, transcribing) {
    button.classList.toggle('transcribing', transcribing);
    button.disabled = transcribing;
}

/** Лимит длительности записи (мс). Enforced на клиенте (см. § 6, § 11.1). */
const MAX_RECORDING_MS = 60_000;

function startTimer(button, textarea) {
    _recordingTimer = setInterval(() => {
        const elapsedMs = Date.now() - _recordingStartTime;
        const sec = Math.floor(elapsedMs / 1000);
        const mm = String(Math.floor(sec / 60)).padStart(2, '0');
        const ss = String(sec % 60).padStart(2, '0');
        button.dataset.timer = `${mm}:${ss}`;

        // Auto-stop: превысили 60 сек → останавливаем и транскрибируем.
        // MAX_RECORDING_MS синхронизирован с Speech:MaxAudioSeconds (60).
        if (elapsedMs >= MAX_RECORDING_MS && _isRecording) {
            console.info('[speech] auto-stop: достигнут лимит 60 сек');
            stopRecording(button, textarea);
        }
    }, 500);
}

function stopTimer() {
    if (_recordingTimer) { clearInterval(_recordingTimer); _recordingTimer = null; }
    delete document.getElementById('btn-speech')?.dataset.timer;
}

function showError(button, message) {
    button.classList.add('error');
    button.title = message;
    setTimeout(() => {
        button.classList.remove('error');
        button.title = 'Голосовой ввод';
    }, 3000);
}
```

### § 4.3. Правка `Index.cshtml`

Добавить кнопку 🎤 **перед** 📎:

```html
<!-- v1.13 (KI-140): Голосовой ввод -->
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

**Важно:** кнопка активируется только если `Speech:Enabled = true` (иначе остаётся `disabled`). Проверка — через data-атрибут на `#chat-messages` или отдельный endpoint `/api/speech/status`.

**Проще:** инжект `data-speech-enabled="@((Configuration.GetValue<bool>("Speech:Enabled")) ? "true" : "false")"` на `#chat-messages`, `speech.js` читает и решает — активировать ли кнопку.

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

Новые ключи в `SharedResources.resx` + `.ru.resx`:

| Ключ | RU | EN |
|:---|:---|:---|
| `SpeechButtonTooltip` | Голосовой ввод | Voice input |
| `SpeechButtonAria` | Начать запись | Start recording |
| `SpeechRecording` | Идёт запись… | Recording… |
| `SpeechTranscribing` | Распознаю… | Transcribing… |
| `SpeechErrorPermission` | Нет доступа к микрофону | Microphone access denied |
| `SpeechErrorBrowser` | Браузер не поддерживает запись | Browser doesn't support recording |

---

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

---

## § 9. Тестирование

### § 9.1. Unit

- **`SpeechControllerTests`** (4 теста):
  - `TranscribeAsync_Disabled_ReturnsFail`
  - `TranscribeAsync_EmptyFile_ReturnsFail`
  - `TranscribeAsync_TooLargeFile_ReturnsFail`
  - `TranscribeAsync_ValidFile_ReturnsText` (mock `ISpeechRecognitionService`)

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

### § 11.2. Отложенные вопросы (Phase 4, опционально)

1. **VAD** (auto-stop по тишине) — реализуется на клиенте через
   `AnalyserNode`. Не требует backend. Приоритет: высокий.
2. **Хоткей `Ctrl+Shift+Space`** — низкая сложность.
3. **GPU** (`Whisper.net.Runtime.Cuda`) — зависит от железа.
4. **Streaming** (промежуточная транскрибация) — требует переделки API.
   Приоритет: низкий.

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
- [Web Audio API — decodeAudioData](https://developer.mozilla.org/en-US/docs/Web/API/BaseAudioContext/decodeAudioData)
- [MediaRecorder API](https://developer.mozilla.org/en-US/docs/Web/API/MediaRecorder)
- [KI-140](../KNOWN_ISSUES.md#ki-140) — запись в реестре

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.11.0**