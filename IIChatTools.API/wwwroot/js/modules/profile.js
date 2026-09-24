/**
 * Модуль страницы /profile (v1.4.x, KI-067-3).
 * Управление per-user настройками retention чатов.
 *
 * Endpoints:
 *   GET /api/profile/settings — текущие настройки + глобальные значения
 *   PUT /api/profile/settings — сохранить настройки
 *
 * © 2026 RuChating (iilmchat) · IIChatTools v1.4.0
 */
import { apiGet } from './api.js';
import { toast } from './ui.js';

// ---------- Состояние ----------
const state = {
    maxDays: 365,
    globalDays: 30
};

/**
 * Инициализирует страницу профиля.
 */
export async function initProfilePage() {
    const btn = document.getElementById('btn-save-profile-settings');
    if (btn) btn.addEventListener('click', saveSettings);

    // При включении DoNotDelete — дизейблим поле дней.
    const cbDoNotDelete = document.getElementById('profile-do-not-delete');
    const inputDays = document.getElementById('profile-retention-days');
    cbDoNotDelete?.addEventListener('change', () => {
        if (inputDays) inputDays.disabled = cbDoNotDelete.checked;
    });

    await loadSettings();
}

// ---------- Загрузка ----------
async function loadSettings() {
    const res = await apiGet('/api/profile/settings');
    if (!res.success) {
        toast(res.message || 'Ошибка загрузки настроек', 'error');
        return;
    }

    const data = res.data || {};
    state.maxDays = data.maxRetentionDays || 365;
    state.globalDays = data.globalRetentionDays || 30;

    const inputDays = document.getElementById('profile-retention-days');
    const cbDoNotDelete = document.getElementById('profile-do-not-delete');
    const hint = document.getElementById('profile-global-hint');

    if (inputDays) {
        inputDays.max = String(state.maxDays);
        inputDays.value = data.retentionDays != null ? String(data.retentionDays) : '';
    }
    if (cbDoNotDelete) {
        cbDoNotDelete.checked = !!data.doNotDelete;
        // Дизейблим поле, если DoNotDelete
        if (inputDays) inputDays.disabled = cbDoNotDelete.checked;
    }
    if (hint) {
        hint.textContent = formatGlobalHint(state.globalDays);
    }
}

/**
 * Заменяет плейсхолдер {0} в data-атрибуте.
 * @param {number} days
 * @returns {string}
 */
function formatGlobalHint(days) {
    const hint = document.getElementById('profile-global-hint');
    const raw = hint?.dataset.template || hint?.textContent || '';
    return raw.replace('{0}', String(days));
}

// ---------- Сохранение ----------
async function saveSettings() {
    const status = document.getElementById('profile-save-status');
    const btn = document.getElementById('btn-save-profile-settings');

    const inputDays = document.getElementById('profile-retention-days');
    const cbDoNotDelete = document.getElementById('profile-do-not-delete');

    const rawDays = (inputDays?.value || '').trim();
    const doNotDelete = !!cbDoNotDelete?.checked;

    // Если DoNotDelete — RetentionDays не отправляем.
    let retentionDays = null;
    if (!doNotDelete && rawDays.length > 0) {
        const n = parseInt(rawDays, 10);
        if (!Number.isFinite(n) || n <= 0 || n > state.maxDays) {
            toast(`Срок хранения должен быть 1–${state.maxDays}`, 'warning');
            return;
        }
        retentionDays = n;
    }

    const body = {
        retentionDays: retentionDays,
        doNotDelete: doNotDelete
    };

    if (btn) btn.disabled = true;
    if (status) status.textContent = '';

    try {
        const res = await fetch('/api/profile/settings', {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка сохранения', 'error');
            return;
        }

        toast('Настройки сохранены', 'success');
        if (status) {
            status.textContent = '✓';
            setTimeout(() => { status.textContent = ''; }, 2000);
        }

        // Обновить значения из ответа (сервер нормализует).
        const data = res.data || {};
        if (inputDays) {
            inputDays.value = data.retentionDays != null ? String(data.retentionDays) : '';
        }
        if (cbDoNotDelete) {
            cbDoNotDelete.checked = !!data.doNotDelete;
            if (inputDays) inputDays.disabled = cbDoNotDelete.checked;
        }
    } catch (ex) {
        toast(ex.message || 'Ошибка сохранения', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}