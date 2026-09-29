/**
 * Модуль вкладки «SQL Agent» в /admin (v1.7.0, KI-097, Фаза 6C).
 * Управление подключениями Database Agent: whitelist таблиц, MaxRows,
 * Timeout, Enabled. Изменения сохраняются в AppSettings и применяются
 * в runtime без перезапуска.
 *
 * Endpoints:
 *   GET    /api/admin/sql-agent/connections
 *   PUT    /api/admin/sql-agent/connections/{name}
 *   POST   /api/admin/sql-agent/connections/{name}/test
 *   POST   /api/admin/sql-agent/connections/{name}/reset
 *
 * Использует универсальную модалку из admin.js (showModal).
 * © 2026 RuChating (iilmchat) · IIChatTools v1.7.0
 */
import { apiGet } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { showModal } from './admin.js';

// ---------- Состояние ----------
const state = {
    connections: []
};

/**
 * Инициализирует вкладку «SQL Agent».
 * Ленивая загрузка при первом открытии + кнопка «Обновить».
 * Делегированные обработчики на tbody — устойчивы к перерисовке.
 */
export function initSqlAgentTab() {
    const tab = document.getElementById('tab-sql-agent');
    if (!tab) return;

    tab.addEventListener('shown.bs.tab', () => loadConnections(), { once: true });

    document.getElementById('btn-refresh-sql-agent')
        ?.addEventListener('click', loadConnections);

    document.getElementById('sql-agent-connections-tbody')
        ?.addEventListener('click', onTbodyClick);
}

/**
 * Возвращает объект локализованных ярлыков из data-атрибутов pane-sql-agent.
 * @returns {object}
 */
function paneLabels() {
    const pane = document.getElementById('pane-sql-agent');
    return {
        editTitle: pane?.dataset.labelEditTitle || 'Connection settings',
        resetConfirm: pane?.dataset.labelResetConfirm || 'Reset connection settings?',
        resetSuccess: pane?.dataset.labelResetSuccess || 'Reset to defaults',
        saveSuccess: pane?.dataset.labelSaveSuccess || 'Settings saved',
        testSuccess: pane?.dataset.labelTestSuccess || 'Connection OK ({0} ms)',
        testFailed: pane?.dataset.labelTestFailed || 'Connection failed',
        edit: pane?.dataset.labelEdit || 'Edit',
        reset: pane?.dataset.labelReset || 'Reset',
        test: pane?.dataset.labelTest || 'Test',
        enabled: pane?.dataset.labelEnabled || 'Enabled',
        allowedTables: pane?.dataset.labelAllowedTables || 'Allowed tables',
        deniedTables: pane?.dataset.labelDeniedTables || 'Denied tables',
        maxRows: pane?.dataset.labelMaxRows || 'Max rows (1–10000)',
        timeout: pane?.dataset.labelTimeout || 'Timeout (sec, 1–300)',
        noConnections: pane?.dataset.labelNoConnections || 'No connections',
        overriddenBadge: pane?.dataset.labelOverriddenBadge || 'modified'
    };
}

// ---------- Загрузка и рендер списка ----------

async function loadConnections() {
    const tbody = document.getElementById('sql-agent-connections-tbody');
    if (!tbody) return;

    tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted">…</td></tr>';

    const res = await apiGet('/api/admin/sql-agent/connections');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="8" class="text-danger text-center">${escapeHtml(res.message || 'Error')}</td></tr>`;
        return;
    }

    state.connections = res.data || [];
    renderConnectionsTable();
}

function renderConnectionsTable() {
    const tbody = document.getElementById('sql-agent-connections-tbody');
    if (!tbody) return;

    const labels = paneLabels();

    if (state.connections.length === 0) {
        tbody.innerHTML = `<tr><td colspan="8" class="text-muted text-center">${escapeHtml(labels.noConnections)}</td></tr>`;
        return;
    }

    tbody.innerHTML = state.connections.map(c => {
        const enabledBadge = c.enabled
            ? '<span class="badge bg-success">✓</span>'
            : '<span class="badge bg-secondary">×</span>';

        const overriddenBadge = c.isOverridden
            ? ` <span class="badge bg-warning text-dark">${escapeHtml(labels.overriddenBadge)}</span>`
            : '';

        const allowedCount = Array.isArray(c.allowedTables) ? c.allowedTables.length : 0;
        const deniedCount = Array.isArray(c.deniedTables) ? c.deniedTables.length : 0;

        return `
            <tr>
                <td>
                    <code>${escapeHtml(c.name || '')}</code>${overriddenBadge}
                </td>
                <td>${escapeHtml(c.displayName || '—')}</td>
                <td><span class="badge bg-light text-dark border">${escapeHtml(c.provider || '—')}</span></td>
                <td>${enabledBadge}</td>
                <td title="${escapeHtml((c.deniedTables || []).join(', '))}">
                    ${allowedCount} <span class="text-muted small">(denied: ${deniedCount})</span>
                </td>
                <td>${c.maxRows ?? '—'}</td>
                <td>${c.statementTimeoutSeconds ?? '—'} с</td>
                <td>
                    <button class="btn btn-sm btn-outline-primary"
                            type="button"
                            data-action="edit"
                            data-name="${escapeHtml(c.name || '')}">${escapeHtml(labels.edit)}</button>
                    <button class="btn btn-sm btn-outline-secondary"
                            type="button"
                            data-action="test"
                            data-name="${escapeHtml(c.name || '')}">${escapeHtml(labels.test)}</button>
                    <button class="btn btn-sm btn-outline-warning"
                            type="button"
                            data-action="reset"
                            data-name="${escapeHtml(c.name || '')}">${escapeHtml(labels.reset)}</button>
                </td>
            </tr>
        `;
    }).join('');
}

// ---------- Обработчики кнопок ----------

async function onTbodyClick(e) {
    const btn = e.target.closest('[data-action]');
    if (!btn) return;
    e.preventDefault();

    const action = btn.dataset.action;
    const name = btn.dataset.name;
    if (!name) return;

    if (action === 'edit') {
        openEditModal(name);
    } else if (action === 'test') {
        await testConnection(name);
    } else if (action === 'reset') {
        await resetConnection(name);
    }
}

// ---------- Модалка редактирования ----------

function openEditModal(name) {
    const conn = state.connections.find(c => c.name === name);
    if (!conn) {
        toast('Подключение не найдено', 'error');
        return;
    }

    const labels = paneLabels();
    const allowedLines = (conn.allowedTables || []).join('\n');
    const deniedLines = (conn.deniedTables || []).join('\n');

    showModal(`${labels.editTitle}: ${conn.name}`, `
        <div class="mb-2">
            <label class="form-label small">${escapeHtml(labels.allowedTables)}</label>
            <textarea class="form-control form-control-sm font-monospace"
                      id="m-sql-allowed" rows="6">${escapeHtml(allowedLines)}</textarea>
        </div>
        <div class="mb-2">
            <label class="form-label small">${escapeHtml(labels.deniedTables)}</label>
            <textarea class="form-control form-control-sm font-monospace"
                      id="m-sql-denied" rows="6">${escapeHtml(deniedLines)}</textarea>
        </div>
        <div class="row g-2 mb-2">
            <div class="col-6">
                <label class="form-label small">${escapeHtml(labels.maxRows)}</label>
                <input type="number" class="form-control form-control-sm" id="m-sql-maxrows"
                       min="1" max="10000" value="${conn.maxRows}">
            </div>
            <div class="col-6">
                <label class="form-label small">${escapeHtml(labels.timeout)}</label>
                <input type="number" class="form-control form-control-sm" id="m-sql-timeout"
                       min="1" max="300" value="${conn.statementTimeoutSeconds}">
            </div>
        </div>
        <div class="form-check">
            <input type="checkbox" class="form-check-input" id="m-sql-enabled"
                   ${conn.enabled ? 'checked' : ''}>
            <label class="form-check-label small" for="m-sql-enabled">${escapeHtml(labels.enabled)}</label>
        </div>
    `, async () => {
        // --- Валидация ---
        const maxRows = parseInt(document.getElementById('m-sql-maxrows').value, 10);
        if (!Number.isFinite(maxRows) || maxRows < 1 || maxRows > 10000)
            return { ok: false, message: 'MaxRows должен быть 1–10000' };

        const timeout = parseInt(document.getElementById('m-sql-timeout').value, 10);
        if (!Number.isFinite(timeout) || timeout < 1 || timeout > 300)
            return { ok: false, message: 'Timeout должен быть 1–300' };

        // --- Сборка тела ---
        const allowed = document.getElementById('m-sql-allowed').value
            .split('\n').map(x => x.trim()).filter(x => x.length > 0);
        const denied = document.getElementById('m-sql-denied').value
            .split('\n').map(x => x.trim()).filter(x => x.length > 0);

        const body = {
            allowedTables: allowed,
            deniedTables: denied,
            maxRows: maxRows,
            statementTimeoutSeconds: timeout,
            enabled: document.getElementById('m-sql-enabled').checked
        };

        // --- Отправка ---
        const res = await fetch(`/api/admin/sql-agent/connections/${encodeURIComponent(name)}`, {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(r => r.json());

        if (!res.success)
            return { ok: false, message: res.message || 'Error' };

        toast(labels.saveSuccess, 'success');
        await loadConnections();
        return { ok: true };
    });
}

// ---------- Test ----------

async function testConnection(name) {
    const labels = paneLabels();

    try {
        const res = await fetch(
            `/api/admin/sql-agent/connections/${encodeURIComponent(name)}/test`,
            { method: 'POST', credentials: 'same-origin' }
        ).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Error', 'error');
            return;
        }

        const result = res.data || {};
        if (result.success) {
            const msg = labels.testSuccess.replace('{0}', String(result.durationMs ?? 0));
            toast(msg, 'success');
        } else {
            toast(`${labels.testFailed}: ${result.message || ''}`, 'error');
        }
    } catch (ex) {
        toast(ex.message || 'Error', 'error');
    }
}

// ---------- Reset ----------

async function resetConnection(name) {
    const labels = paneLabels();
    if (!confirm(labels.resetConfirm)) return;

    try {
        const res = await fetch(
            `/api/admin/sql-agent/connections/${encodeURIComponent(name)}/reset`,
            { method: 'POST', credentials: 'same-origin' }
        ).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Error', 'error');
            return;
        }

        toast(labels.resetSuccess, 'success');
        await loadConnections();
    } catch (ex) {
        toast(ex.message || 'Error', 'error');
    }
}