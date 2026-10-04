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

// ============ Константы ============

/** Лимит длительности записи (мс). Синхронизировано со Speech:MaxAudioSeconds (60). */
const MAX_RECORDING_MS = 60_000;

/** Интервал обновления таймера в UI (мс). */
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

    if (enabledFlag !== 'true') {
        button.disabled = true;
        button.title = _getLabel('labelSpeechDisabled', 'Голосовой ввод отключён');
        return;
    }

    button.disabled = false;
    button.addEventListener('click', _onButtonClick);

    button.title = _getLabel('labelSpeechTooltip', 'Голосовой ввод');
    button.setAttribute('aria-label', button.title);
}

/**
 * Убирает обработчики и освобождает ресурсы (для SPA-навигации / тестов).
 */
export function disposeSpeechRecognition() {
    if (_buttonEl) _buttonEl.removeEventListener('click', _onButtonClick);
    _releaseAudioResources();
    _stopTimer();
    _stopStream();
    _isRecording = false;
}

// ============ Обработчики ============

function _onButtonClick() {
    if (_isRecording) _stopRecording();
    else _startRecording();
}

async function _startRecording() {
    if (!navigator.mediaDevices?.getUserMedia) {
        _showError(_getLabel('labelSpeechErrorBrowser',
            'Браузер не поддерживает запись аудио'));
        return;
    }

    try {
        _stream = await navigator.mediaDevices.getUserMedia({
            audio: {
                echoCancellation: false,
                noiseSuppression: false,
                autoGainControl: false,
                channelCount: 1,
            }
        });

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

        _pcmChunks = [];
        _isRecording = true;
        _recordingStartTime = Date.now();
        _setRecordingUI(true);
        _startTimer();

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

/**
 * Освобождает ресурсы: MediaStreamTrackProcessor reader + Web Audio API.
 */
function _releaseAudioResources() {
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