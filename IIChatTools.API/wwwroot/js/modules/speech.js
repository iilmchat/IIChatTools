/**
 * Модуль офлайн-распознавания речи в чате (v1.13.0, KI-140).
 *
 * v1.13.1-fix6: переход на MediaStreamTrackProcessor (WebCodecs API) —
 * надёжное чтение PCM из MediaStreamTrack без Web Audio.
 *
 * Причина: Chrome pruning-ит ScriptProcessorNode даже с подключением к
 * destination — `onaudioprocess` фирес, но inputBuffer пустой (нули).
 * Yandex Browser ведёт себя мягче. MediaStreamTrackProcessor одинаково
 * работает везде.
 *
 * Fallback: если MediaStreamTrackProcessor недоступен (старый Chromium /
 * Firefox < 128) — Web Audio API с ScriptProcessorNode.
 *
 * Принципы:
 *   - Вся обработка — локально, аудио не покидает сервер приложения.
 *   - Auto-stop по 60 сек (синхронизировано со Speech:MaxAudioSeconds).
 *   - Текст вставляется в textarea (не отправляется автоматически).
 *
 * © 2026 RuChating (iilmchat) · IIChatTools v1.13.1
 */

import { toast } from './ui.js';   // v1.13.1-fix8 (KI-145): fallback-toast

// ============ Константы ============

/**
 * Лимит длительности записи (мс). Читается из `Speech:MaxAudioSeconds` (× 1000).
 * Default = 60 000 (60 сек) — v1.13.1-fix11 (KI-140).
 */
let MAX_RECORDING_MS = 60_000;

/** Интервал обновления таймера в UI (мс). Не конфигурируется (UI-only). */
const TIMER_INTERVAL_MS = 500;

/** Целевая частота дискретизации WAV для Whisper.net. */
const TARGET_SAMPLE_RATE = 16000;

/** Размер буфера ScriptProcessor (сэмплов). 4096 — баланс latency/нагрузка. */
const PROCESSOR_BUFFER_SIZE = 4096;

/** Целевой peak для нормализации (0..1). 0.7 = −3 дБ, без клиппинга. */
const NORMALIZE_TARGET_PEAK = 0.7;

/** Минимальный peak, при котором нормализация ещё имеет смысл. */
const NORMALIZE_MIN_PEAK = 1e-4;

/** Максимальный gain при нормализации (10× ≈ +20 дБ). */
const NORMALIZE_MAX_GAIN = 10.0;

/**
 * Паттерн «виртуальных» audio-устройств (v1.13.1-fix9, KI-140-fix).
 * Такие устройства дают валидный MediaStreamTrack, но нулевой сигнал
 * (см. KI-144). Если пользователь не выбрал микрофон явно, а трек
 * матчит паттерн — один раз предупреждаем в UI.
 */
const VIRTUAL_DEVICE_PATTERN = /(steam|vb[-\s]?cable|virtual|voicemeeter|obs)/i;

// ============ VAD (Voice Activity Detection, v1.13.1-fix10/11, KI-140) ============
// Все параметры читаются из data-* на #chat-messages (RULES § 4.17).
// Defaults — fallback, если data-* отсутствуют (старый Razor-шаблон).

/** Включён ли VAD. Default: true. */
let VAD_ENABLED = true;

/**
 * Порог RMS для определения «тишины» (legacy, если adaptive выключен).
 * Типично: тихая комната — 0.001–0.005; шумная — 0.01–0.03; речь — 0.05–0.30.
 * Default: 0.015.
 */
let VAD_SILENCE_RMS = 0.015;

/**
 * v1.13.1-fix11c (KI-140): адаптивный VAD.
 * При включении порог вычисляется как max(minObservedRms × NoiseMultiplier, AbsoluteMinRms).
 * Это спасает тихие микрофоны (RMS речи < 0.015) — иначе VAD считает речь тишиной.
 * Default: true.
 */
let VAD_ADAPTIVE_ENABLED = true;

/** Множитель для адаптивного порога. Default: 2.0. */
let VAD_NOISE_MULTIPLIER = 2.0;

/** Абсолютный минимум порога RMS. Default: 0.001. */
let VAD_ABSOLUTE_MIN_RMS = 0.001;

/**
 * Длительность непрерывной тишины для auto-stop (мс). Default: 2000.
 */
let VAD_SILENCE_TIMEOUT_MS = 2000;

/**
 * Минимальная длительность записи до разрешения auto-stop (мс). Default: 700.
 */
let VAD_MIN_RECORDING_MS = 700;

/** Интервал опроса VAD (мс). Default: 200. */
let VAD_POLL_INTERVAL_MS = 200;

// ============ Состояние ============

let _stream = null;

/** Имя последнего использованного устройства — для сообщений об ошибке. v1.13.1-fix7. */
let _lastTrackLabel = null;

// Основной путь: MediaStreamTrackProcessor (WebCodecs)
let _trackProcessor = null;
let _trackReader = null;

// Fallback: Web Audio API
let _audioCtx = null;
let _source = null;
let _processor = null;

// Общий PCM-буфер: Array<{ pcm: Float32Array, sampleRate: number }>
let _pcmChunks = [];

let _isRecording = false;
let _recordingStartTime = 0;
let _recordingTimer = null;

// DOM-элементы
let _buttonEl = null;
let _textareaEl = null;
let _containerEl = null;

// v1.13.1-fix8 (KI-145): сохранённый пользователем микрофон (из /profile → Аудио).
// Ленивая загрузка при первом клике 🎤; кэш на время жизни страницы.
let _audioDeviceId = null;           // null = системный default
let _audioDeviceIdLoaded = false;
let _audioDeviceIdLoading = null;    // Promise, если идёт загрузка

// Флаг: уже показывали toast про fallback на default в этой сессии.
let _deviceFallbackToastShown = false;

// v1.13.1-fix9 (KI-140-fix): флаг — уже предупреждали о «виртуальном» микрофоне.
let _virtualDeviceToastShown = false;

// v1.13.1-fix10 (KI-140): VAD — auto-stop по тишине через AnalyserNode.
let _vadCtx = null;          // AudioContext для VAD
let _vadSource = null;       // MediaStreamAudioSourceNode
let _vadAnalyser = null;     // AnalyserNode
let _vadTimer = null;        // setInterval-таймер опроса
let _vadBuffer = null;       // Float32Array для getFloatTimeDomainData
let _vadStartedAt = 0;       // Date.now() старта VAD
let _vadLastVoiceTs = 0;     // Date.now() последнего «громкого» фрейма

// v1.13.1-fix11c (KI-140): минимум RMS, наблюдавшийся с начала записи.
// Только уменьшается. Определяет адаптивный порог тишины.
// Infinity — до первого тика. Сбрасывается в _startVad.
let _vadObservedMinRms = Infinity;

// ============ Загрузка конфигурации из data-* (v1.13.1-fix11, KI-140) ============

/**
 * Читает конфигурацию голосового ввода из `data-*` на `#chat-messages`.
 * Вызывается один раз в `initSpeechRecognition` (RULES § 4.17).
 *
 * <para>
 * Параметры: MAX_RECORDING_MS, VAD_ENABLED, VAD_SILENCE_RMS,
 * VAD_SILENCE_TIMEOUT_MS, VAD_MIN_RECORDING_MS, VAD_POLL_INTERVAL_MS.
 * </para>
 *
 * <para>
 * Если атрибут отсутствует — константа-фолбэк остаётся как есть. Это
 * нормальный сценарий при первом деплое нового Razor-шаблона с устаревшим
 * закэшированным HTML.
 * </para>
 */
function _loadRuntimeConfig() {
    const el = _containerEl || document.getElementById('chat-messages');
    if (!el) return;

    const num = (raw, fallback) => {
        if (raw === undefined || raw === null || raw === '') return fallback;
        // v1.13.1-fix11a (KI-140): защита от ru-RU локали.
        // Razor с CultureInfo.CurrentCulture может отдать "0,015" — parseFloat
        // останавливается на запятой и возвращает 0. Нормализуем к "0.015".
        // (Правильный fix — .ToString(InvariantCulture) в Razor; это — страховка.)
        const normalized = String(raw).replace(',', '.');
        const n = parseFloat(normalized);
        return Number.isFinite(n) ? n : fallback;
    };
    const bool = (raw, fallback) => {
        if (raw === undefined || raw === null) return fallback;
        return raw === 'true' || raw === true;
    };

    MAX_RECORDING_MS = num(el.dataset.speechMaxRecordMs, MAX_RECORDING_MS);
    VAD_ENABLED = bool(el.dataset.speechVadEnabled, VAD_ENABLED);
    VAD_SILENCE_RMS = num(el.dataset.speechVadSilenceRms, VAD_SILENCE_RMS);
    VAD_ADAPTIVE_ENABLED = bool(el.dataset.speechVadAdaptiveEnabled, VAD_ADAPTIVE_ENABLED);
    VAD_NOISE_MULTIPLIER = num(el.dataset.speechVadNoiseMultiplier, VAD_NOISE_MULTIPLIER);
    VAD_ABSOLUTE_MIN_RMS = num(el.dataset.speechVadAbsoluteMinRms, VAD_ABSOLUTE_MIN_RMS);
    VAD_SILENCE_TIMEOUT_MS = num(el.dataset.speechVadSilenceTimeoutMs, VAD_SILENCE_TIMEOUT_MS);
    VAD_MIN_RECORDING_MS = num(el.dataset.speechVadMinRecordingMs, VAD_MIN_RECORDING_MS);
    VAD_POLL_INTERVAL_MS = num(el.dataset.speechVadPollIntervalMs, VAD_POLL_INTERVAL_MS);

    console.info('[speech] Конфигурация из data-*:', {
        maxRecordingMs: MAX_RECORDING_MS,
        vadEnabled: VAD_ENABLED,
        vadAdaptiveEnabled: VAD_ADAPTIVE_ENABLED,
        vadSilenceRms: VAD_SILENCE_RMS,
        vadNoiseMultiplier: VAD_NOISE_MULTIPLIER,
        vadAbsoluteMinRms: VAD_ABSOLUTE_MIN_RMS,
        vadSilenceTimeoutMs: VAD_SILENCE_TIMEOUT_MS,
        vadMinRecordingMs: VAD_MIN_RECORDING_MS,
        vadPollIntervalMs: VAD_POLL_INTERVAL_MS,
    });
}

// ============ Публичное API ============

/**
 * Инициализирует модуль голосового ввода.
 *
 * @param {HTMLButtonElement} button — кнопка 🎤 (#btn-speech)
 * @param {HTMLTextAreaElement} textarea — целевая textarea (#chat-input)
 * @param {HTMLElement} container — контейнер с data-* (обычно #chat-messages).
 * @param {string} enabledFlag — 'true' | 'false' — Speech:Enabled из data-атрибута.
 */
export function initSpeechRecognition(button, textarea, container, enabledFlag) {
    if (!button || !textarea) return;

    _buttonEl = button;
    _textareaEl = textarea;
    _containerEl = container;

    // v1.13.1-fix11 (KI-140): загрузить конфиг VAD + MAX_RECORDING_MS из data-*.
    _loadRuntimeConfig();

    if (enabledFlag !== 'true') {
        button.disabled = true;
        button.title = _getLabel('labelSpeechDisabled', 'Голосовой ввод отключён');
        return;
    }

    button.disabled = false;
    button.addEventListener('click', _onButtonClick);

    // v1.13.1-fix10 (KI-140): глобальный хоткей Ctrl+Shift+Space.
    document.addEventListener('keydown', _onGlobalKeydown);

    button.title = _getLabel('labelSpeechTooltip', 'Голосовой ввод');
    button.setAttribute('aria-label', button.title);
}

/**
 * Убирает обработчики и освобождает ресурсы (для SPA-навигации / тестов).
 */
export function disposeSpeechRecognition() {
    if (_buttonEl) _buttonEl.removeEventListener('click', _onButtonClick);

    // v1.13.1-fix10 (KI-140): снять глобальный хоткей.
    document.removeEventListener('keydown', _onGlobalKeydown);

    _releaseAudioResources();
    _stopTimer();
    _stopStream();
    _isRecording = false;

    // v1.13.1-fix8 (KI-145): сбросить флаги toast — при повторной инициализации
    // пользователь снова увидит предупреждения (актуально для SPA).
    _deviceFallbackToastShown = false;
    _virtualDeviceToastShown = false;   // v1.13.1-fix9
}

// ============ Обработчики ============

function _onButtonClick() {
    if (_isRecording) _stopRecording();
    else _startRecording();
}

/**
 * v1.13.1-fix10 (KI-140): глобальный хоткей Ctrl+Shift+Space — start/stop
 * голосового ввода. Работает на всей странице /chat, включая фокус
 * в textarea (в этом случае Ctrl+Shift+Space обычно вводит &nbsp; —
 * перехватываем через preventDefault).
 *
 * @param {KeyboardEvent} e
 */
function _onGlobalKeydown(e) {
    // Только Ctrl+Shift+Space, без Alt/Meta (не пересекается с Ctrl+B/F/K
    // в chat.js — там нет shiftKey).
    if (!e.ctrlKey || !e.shiftKey || e.altKey || e.metaKey) return;

    // Разные браузеры по-разному отдают Space: e.code='Space', e.key=' '.
    const isSpace = e.code === 'Space' || e.key === ' ' || e.key === 'Spacebar';
    if (!isSpace) return;

    e.preventDefault();
    e.stopPropagation();
    _onButtonClick();
}

/**
 * Ленивая загрузка выбранного пользователем микрофона из /api/profile/settings
 * (v1.13.1-fix8, KI-145). Кэшируется на время жизни страницы.
 *
 * @returns {Promise<string|null>} deviceId или null (= системный default)
 */
async function _ensureAudioDeviceId() {
    if (_audioDeviceIdLoaded) return _audioDeviceId;
    if (_audioDeviceIdLoading) return _audioDeviceIdLoading;

    _audioDeviceIdLoading = (async () => {
        try {
            const res = await fetch('/api/profile/settings', {
                credentials: 'same-origin'
            }).then(r => r.json());

            if (res?.success && typeof res.data?.audioInputDeviceId === 'string') {
                const id = res.data.audioInputDeviceId.trim();
                _audioDeviceId = id.length > 0 ? id : null;
            } else {
                _audioDeviceId = null;
            }
        } catch (ex) {
            console.warn('[speech] Не удалось загрузить AudioInputDeviceId:', ex);
            _audioDeviceId = null;
        } finally {
            _audioDeviceIdLoaded = true;
            _audioDeviceIdLoading = null;
        }
        return _audioDeviceId;
    })();

    return _audioDeviceIdLoading;
}

/**
 * Показывает один раз за сессию toast о fallback на системный микрофон
 * (v1.13.1-fix8, KI-145).
 */
function _showDeviceFallbackToastOnce() {
    if (_deviceFallbackToastShown) return;
    _deviceFallbackToastShown = true;

    const msg = _getLabel(
        'labelSpeechDeviceFallback',
        'Сохранённый микрофон недоступен. Использован системный. Проверьте Профиль → Аудио.');

    try {
        toast(msg, 'warning');
    } catch (ex) {
        console.warn('[speech] toast failed:', ex);
    }
}

/**
 * v1.13.1-fix9 (KI-140-fix): если пользователь не выбрал микрофон явно,
 * а браузер отдал «виртуальное» устройство (Steam Streaming / VB-Cable /
 * VoiceMeeter / OBS Virtual Audio) — оно даст нулевой сигнал. Один раз
 * за сессию предупреждаем со ссылкой на /profile → Аудио.
 *
 * <para>
 * Показываем только когда <c>_audioDeviceId === null</c> (пользователь
 * не выбрал в /profile). Если выбрано явно — доверяем выбору.
 * </para>
 *
 * @param {string|null} trackLabel — <c>MediaStreamTrack.label</c>.
 */
function _maybeWarnAboutVirtualDevice(trackLabel) {
    if (_virtualDeviceToastShown) return;
    if (_audioDeviceId) return;                      // явный выбор — уважаем
    if (!trackLabel || trackLabel.length === 0) return;

    if (!VIRTUAL_DEVICE_PATTERN.test(trackLabel)) return;

    _virtualDeviceToastShown = true;

    const msg = _getLabel(
        'labelSpeechVirtualDeviceWarning',
        'Выбран виртуальный микрофон — возможен нулевой сигнал. Профиль → Аудио.');

    console.warn(
        '[speech] Виртуальный микрофон «%s» — рекомендуем выбрать физический в Профиль → Аудио',
        trackLabel);

    try {
        toast(msg, 'warning');
    } catch (ex) {
        console.warn('[speech] toast failed:', ex);
    }
}

async function _startRecording() {
    if (!navigator.mediaDevices?.getUserMedia) {
        _showError(_getLabel('labelSpeechErrorBrowser',
            'Браузер не поддерживает запись аудио'));
        return;
    }

    try {
        // v1.13.1-fix8 (KI-145): использовать сохранённый пользователем микрофон
        // (если задан в /profile → Аудио). Иначе — системный default.
        const savedDeviceId = await _ensureAudioDeviceId();

        const baseConstraints = {
            echoCancellation: false,
            noiseSuppression: false,
            autoGainControl: false,
            channelCount: 1,
        };

        try {
            const audioConstraints = savedDeviceId
                ? { ...baseConstraints, deviceId: { exact: savedDeviceId } }
                : baseConstraints;

            _stream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraints });
        } catch (ex) {
            // Fallback: сохранённое устройство недоступно (USB выдернули, драйвер обновили).
            // Различаем только типичные «device not available»-ошибки — отказ в разрешении
            // и прочие ошибки пробрасываем наружу как обычно.
            const isDeviceError = savedDeviceId && (
                ex.name === 'NotFoundError'
                || ex.name === 'OverconstrainedError'
                || ex.name === 'NotReadableError'
                || ex.name === 'AbortError');

            if (!isDeviceError) throw ex;

            console.warn(
                '[speech] Сохранённый микрофон недоступен (%s), используем системный default',
                ex.name);
            _showDeviceFallbackToastOnce();

            _stream = await navigator.mediaDevices.getUserMedia({ audio: baseConstraints });
        }

        const track = _stream.getAudioTracks()[0];
        _lastTrackLabel = track?.label || null;        
        const settings = track?.getSettings?.();
        // v1.13.1-fix7 (KI-140-fix): логируем label + deviceId — критично для
        // диагностики «виртуальных» устройств (Steam Streaming Microphone,
        // VB-Cable, OBS Virtual Audio) — они дают валидный трек с maxAbs=0.
        console.info('[speech] Микрофон применён:', {
            label: track?.label,                      // ← NEW: имя устройства
            deviceId: settings?.deviceId,             // ← NEW
            echoCancellation: settings?.echoCancellation,
            noiseSuppression: settings?.noiseSuppression,
            autoGainControl: settings?.autoGainControl,
            sampleRate: settings?.sampleRate,
            channelCount: settings?.channelCount,
            muted: track?.muted,
            enabled: track?.enabled,
        });

        // v1.13.1-fix9 (KI-140-fix): если трек — virtual device, а выбор
        // в /profile не сделан — предупреждаем один раз (см. KI-144).
        _maybeWarnAboutVirtualDevice(track?.label);

        _pcmChunks = [];
        _isRecording = true;
        _recordingStartTime = Date.now();
        _setRecordingUI(true);
        _startTimer();

        // v1.13.1-fix10 (KI-140): VAD — параллельный детектор тишины.
        // Запускается поверх _stream, независимо от основного пути чтения.
        _startVad();

        // v1.13.1-fix6: MediaStreamTrackProcessor (WebCodecs API) — основной путь.
        // Работает во всех Chromium 94+. Не подвержен pruning'у Web Audio.
        if (typeof MediaStreamTrackProcessor !== 'undefined') {
            try {
                _trackProcessor = new MediaStreamTrackProcessor({ track });
                _trackReader = _trackProcessor.readable.getReader();
                console.info('[speech] Используется MediaStreamTrackProcessor (WebCodecs)');
                _readMicrophoneLoop();   // fire-and-forget
                return;
            } catch (ex) {
                console.warn('[speech] MediaStreamTrackProcessor упал, fallback на Web Audio:', ex);
                _trackProcessor = null;
                _trackReader = null;
            }
        }

        // Fallback: Web Audio API (Firefox < 128, Safari, старый Chromium)
        console.info('[speech] Fallback: Web Audio API + ScriptProcessorNode');
        _setupWebAudioFallback();
    } catch (ex) {
        console.error('[speech] getUserMedia failed:', ex);
        const msg = (ex.name === 'NotAllowedError' || ex.name === 'PermissionDeniedError')
            ? _getLabel('labelSpeechErrorPermission', 'Нет доступа к микрофону')
            : 'Нет доступа к микрофону';
        _showError(msg);
        _releaseAudioResources();
        _stopStream();
        _isRecording = false;
        _stopTimer();
        _setRecordingUI(false);
    }
}

/**
 * Основной путь: чтение PCM через MediaStreamTrackProcessor (WebCodecs API).
 * Возвращает AudioData в формате f32-planar.
 */
async function _readMicrophoneLoop() {
    let diagCount = 0;
    try {
        while (_isRecording) {
            const { value, done } = await _trackReader.read();
            if (done || !value) break;

            const numFrames = value.numberOfFrames;
            const sampleRate = value.sampleRate;

            const pcm = new Float32Array(numFrames);
            value.copyTo(pcm, { planeIndex: 0, format: 'f32-planar' });
            value.close();

            _pcmChunks.push({ pcm, sampleRate });

            if (diagCount < 3) {
                diagCount++;
                let m = 0;
                for (let i = 0; i < pcm.length; i++) {
                    const a = Math.abs(pcm[i]);
                    if (a > m) m = a;
                }
                console.info(`[speech] track.read() #${diagCount}: ${numFrames} сэмплов @ ${sampleRate} Hz, maxAbs=${m.toFixed(6)}`);
            }
        }
    } catch (ex) {
        // AbortError при release() — ожидаемо
        if (ex.name !== 'AbortError') {
            console.error('[speech] track reader error:', ex);
        }
    } finally {
        try { _trackReader?.releaseLock(); } catch {}
        _trackReader = null;
    }
}

/**
 * Fallback: Web Audio API + ScriptProcessorNode.
 * Используется, если MediaStreamTrackProcessor недоступен.
 */
function _setupWebAudioFallback() {
    _audioCtx = new AudioContext();
    _audioCtx.resume().catch(() => {});

    const nativeSampleRate = _audioCtx.sampleRate;
    console.info(`[speech] AudioContext: state=${_audioCtx.state}, sampleRate=${nativeSampleRate}`);

    _source = _audioCtx.createMediaStreamSource(_stream);
    _processor = _audioCtx.createScriptProcessor(PROCESSOR_BUFFER_SIZE, 1, 1);

    let diagCount = 0;
    _processor.onaudioprocess = (e) => {
        if (!_isRecording) return;
        const input = e.inputBuffer.getChannelData(0);
        _pcmChunks.push({
            pcm: new Float32Array(input),
            sampleRate: nativeSampleRate
        });

        const output = e.outputBuffer.getChannelData(0);
        output.fill(0);

        if (diagCount < 3) {
            diagCount++;
            let m = 0;
            for (let i = 0; i < input.length; i++) {
                const a = Math.abs(input[i]);
                if (a > m) m = a;
            }
            console.info(`[speech] onaudioprocess #${diagCount}: ${input.length} сэмплов, maxAbs=${m.toFixed(6)}`);
        }
    };

    _source.connect(_processor);
    _processor.connect(_audioCtx.destination);
}

function _stopRecording() {
    if (!_isRecording) return;

    _isRecording = false;
    _stopTimer();
    _setRecordingUI(false);
    _setTranscribingUI(true);

    // Забираем накопленные PCM-чанки ДО освобождения ресурсов
    const chunks = _pcmChunks;
    _pcmChunks = [];

    _releaseAudioResources();
    _stopStream();

    _processAndSend(chunks);
}

// ============ VAD: Voice Activity Detection (v1.13.1-fix10, KI-140) ============

/**
 * Запускает VAD-детектор тишины параллельно с основной записью.
 *
 * <para>
 * Использует отдельный <c>AudioContext</c> + <c>AnalyserNode</c> поверх
 * того же <see cref="_stream"/>. Опрос RMS каждые {@link VAD_POLL_INTERVAL_MS} мс;
 * если тишина держится ≥ {@link VAD_SILENCE_TIMEOUT_MS} — вызывается
 * {@link _stopRecording}.
 * </para>
 *
 * <para>
 * Не критично для записи: если AudioContext недоступен, VAD молча
 * отключается (manual stop / 60-секундный auto-stop продолжат работать).
 * </para>
 */
function _startVad() {
    if (!VAD_ENABLED) {
        console.info('[speech] VAD отключён в конфигурации (Speech:Vad:Enabled=false)');
        return;
    }

    try {
        if (!_stream) return;

        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) {
            console.info('[speech] VAD: AudioContext недоступен — пропускаем');
            return;
        }

        _vadCtx = new Ctx();
        _vadSource = _vadCtx.createMediaStreamSource(_stream);
        _vadAnalyser = _vadCtx.createAnalyser();
        _vadAnalyser.fftSize = 1024;    // ~23 мс окно при 44.1 kHz
        _vadAnalyser.smoothingTimeConstant = 0;
        _vadSource.connect(_vadAnalyser);

        _vadBuffer = new Float32Array(_vadAnalyser.fftSize);
        _vadStartedAt = Date.now();
        _vadLastVoiceTs = _vadStartedAt;
        _vadObservedMinRms = Infinity;   // v1.13.1-fix11c: сброс для новой записи

        _vadTimer = setInterval(_vadTick, VAD_POLL_INTERVAL_MS);

        console.info(
            '[speech] VAD: запущен (adaptive=%s, порог=%s, множитель=%s, absMin=%s, тишина=%d мс, min=%d мс)',
            VAD_ADAPTIVE_ENABLED, VAD_SILENCE_RMS, VAD_NOISE_MULTIPLIER,
            VAD_ABSOLUTE_MIN_RMS, VAD_SILENCE_TIMEOUT_MS, VAD_MIN_RECORDING_MS);
    } catch (ex) {
        console.warn('[speech] VAD не запустился:', ex);
        _stopVad();
    }
}

/**
 * Периодический тик VAD. Читает RMS, обновляет `_vadLastVoiceTs`,
 * при длительной тишине вызывает `_stopRecording`.
 */
function _vadTick() {
    if (!_isRecording || !_vadAnalyser || !_vadBuffer) return;

    try {
        _vadAnalyser.getFloatTimeDomainData(_vadBuffer);
    } catch {
        return;
    }

    let sumSq = 0;
    for (let i = 0; i < _vadBuffer.length; i++) {
        sumSq += _vadBuffer[i] * _vadBuffer[i];
    }
    const rms = Math.sqrt(sumSq / _vadBuffer.length);

    // v1.13.1-fix11c (KI-140): обновляем наблюдаемый минимум RMS
    // (только в сторону уменьшения). Определяет адаптивный порог.
    if (rms < _vadObservedMinRms) {
        _vadObservedMinRms = rms;
    }

    // Эффективный порог тишины:
    //   adaptive = true: max(minRms × multiplier, absoluteMin)
    //   adaptive = false: silenceRms (legacy)
    // Первый тик (minRms = Infinity) — порог = Infinity, всегда «тишина»;
    // но это безопасно, т.к. VAD_MIN_RECORDING_MS = 700 мс — окно
    // «набора» минимального RMS.
    let effectiveThreshold;
    if (VAD_ADAPTIVE_ENABLED) {
        effectiveThreshold = Math.max(
            _vadObservedMinRms * VAD_NOISE_MULTIPLIER,
            VAD_ABSOLUTE_MIN_RMS);
    } else {
        effectiveThreshold = VAD_SILENCE_RMS;
    }

    const now = Date.now();

    if (rms >= effectiveThreshold) {
        // Голос/шум выше порога — продлеваем таймаут.
        _vadLastVoiceTs = now;
        return;
    }

    // Тишина.
    const recordingMs = now - _vadStartedAt;
    if (recordingMs < VAD_MIN_RECORDING_MS) return;

    const silenceMs = now - _vadLastVoiceTs;
    if (silenceMs >= VAD_SILENCE_TIMEOUT_MS) {
        console.info(
            '[speech] VAD: auto-stop — тишина %d мс (RMS < %s, min=%s, mode=%s)',
            silenceMs, effectiveThreshold.toFixed(5),
            _vadObservedMinRms.toFixed(5),
            VAD_ADAPTIVE_ENABLED ? 'adaptive' : 'fixed');
        _stopRecording();
    }
}

/**
 * Останавливает VAD: таймер, source, analyser, AudioContext.
 * Идемпотентно — безопасно вызывать повторно.
 */
function _stopVad() {
    if (_vadTimer) {
        clearInterval(_vadTimer);
        _vadTimer = null;
    }
    if (_vadSource) {
        try { _vadSource.disconnect(); } catch { /* ignore */ }
        _vadSource = null;
    }
    if (_vadAnalyser) {
        try { _vadAnalyser.disconnect(); } catch { /* ignore */ }
        _vadAnalyser = null;
    }
    if (_vadCtx) {
        try { _vadCtx.close(); } catch { /* ignore */ }
        _vadCtx = null;
    }
    _vadBuffer = null;
}

/**
 * Освобождает ресурсы: MediaStreamTrackProcessor reader + Web Audio API.
 */
function _releaseAudioResources() {
    // v1.13.1-fix10 (KI-140): остановить VAD первым — он использует _stream.
    _stopVad();

    // WebCodecs track processor
    if (_trackReader) {
        try { _trackReader.cancel(); } catch {}
        try { _trackReader.releaseLock(); } catch {}
        _trackReader = null;
    }
    _trackProcessor = null;

    // Web Audio fallback
    if (_processor) {
        try { _processor.onaudioprocess = null; } catch {}
        try { _processor.disconnect(); } catch {}
        _processor = null;
    }
    if (_source) {
        try { _source.disconnect(); } catch {}
        _source = null;
    }
    if (_audioCtx) {
        try { _audioCtx.close(); } catch {}
        _audioCtx = null;
    }
}

/**
 * Обрабатывает накопленные PCM-чанки и отправляет на сервер.
 * @param {Array<{ pcm: Float32Array, sampleRate: number }>} chunks
 */
async function _processAndSend(chunks) {
    try {
        if (!chunks || chunks.length === 0) {
            _showError('Не удалось записать аудио');
            return;
        }

        // 1. Собираем все чанки в один Float32Array + запоминаем sampleRate
        const nativeSampleRate = chunks[0].sampleRate;
        const totalLen = chunks.reduce((sum, c) => sum + c.pcm.length, 0);
        if (totalLen === 0) {
            _showError('Не удалось записать аудио');
            return;
        }

        const merged = new Float32Array(totalLen);
        let offset = 0;
        for (const c of chunks) {
            merged.set(c.pcm, offset);
            offset += c.pcm.length;
        }

        // 2. RMS до нормализации
        const rmsBefore = _computeRms(merged);
        console.info(`[speech] PCM: ${merged.length} сэмплов @ ${nativeSampleRate} Hz, RMS=${rmsBefore.toFixed(5)}`);

        // 3. Peak normalization
        _normalizePeak(merged, NORMALIZE_TARGET_PEAK);

        // 4. Ресемплинг до 16 kHz
        const resampled = await _resampleTo16k(merged, nativeSampleRate);

        // 5. RMS после
        const rmsAfter = _computeRms(resampled);
        console.info(`[speech] Ресемплировано: ${resampled.length} сэмплов @ ${TARGET_SAMPLE_RATE} Hz, RMS=${rmsAfter.toFixed(5)}`);

        // 6. Ранний выход при нулевом сигнале (защита от галлюцинаций Whisper)
        // v1.13.1-fix7 (KI-140-fix): называем устройство и подсказываем где смотреть.
        // Steam Streaming / VB-Cable / OBS Virtual Audio дают валидный трек с maxAbs=0.
        if (rmsAfter < 1e-5) {
            const deviceLabel = _lastTrackLabel || 'неизвестное устройство';
            console.warn(
                '[speech] Входной сигнал полностью нулевой — микрофон «%s» не передаёт данные',
                deviceLabel);
            _showError(
                `Микрофон «${deviceLabel}» не передаёт данные. ` +
                `Откройте chrome://settings/content/microphone и выберите ` +
                `физический микрофон (не Steam Streaming / VB-Cable / Virtual Audio).`
            );
            return;
        }

        // 7. WAV 16-bit mono
        const wavBlob = _encodeWav(resampled, TARGET_SAMPLE_RATE);
        console.info(`[speech] WAV: ${wavBlob.size} байт`);

        // 8. Отправляем
        const fd = new FormData();
        fd.append('file', wavBlob, 'audio.wav');

        const response = await fetch('/api/speech/transcribe', {
            method: 'POST',
            credentials: 'same-origin',
            body: fd,
        });
        const res = await response.json();

        if (!res || !res.success) {
            _showError((res && res.message) || 'Ошибка распознавания');
            return;
        }

        // 9. Вставляем текст в textarea
        const ta = _textareaEl;
        if (!ta) return;
        const prefix = ta.value.trim() ? ta.value + ' ' : '';
        ta.value = prefix + (res.data.text || '');
        ta.dispatchEvent(new Event('input', { bubbles: true }));
        ta.focus();
        ta.scrollTop = ta.scrollHeight;
    } catch (ex) {
        console.error('[speech] process/send failed:', ex);
        _showError('Ошибка обработки аудио');
    } finally {
        _setTranscribingUI(false);
    }
}

// ============ Ресемплинг + нормализация ============

/**
 * Ресемплирует Float32Array до 16 kHz через OfflineAudioContext.
 */
async function _resampleTo16k(samples, nativeRate) {
    if (nativeRate === TARGET_SAMPLE_RATE) return samples;

    const duration = samples.length / nativeRate;
    const outLength = Math.max(1, Math.ceil(duration * TARGET_SAMPLE_RATE));

    const offlineCtx = new OfflineAudioContext(1, outLength, TARGET_SAMPLE_RATE);
    const buffer = offlineCtx.createBuffer(1, samples.length, nativeRate);
    buffer.getChannelData(0).set(samples);

    const source = offlineCtx.createBufferSource();
    source.buffer = buffer;
    source.connect(offlineCtx.destination);
    source.start();

    const rendered = await offlineCtx.startRendering();
    return rendered.getChannelData(0);
}

/**
 * Peak-нормализация.
 */
function _normalizePeak(samples, targetPeak) {
    let maxAbs = 0;
    for (let i = 0; i < samples.length; i++) {
        const a = Math.abs(samples[i]);
        if (a > maxAbs) maxAbs = a;
    }
    if (maxAbs < NORMALIZE_MIN_PEAK) return;
    if (maxAbs >= targetPeak) return;

    let gain = targetPeak / maxAbs;
    if (gain > NORMALIZE_MAX_GAIN) gain = NORMALIZE_MAX_GAIN;

    for (let i = 0; i < samples.length; i++) {
        samples[i] *= gain;
    }
    console.info(`[speech] Peak normalize: maxAbs=${maxAbs.toFixed(4)}, gain=${gain.toFixed(2)}×`);
}

function _computeRms(samples) {
    if (!samples || samples.length === 0) return 0;
    let sumSq = 0;
    for (let i = 0; i < samples.length; i++) {
        sumSq += samples[i] * samples[i];
    }
    return Math.sqrt(sumSq / samples.length);
}

// ============ WAV-энкодинг ============

function _encodeWav(samples, sampleRate) {
    const numChannels = 1;
    const bytesPerSample = 2;
    const blockAlign = numChannels * bytesPerSample;
    const byteRate = sampleRate * blockAlign;
    const dataSize = samples.length * bytesPerSample;

    const buffer = new ArrayBuffer(44 + dataSize);
    const view = new DataView(buffer);

    _writeString(view, 0, 'RIFF');
    view.setUint32(4, 36 + dataSize, true);
    _writeString(view, 8, 'WAVE');
    _writeString(view, 12, 'fmt ');
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, byteRate, true);
    view.setUint16(32, blockAlign, true);
    view.setUint16(34, 16, true);
    _writeString(view, 36, 'data');
    view.setUint32(40, dataSize, true);

    let offset = 44;
    for (let i = 0; i < samples.length; i++, offset += 2) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
    }

    return new Blob([buffer], { type: 'audio/wav' });
}

function _writeString(view, offset, str) {
    for (let i = 0; i < str.length; i++) {
        view.setUint8(offset + i, str.charCodeAt(i));
    }
}

// ============ UI helpers ============

function _setRecordingUI(recording) {
    if (!_buttonEl) return;
    _buttonEl.classList.toggle('recording', recording);
    const label = recording
        ? _getLabel('labelSpeechRecording', 'Идёт запись…')
        : _getLabel('labelSpeechTooltip', 'Голосовой ввод');
    _buttonEl.setAttribute('aria-label', label);
    _buttonEl.title = label;
}

function _setTranscribingUI(transcribing) {
    if (!_buttonEl) return;
    _buttonEl.classList.toggle('transcribing', transcribing);
    _buttonEl.disabled = transcribing;
    if (transcribing) {
        _buttonEl.title = _getLabel('labelSpeechTranscribing', 'Распознаю…');
    }
}

function _startTimer() {
    if (_recordingTimer) clearInterval(_recordingTimer);
    _recordingTimer = setInterval(() => {
        const elapsedMs = Date.now() - _recordingStartTime;
        const sec = Math.floor(elapsedMs / 1000);
        const mm = String(Math.floor(sec / 60)).padStart(2, '0');
        const ss = String(sec % 60).padStart(2, '0');
        if (_buttonEl) _buttonEl.dataset.timer = `${mm}:${ss}`;

        if (elapsedMs >= MAX_RECORDING_MS && _isRecording) {
            console.info('[speech] auto-stop: достигнут лимит 60 сек');
            _stopRecording();
        }
    }, TIMER_INTERVAL_MS);
}

function _stopTimer() {
    if (_recordingTimer) {
        clearInterval(_recordingTimer);
        _recordingTimer = null;
    }
    if (_buttonEl) delete _buttonEl.dataset.timer;
}

function _stopStream() {
    if (_stream) {
        _stream.getTracks().forEach(t => t.stop());
        _stream = null;
    }
}

function _showError(message) {
    if (!_buttonEl) return;
    _buttonEl.classList.add('error');
    _buttonEl.title = message;
    setTimeout(() => {
        _buttonEl.classList.remove('error');
        _buttonEl.title = _getLabel('labelSpeechTooltip', 'Голосовой ввод');
    }, 3000);
}

function _getLabel(key, fallback) {
    const el = _containerEl || document.getElementById('chat-messages');
    if (!el) return fallback || '';
    return el.dataset[key] || fallback || '';
}