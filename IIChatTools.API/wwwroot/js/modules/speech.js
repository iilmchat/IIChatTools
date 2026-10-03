/**
 * Модуль офлайн-распознавания речи в чате (v1.13.0, KI-140).
 *
 * Использует:
 *   - MediaRecorder (запись WebM/Opus);
 *   - Web Audio API + OfflineAudioContext (ресемплинг в WAV 16 kHz mono);
 *   - Whisper.net backend (POST /api/speech/transcribe).
 *
 * Принципы:
 *   - Вся обработка — локально, аудио не покидает сервер приложения.
 *   - Auto-stop по 60 сек (синхронизировано со Speech:MaxAudioSeconds).
 *   - Текст вставляется в textarea (не отправляется автоматически).
 *
 * © 2026 RuChating (iilmchat) · IIChatTools v1.13.0
 */

// ============ Состояние ============

/** Лимит длительности записи (мс). Синхронизировано со Speech:MaxAudioSeconds (60). */
const MAX_RECORDING_MS = 60_000;

/** Интервал обновления таймера в UI (мс). */
const TIMER_INTERVAL_MS = 500;

let _mediaRecorder = null;
let _audioChunks = [];
let _stream = null;
let _isRecording = false;
let _recordingStartTime = 0;
let _recordingTimer = null;

/** DOM-элементы, привязанные при инициализации. */
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

    // Если Speech:Enabled = false — кнопка остаётся disabled, обработчик не вешаем.
    if (enabledFlag !== 'true') {
        button.disabled = true;
        button.title = _getLabel('labelSpeechDisabled', 'Голосовой ввод отключён');
        return;
    }

    // Активируем кнопку и вешаем обработчик.
    button.disabled = false;
    button.addEventListener('click', _onButtonClick);

    // Проставляем локализованные лейблы из data-*
    button.title = _getLabel('labelSpeechTooltip', 'Голосовой ввод');
    button.setAttribute('aria-label', button.title);
}

/**
 * Убирает обработчики и освобождает ресурсы (для SPA-навигации / тестов).
 */
export function disposeSpeechRecognition() {
    if (_buttonEl) _buttonEl.removeEventListener('click', _onButtonClick);
    if (_recordingTimer) { clearInterval(_recordingTimer); _recordingTimer = null; }
    if (_stream) {
        _stream.getTracks().forEach(t => t.stop());
        _stream = null;
    }
    _mediaRecorder = null;
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
        _stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        _audioChunks = [];

        // Выбираем поддерживаемый MIME. Chrome/Firefox/Edge: webm/opus.
        // Safari: audio/mp4 (некоторые версии). Fallback — дефолт браузера.
        const mimeType = _pickSupportedMimeType();
        _mediaRecorder = mimeType
            ? new MediaRecorder(_stream, { mimeType })
            : new MediaRecorder(_stream);

        _mediaRecorder.ondataavailable = (e) => {
            if (e.data.size > 0) _audioChunks.push(e.data);
        };
        _mediaRecorder.onstop = _onRecordingStop;
        _mediaRecorder.onerror = (e) => {
            console.error('[speech] MediaRecorder error:', e);
            _showError('Ошибка записи');
        };
        _mediaRecorder.start();

        _isRecording = true;
        _recordingStartTime = Date.now();
        _setRecordingUI(true);
        _startTimer();
    } catch (ex) {
        console.error('[speech] getUserMedia failed:', ex);
        const msg = (ex.name === 'NotAllowedError' || ex.name === 'PermissionDeniedError')
            ? _getLabel('labelSpeechErrorPermission', 'Нет доступа к микрофону')
            : 'Нет доступа к микрофону';
        _showError(msg);
    }
}

function _stopRecording() {
    if (!_mediaRecorder) return;
    try {
        _mediaRecorder.stop();
    } catch (ex) {
        console.warn('[speech] stop() failed:', ex);
    }
    _stopStream();
    _isRecording = false;
    _stopTimer();
    _setRecordingUI(false);
    _setTranscribingUI(true);
}

function _onRecordingStop() {
    (async () => {
        try {
            const blob = new Blob(_audioChunks, { type: 'audio/webm' });
            const wavBlob = await _webmToWav(blob);

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

            // Вставляем текст в textarea (в конец, если уже что-то есть).
            const ta = _textareaEl;
            if (!ta) return;
            const prefix = ta.value.trim() ? ta.value + ' ' : '';
            ta.value = prefix + (res.data.text || '');

            // Триггерим input → autoResizeTextarea в chat.js подхватит.
            ta.dispatchEvent(new Event('input', { bubbles: true }));
            ta.focus();

            // Прокручиваем textarea к концу.
            ta.scrollTop = ta.scrollHeight;
        } catch (ex) {
            console.error('[speech] transcribe failed:', ex);
            _showError('Ошибка обработки аудио');
        } finally {
            _setTranscribingUI(false);
        }
    })();
}

// ============ WAV-энкодинг ============

/**
 * Конвертирует WebM/Opus (или иной формат MediaRecorder) → WAV 16 kHz mono.
 *
 * ВАЖНО: `new AudioContext({ sampleRate: 16000 })` — это hint, не гарантия.
 * Chrome игнорирует его для decodeAudioData — возвращает buffer в native rate
 * (обычно 48 kHz). Whisper.net ожидает WAV 16 kHz — поэтому делаем явный
 * ресемплинг через OfflineAudioContext.
 */
async function _webmToWav(blob) {
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
    const length = Math.max(1, Math.ceil(audioBuffer.duration * targetRate));
    const offlineCtx = new OfflineAudioContext(1, length, targetRate);
    const source = offlineCtx.createBufferSource();
    source.buffer = audioBuffer;
    source.connect(offlineCtx.destination);
    source.start();
    const resampled = await offlineCtx.startRendering();

    return _encodeWav(resampled);
}

/**
 * Кодирует AudioBuffer в WAV (RIFF PCM 16-bit mono).
 * @param {AudioBuffer} audioBuffer
 * @returns {Blob}
 */
function _encodeWav(audioBuffer) {
    const numChannels = 1;
    const sampleRate = audioBuffer.sampleRate;
    const samples = audioBuffer.getChannelData(0);
    const buffer = new ArrayBuffer(44 + samples.length * 2);
    const view = new DataView(buffer);

    _writeString(view, 0, 'RIFF');
    view.setUint32(4, 36 + samples.length * 2, true);
    _writeString(view, 8, 'WAVE');
    _writeString(view, 12, 'fmt ');
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);          // PCM
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * numChannels * 2, true);  // byte rate
    view.setUint16(32, numChannels * 2, true);               // block align
    view.setUint16(34, 16, true);                            // bits per sample
    _writeString(view, 36, 'data');
    view.setUint32(40, samples.length * 2, true);

    let offset = 44;
    for (let i = 0; i < samples.length; i++, offset += 2) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
    }

    return new Blob([view], { type: 'audio/wav' });
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

        // Auto-stop: превысили 60 сек → останавливаем и транскрибируем.
        // MAX_RECORDING_MS синхронизирован со Speech:MaxAudioSeconds (60).
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

function _pickSupportedMimeType() {
    const candidates = [
        'audio/webm;codecs=opus',
        'audio/webm',
        'audio/ogg;codecs=opus',
        'audio/mp4',
    ];
    for (const mime of candidates) {
        if (window.MediaRecorder?.isTypeSupported?.(mime)) return mime;
    }
    return null;
}

/**
 * Читает локализованный лейбл из data-* на контейнере (#chat-messages).
 * @param {string} key — camelCase ключ (например, 'labelSpeechTooltip').
 * @param {string} fallback — если нет в data-*.
 * @returns {string}
 */
function _getLabel(key, fallback) {
    const el = _containerEl || document.getElementById('chat-messages');
    if (!el) return fallback || '';
    return el.dataset[key] || fallback || '';
}