/**
 * Модуль карточки «Workspace index» на /profile
 * (v1.5.0, KI-083, Шаг 7C.2).
 *
 * Управление per-user семантическим индексом по файлам workspace:
 *   - вкл/выкл (checkbox);
 *   - переиндексация (кнопка);
 *   - polling статуса каждые 2 с, пока идёт индексация.
 *
 * Endpoints:
 *   GET  /api/profile/workspace-index          — статус (initial)
 *   GET  /api/profile/workspace-index/status   — статус (polling)
 *   POST /api/profile/workspace-index/enable
 *   POST /api/profile/workspace-index/disable
 *   POST /api/profile/workspace-index/reindex
 *
 * Локализация — через data-* на #profile-workspace-card (RULES § 4.17).
 * © 2026 RuChating (iilmchat) · IIChatTools v1.5.0
 */
import { apiGet } from './api.js';
import { toast } from './ui.js';

/** Интервал polling при индексации (мс). */
const POLL_INTERVAL_MS = 2000;

// ---------- Состояние ----------
const state = {
    enabled: false,
    isIndexing: false,
    filesIndexed: 0,
    chunkCount: 0,
    totalFiles: 0,
    filesProcessed: 0,
    chunksCreated: 0,
    lastError: null,
    pollTimer: null
};

/**
 * Инициализирует карточку Workspace index.
 * Идемпотентно: если карточки нет — no-op.
 */
export function initProfileWorkspaceCard() {
    const card = document.getElementById('profile-workspace-card');
    if (!card) return;

    bindEvents();
    loadStatus();
}

// ---------- Привязка событий ----------

function bindEvents() {
    document.getElementById('profile-workspace-enable')
        ?.addEventListener('change', onToggleEnable);

    document.getElementById('btn-profile-workspace-reindex')
        ?.addEventListener('click', onReindex);
}

// ---------- Загрузка / рендер ----------

async function loadStatus() {
    const res = await apiGet('/api/profile/workspace-index');
    if (!res.success) {
        // Карточка не критична — не показываем toast, только консоль.
        console.warn('[profile-workspace] Ошибка загрузки статуса:', res.message);
        return;
    }

    applyStatus(res.data || {});
}

/**
 * Применяет DTO статуса к state и перерисовывает UI.
 * @param {object} dto WorkspaceIndexStatusDto
 */
function applyStatus(dto) {
    state.enabled = !!dto.enabled;
    state.isIndexing = !!dto.isIndexing;
    state.filesIndexed = dto.filesIndexed || 0;
    state.chunkCount = dto.chunkCount || 0;
    state.totalFiles = dto.totalFiles || 0;
    state.filesProcessed = dto.filesProcessed || 0;
    state.chunksCreated = dto.chunksCreated || 0;
    state.lastError = dto.lastError || null;

    render();
    schedulePolling();
}

function render() {
    const card = document.getElementById('profile-workspace-card');
    if (!card) return;

    // 1. Checkbox
    const cb = document.getElementById('profile-workspace-enable');
    if (cb) cb.checked = state.enabled;

    // 2. Статус
    renderStatus(card);

    // 3. Progress bar
    renderProgress(card);

    // 4. Error
    renderError(card);

    // 5. Кнопка «Переиндексировать»
    const reindexBtn = document.getElementById('btn-profile-workspace-reindex');
    if (reindexBtn) {
        reindexBtn.disabled = !state.enabled || state.isIndexing;
    }
}

function renderStatus(card) {
    const el = document.getElementById('profile-workspace-status');
    if (!el) return;

    if (state.isIndexing) {
        const tpl = card.dataset.labelStatusIndexing || 'Indexing…';
        el.textContent = tpl;
        return;
    }

    if (state.enabled) {
        const tpl = card.dataset.labelStatusEnabled || 'Enabled · {0} files · {1} chunks';
        el.textContent = tpl
            .replace('{0}', String(state.filesIndexed))
            .replace('{1}', String(state.chunkCount));
        return;
    }

    el.textContent = card.dataset.labelStatusDisabled || 'Disabled';
}

function renderProgress(card) {
    const wrap = document.getElementById('profile-workspace-progress-wrap');
    const bar = document.getElementById('profile-workspace-progress-bar');
    const text = document.getElementById('profile-workspace-progress-text');
    if (!wrap || !bar || !text) return;

    if (!state.isIndexing) {
        wrap.hidden = true;
        return;
    }

    wrap.hidden = false;

    const total = state.totalFiles || 0;
    const processed = state.filesProcessed || 0;
    const pct = total > 0 ? Math.min(100, Math.round(processed * 100 / total)) : 0;

    bar.style.width = `${pct}%`;
    bar.setAttribute('aria-valuenow', String(pct));

    const tpl = card.dataset.labelProgress || '{0} / {1}';
    const progress = tpl
        .replace('{0}', String(processed))
        .replace('{1}', String(total));

    text.textContent = total > 0
        ? `${progress} (${pct}%)`
        : (processed > 0 ? `${processed} …` : '…');
}

function renderError(card) {
    const el = document.getElementById('profile-workspace-error');
    if (!el) return;

    if (!state.lastError) {
        el.hidden = true;
        el.textContent = '';
        return;
    }

    const prefix = card.dataset.labelError || 'Last error:';
    el.hidden = false;
    el.textContent = `${prefix} ${state.lastError}`;
}

// ---------- Polling ----------

/**
 * Планирует следующий опрос статуса, если идёт индексация.
 * Использует setTimeout (а не setInterval) — гарантирует, что новый
 * запрос не стартует, пока не завершился предыдущий.
 */
function schedulePolling() {
    if (state.pollTimer) {
        clearTimeout(state.pollTimer);
        state.pollTimer = null;
    }

    if (!state.isIndexing) return;

    state.pollTimer = setTimeout(async () => {
        state.pollTimer = null;

        try {
            const res = await apiGet('/api/profile/workspace-index/status');
            if (res.success) {
                applyStatus(res.data || {});
            } else {
                // Не смогли получить — попробуем ещё раз через 2 с.
                schedulePolling();
            }
        } catch (ex) {
            console.warn('[profile-workspace] Ошибка polling:', ex);
            schedulePolling();
        }
    }, POLL_INTERVAL_MS);
}

// ---------- Действия пользователя ----------

async function onToggleEnable(e) {
    const card = document.getElementById('profile-workspace-card');
    const wantEnabled = !!e.target.checked;

    if (wantEnabled) {
        await enable(card, e.target);
    } else {
        await disable(card, e.target);
    }
}

async function enable(card, checkbox) {
    try {
        const res = await fetch('/api/profile/workspace-index/enable', {
            method: 'POST',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res.success) {
            checkbox.checked = false;
            toast(res.message || 'Error', 'error');
            return;
        }

        toast(card.dataset.labelEnableSuccess || 'Enabled', 'success');
        applyStatus(res.data || {});
    } catch (ex) {
        checkbox.checked = false;
        toast(ex.message || 'Error', 'error');
    }
}

async function disable(card, checkbox) {
    const confirmMsg = card.dataset.labelDisableConfirm
        || 'Disable workspace indexing? All indexed data will be removed.';

    if (!confirm(confirmMsg)) {
        checkbox.checked = true;   // откат UI
        return;
    }

    try {
        const res = await fetch('/api/profile/workspace-index/disable', {
            method: 'POST',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res.success) {
            checkbox.checked = true;
            toast(res.message || 'Error', 'error');
            return;
        }

        toast(card.dataset.labelDisableSuccess || 'Disabled', 'success');
        applyStatus(res.data || {});
    } catch (ex) {
        checkbox.checked = true;
        toast(ex.message || 'Error', 'error');
    }
}

async function onReindex() {
    const card = document.getElementById('profile-workspace-card');

    try {
        const res = await fetch('/api/profile/workspace-index/reindex', {
            method: 'POST',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Error', 'error');
            return;
        }

        toast(card.dataset.labelReindexSuccess || 'Reindex started', 'success');
        applyStatus(res.data || {});
    } catch (ex) {
        toast(ex.message || 'Error', 'error');
    }
}