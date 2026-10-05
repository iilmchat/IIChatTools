/**
 * Модуль карточки «🎤 Аудио» на странице /profile (v1.13.1-fix8, KI-145).
 *
 * Позволяет пользователю явно выбрать микрофон для голосового ввода в /chat.
 * Без явного выбора Chrome может использовать «виртуальное» устройство
 * (Steam Streaming Microphone / VB-Cable / VoiceMeeter / OBS Virtual Audio),
 * которое даёт валидный MediaStreamTrack, но нулевой сигнал (см. KI-144).
 *
 * Endpoints:
 *   GET /api/profile/settings   — читает audioInputDeviceId (read-only поле)
 *   PUT /api/profile/audio-device — сохраняет выбранный deviceId
 *
 * © 2026 RuChating (iilmchat) · IIChatTools v1.13.1
 */
import { apiGet } from './api.js';
import { escapeHtml, toast } from './ui.js';

/** Длительность тестовой записи (мс). */
const TEST_DURATION_MS = 2000;

/** Порог maxAbs, ниже которого считаем сигнал «нулевым». */
const TEST_MAXABS_THRESHOLD = 1e-4;

// ---------- Состояние ----------
const state = {
    /** @type {string|null} — сохранённый deviceId (null = системный default). */
    savedDeviceId: null,
    /** @type {MediaDeviceInfo[]} */
    devices: [],
    /** @type {boolean} — защита от двойного клика «Протестировать». */
    testing: false
};

/**
 * Инициализирует карточку «Аудио» на странице /profile.
 */
export function initProfileAudioCard() {
    const card = document.getElementById('profile-audio-card');
    if (!card) return;

    document.getElementById('btn-profile-audio-request-permission')
        ?.addEventListener('click', onRequestPermission);

    document.getElementById('profile-audio-device')
        ?.addEventListener('change', onDeviceChanged);

    document.getElementById('btn-profile-audio-test')
        ?.addEventListener('click', onTestDevice);

    // Асинхронная инициализация — не блокирует остальную страницу.
    void init();
}

async function init() {
    // 1. Загружаем сохранённый deviceId (может быть null).
    state.savedDeviceId = await loadSavedDeviceId();

    // 2. Проверяем, дано ли разрешение на микрофон.
    const hasPermission = await hasMicrophonePermission();

    // 3. Если разрешение уже есть — показываем состояние B.
    if (hasPermission) {
        await showDeviceState();
    }
    // Иначе остаёмся в состоянии A (кнопка «Разрешить доступ»).
}

/**
 * Читает сохранённый пользователем deviceId из /api/profile/settings.
 * @returns {Promise<string|null>}
 */
async function loadSavedDeviceId() {
    try {
        const res = await apiGet('/api/profile/settings');
        if (res?.success && typeof res.data?.audioInputDeviceId === 'string') {
            const id = res.data.audioInputDeviceId.trim();
            return id.length > 0 ? id : null;
        }
    } catch (ex) {
        console.warn('[profile-audio] Не удалось загрузить settings:', ex);
    }
    return null;
}

/**
 * Проверяет, выдано ли разрешение на микрофон.
 * Сначала Permissions API, при неудаче — эвристика по labels.
 * @returns {Promise<boolean>}
 */
async function hasMicrophonePermission() {
    try {
        if (navigator.permissions?.query) {
            const result = await navigator.permissions.query({ name: 'microphone' });
            return result.state === 'granted';
        }
    } catch {
        // Firefox не поддерживает name: 'microphone' — fallback ниже.
    }

    // Fallback: если хоть одно audioinput имеет непустой label → разрешение выдано.
    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const inputs = devices.filter(d => d.kind === 'audioinput');
        return inputs.some(d => d.label && d.label.trim().length > 0);
    } catch {
        return false;
    }
}

/**
 * Обработчик кнопки «Разрешить доступ к микрофону».
 * Триггерит getUserMedia, останавливает трек, показывает список устройств.
 */
async function onRequestPermission() {
    const btn = document.getElementById('btn-profile-audio-request-permission');
    const status = document.getElementById('profile-audio-permission-status');
    const card = document.getElementById('profile-audio-card');

    if (btn) btn.disabled = true;
    if (status) status.textContent = '';

    try {
        // Триггерим запрос разрешения (одноразово).
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        stream.getTracks().forEach(t => t.stop());

        if (status) {
            status.textContent = card?.dataset.labelPermissionGranted || 'OK';
        }

        await showDeviceState();
    } catch (ex) {
        console.warn('[profile-audio] getUserMedia failed:', ex);
        const msg = card?.dataset.labelPermissionDenied || 'Permission denied';
        if (status) status.textContent = msg;
        toast(msg, 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

/**
 * Переключает UI из состояния A (нет разрешения) в состояние B (список устройств).
 */
async function showDeviceState() {
    const permState = document.getElementById('profile-audio-permission-state');
    const deviceState = document.getElementById('profile-audio-device-state');
    if (permState) permState.hidden = true;
    if (deviceState) deviceState.hidden = false;

    await populateDevices();
}

/**
 * Префиксы, которые Chrome добавляет к label одного и того же
 * физического устройства (v1.13.x-fix12, KI-146).
 * Пример: «Микрофон (X)», «По умолчанию — Микрофон (X)»,
 * «Оборудование — Микрофон (X)». Все три deviceId ведут к одному железу.
 */
const DEVICE_LABEL_PREFIXES = [
    /^По умолчанию\s*—\s*/i,
    /^По умолчанию\s*-\s*/i,
    /^Оборудование\s*—\s*/i,
    /^Оборудование\s*-\s*/i,
    /^Default\s*—\s*/i,
    /^Default\s*-\s*/i,
    /^Communications\s*—\s*/i,
    /^Communications\s*-\s*/i,
];

/**
 * Убирает служебные префиксы из label устройства (v1.13.x-fix12, KI-146).
 * @param {string} label
 * @returns {string}
 */
function normalizeDeviceLabel(label) {
    let result = label || '';
    for (const prefix of DEVICE_LABEL_PREFIXES) {
        result = result.replace(prefix, '');
    }
    return result.trim();
}

/**
 * Заполняет <select> списком audioinput-устройств.
 * v1.13.x-fix12 (KI-146): с дедупликацией по нормализованному label.
 */
async function populateDevices() {
    const select = document.getElementById('profile-audio-device');
    if (!select) return;

    const devices = await navigator.mediaDevices.enumerateDevices();
    state.devices = devices.filter(d => d.kind === 'audioinput');

    const card = document.getElementById('profile-audio-card');
    const defaultLabel = card?.dataset.labelDeviceDefault || 'Default';

    // v1.13.x-fix12 (KI-146): дедупликация. Chrome перечисляет одно физ.
    // устройство как 3 разных deviceId с префиксами в label. Группируем
    // по нормализованному label. Внутри группы выбираем deviceId, который
    // совпадает с savedDeviceId (иначе select.value не подсветит сохранённый
    // выбор), иначе — первый по порядку enumerateDevices.
    const groups = new Map(); // normalizedLower → MediaDeviceInfo[]
    for (const d of state.devices) {
        const normalized = normalizeDeviceLabel(d.label);
        const key = normalized.toLowerCase();
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(d);
    }

    const uniqueDevices = [];
    for (const group of groups.values()) {
        const picked = group.find(d => d.deviceId === state.savedDeviceId) || group[0];
        const displayLabel = normalizeDeviceLabel(picked.label)
            || picked.label
            || `Микрофон (${picked.deviceId.substring(0, 8)}…)`;
        uniqueDevices.push({
            deviceId: picked.deviceId,
            label: displayLabel,
        });
    }

    let html = `<option value="">${escapeHtml(defaultLabel)}</option>`;
    for (const d of uniqueDevices) {
        const selected = d.deviceId === state.savedDeviceId ? ' selected' : '';
        html += `<option value="${escapeHtml(d.deviceId)}"${selected}>${escapeHtml(d.label)}</option>`;
    }

    select.innerHTML = html;

    // Если savedDeviceId вообще отсутствует среди текущих устройств (не только
    // среди дублей) — сбрасываем на default.
    if (state.savedDeviceId && !state.devices.some(d => d.deviceId === state.savedDeviceId)) {
        console.warn('[profile-audio] Сохранённое устройство не найдено в списке — сброс на default');
        state.savedDeviceId = null;
    }

    select.value = state.savedDeviceId || '';
}

/**
 * Обработчик изменения <select> — сохраняет deviceId через API.
 * @param {Event} e
 */
async function onDeviceChanged(e) {
    const newId = e.target.value || null;
    state.savedDeviceId = newId;
    await saveDeviceId(newId);
}

/**
 * Сохраняет выбранный deviceId через PUT /api/profile/audio-device.
 * @param {string|null} deviceId — null/'' = сбросить на системный default.
 */
async function saveDeviceId(deviceId) {
    const card = document.getElementById('profile-audio-card');
    try {
        const res = await fetch('/api/profile/audio-device', {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ audioInputDeviceId: deviceId || '' })
        }).then(r => r.json());

        if (!res?.success) {
            toast(res?.message || card?.dataset.labelSaveError || 'Save error', 'error');
            return;
        }

        toast(card?.dataset.labelSaveSuccess || 'Saved', 'success');
    } catch (ex) {
        toast(ex.message || card?.dataset.labelSaveError || 'Save error', 'error');
    }
}

/**
 * Обработчик кнопки «Протестировать» — записывает 2 сек, показывает RMS/maxAbs.
 */
async function onTestDevice() {
    if (state.testing) return;
    state.testing = true;

    const btn = document.getElementById('btn-profile-audio-test');
    const result = document.getElementById('profile-audio-test-result');
    const card = document.getElementById('profile-audio-card');
    const select = document.getElementById('profile-audio-device');

    if (btn) btn.disabled = true;
    if (result) {
        result.textContent = card?.dataset.labelTestRecording || 'Recording…';
        result.className = 'ms-2 small text-muted';
    }

    const deviceId = select?.value || null;

    try {
        const r = await testAudioDevice(deviceId);
        const isOk = r.maxAbs > TEST_MAXABS_THRESHOLD;

        const tpl = isOk
            ? (card?.dataset.labelTestSuccess || '✓ RMS: {0} · maxAbs: {1}')
            : (card?.dataset.labelTestFailed || '✗ Zero signal');

        const text = tpl
            .replace('{0}', r.rms.toFixed(4))
            .replace('{1}', r.maxAbs.toFixed(4));

        if (result) {
            result.textContent = text;
            result.className = `ms-2 small ${isOk ? 'text-success' : 'text-danger'}`;
        }
    } catch (ex) {
        console.warn('[profile-audio] test failed:', ex);
        if (result) {
            result.textContent = card?.dataset.labelTestFailed || 'Failed';
            result.className = 'ms-2 small text-danger';
        }
    } finally {
        state.testing = false;
        if (btn) btn.disabled = false;
    }
}

/**
 * Записывает ~2 секунды через MediaStreamTrackProcessor и считает RMS / maxAbs.
 * @param {string|null} deviceId
 * @returns {Promise<{ rms: number, maxAbs: number, samples: number }>}
 */
async function testAudioDevice(deviceId) {
    const baseConstraints = {
        echoCancellation: false,
        noiseSuppression: false,
        autoGainControl: false,
        channelCount: 1
    };

    const audioConstraints = deviceId
        ? { ...baseConstraints, deviceId: { exact: deviceId } }
        : baseConstraints;

    const stream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraints });
    const track = stream.getAudioTracks()[0];

    if (typeof MediaStreamTrackProcessor === 'undefined') {
        stream.getTracks().forEach(t => t.stop());
        throw new Error('MediaStreamTrackProcessor not supported');
    }

    const processor = new MediaStreamTrackProcessor({ track });
    const reader = processor.readable.getReader();

    const startTime = Date.now();
    let maxAbs = 0;
    let samples = 0;
    let sumSq = 0;

    try {
        while (Date.now() - startTime < TEST_DURATION_MS) {
            const { value, done } = await reader.read();
            if (done || !value) break;

            const pcm = new Float32Array(value.numberOfFrames);
            value.copyTo(pcm, { planeIndex: 0, format: 'f32-planar' });
            value.close();

            for (let i = 0; i < pcm.length; i++) {
                const a = Math.abs(pcm[i]);
                if (a > maxAbs) maxAbs = a;
                sumSq += pcm[i] * pcm[i];
            }
            samples += pcm.length;
        }
    } finally {
        try { await reader.cancel(); } catch { /* ignore */ }
        try { reader.releaseLock(); } catch { /* ignore */ }
        stream.getTracks().forEach(t => t.stop());
    }

    const rms = samples > 0 ? Math.sqrt(sumSq / samples) : 0;
    return { rms, maxAbs, samples };
}