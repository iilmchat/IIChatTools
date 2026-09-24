/**
 * Модуль вкладки «Агенты» в /admin (v1.4.0 Фаза 6.5-6.6, KI-052).
 * Управление специализированными суб-агентами: список, редактирование,
 * сброс к значениям по умолчанию.
 *
 * Endpoints:
 *   GET  /api/admin/agents               — список агентов
 *   PUT  /api/admin/agents/{name}        — обновить дескриптор
 *   POST /api/admin/agents/{name}/reset  — сбросить к appsettings.json
 *
 * Использует универсальную модалку из admin.js (showModal).
 * © 2026 RuChating (iilmchat) · IIChatTools v1.4.0
 */
import { apiGet } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { showModal } from './admin.js';

// ---------- Состояние ----------
const state = {
    agents: []
};

/**
 * Инициализирует вкладку «Агенты».
 * Ленивая загрузка при первом открытии таба + кнопка «Обновить».
 */
export function initAgentsTab() {
    const tab = document.getElementById('tab-agents');
    if (!tab) return;

    tab.addEventListener('shown.bs.tab', () => {
        loadAgents();
        loadAgentStats();   // KI-076
    }, { once: true });

    const btn = document.getElementById('btn-refresh-agents');
    if (btn) btn.addEventListener('click', () => {
        loadAgents();
        loadAgentStats();   // KI-076
    });
}

// ---------- Статистика агентов (KI-076) ----------

/**
 * Загружает и рендерит карточки статистики запусков агентов.
 * Источник: GET /api/admin/agents/stats.
 */
async function loadAgentStats() {
    const grid = document.getElementById('agent-stats-grid');
    if (!grid) return;

    const res = await apiGet('/api/admin/agents/stats');
    if (!res.success) {
        grid.innerHTML = `<div class="text-danger small">${escapeHtml(res.message || 'Ошибка загрузки статистики')}</div>`;
        return;
    }

    const stats = res.data || [];
    if (stats.length === 0) {
        const empty = grid.dataset.labelEmpty || 'Пока нет запусков агентов.';
        grid.innerHTML = `<div class="text-muted small">${escapeHtml(empty)}</div>`;
        return;
    }

    const labelTotal = grid.dataset.labelTotal || 'Всего запусков';
    const labelAvgTime = grid.dataset.labelAvgtime || 'Среднее время';
    const labelSuccess = grid.dataset.labelSuccess || 'Успешных';
    const labelLastRun = grid.dataset.labelLastrun || 'Последний запуск';

    grid.innerHTML = stats.map(s => `
        <div class="agent-stat-card">
            <div class="agent-stat-card-header">
                <span class="agent-stat-card-title">${escapeHtml(s.displayName || s.agentName)}</span>
                <code class="agent-stat-card-name">${escapeHtml(s.agentName)}</code>
            </div>
            <div class="agent-stat-card-metrics">
                <div class="agent-stat-metric">
                    <div class="agent-stat-value">${s.totalRuns}</div>
                    <div class="agent-stat-label">${escapeHtml(labelTotal)}</div>
                </div>
                <div class="agent-stat-metric">
                    <div class="agent-stat-value">${formatDuration(s.avgDurationMs)}</div>
                    <div class="agent-stat-label">${escapeHtml(labelAvgTime)}</div>
                </div>
                <div class="agent-stat-metric">
                    <div class="agent-stat-value">${formatPercent(s.successRate)}</div>
                    <div class="agent-stat-label">${escapeHtml(labelSuccess)}</div>
                </div>
            </div>
            <div class="agent-stat-card-footer">
                <span class="text-muted small">${escapeHtml(labelLastRun)}: ${formatLastRun(s.lastRunAt)}</span>
            </div>
        </div>
    `).join('');
}

/**
 * Форматирует длительность (мс → «1.2 с» / «345 мс»).
 * @param {number} ms
 * @returns {string}
 */
function formatDuration(ms) {
    if (!ms || ms < 0) return '—';
    if (ms < 1000) return `${ms} мс`;
    return `${(ms / 1000).toFixed(1)} с`;
}

/**
 * Форматирует процент (0–100 → «95%»).
 * @param {number} pct
 * @returns {string}
 */
function formatPercent(pct) {
    if (pct == null || isNaN(pct)) return '—';
    return `${pct.toFixed(1)}%`;
}

/**
 * Форматирует дату последнего запуска (относительная или абсолютная).
 * @param {string|null} iso
 * @returns {string}
 */
function formatLastRun(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    const now = new Date();
    const diffMin = Math.floor((now - d) / 60000);
    if (diffMin < 1) return 'только что';
    if (diffMin < 60) return `${diffMin} мин назад`;
    const diffH = Math.floor(diffMin / 60);
    if (diffH < 24) return `${diffH} ч назад`;
    const diffD = Math.floor(diffH / 24);
    if (diffD < 7) return `${diffD} д назад`;
    return d.toLocaleDateString();
}

// ---------- Загрузка списка ----------

async function loadAgents() {
    const tbody = document.getElementById('agents-tbody');
    if (!tbody) return;

    tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">Загрузка…</td></tr>';

    const res = await apiGet('/api/admin/agents');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="7" class="text-danger text-center">${escapeHtml(res.message)}</td></tr>`;
        return;
    }

    state.agents = res.data || [];
    renderAgentsTable();
}

function renderAgentsTable() {
    const tbody = document.getElementById('agents-tbody');
    if (!tbody) return;

    if (state.agents.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" class="text-muted text-center">—</td></tr>';
        return;
    }

    tbody.innerHTML = state.agents.map(a => {
        const model = a.model
            ? escapeHtml(a.model)
            : '<span class="text-muted small">&lt;default&gt;</span>';

        const approval = a.requiresApprovalByDefault
            ? '<span class="badge bg-warning text-dark">Да</span>'
            : '<span class="badge bg-secondary">Нет</span>';

        const enabled = a.disabled
            ? '<span class="badge bg-secondary">Отключён</span>'
            : '<span class="badge bg-success">Включён</span>';

        return `
            <tr>
                <td><code>${escapeHtml(a.name)}</code></td>
                <td>${escapeHtml(a.displayName || '—')}</td>
                <td>${model}</td>
                <td>${a.toolCount}</td>
                <td>${approval}</td>
                <td>${enabled}</td>
                <td>
                    <button class="btn btn-sm btn-outline-primary" data-action="edit-agent" data-name="${escapeHtml(a.name)}">Изменить</button>
                    <button class="btn btn-sm btn-outline-warning" data-action="reset-agent" data-name="${escapeHtml(a.name)}">Сбросить</button>
                </td>
            </tr>
        `;
    }).join('');

    tbody.querySelectorAll('[data-action="edit-agent"]').forEach(btn => {
        btn.addEventListener('click', () => openAgentModal(btn.dataset.name));
    });
    tbody.querySelectorAll('[data-action="reset-agent"]').forEach(btn => {
        btn.addEventListener('click', () => resetAgent(btn.dataset.name));
    });
}

// ---------- Редактирование ----------

/**
 * Открывает модалку редактирования агента.
 * @param {string} name Техническое имя агента (snake_case)
 */
function openAgentModal(name) {
    const agent = state.agents.find(a => a.name === name);
    if (!agent) {
        toast('Агент не найден', 'error');
        return;
    }

    const allowedToolsValue = (agent.allowedTools || []).join('\n');

    showModal('Редактировать агента: ' + agent.name, `
        <div class="mb-2">
            <label class="form-label small">Техническое имя (только чтение)</label>
            <input type="text" class="form-control form-control-sm font-monospace"
                   value="${escapeHtml(agent.name)}" readonly>
        </div>
        <div class="mb-2">
            <label class="form-label small">Отображаемое имя</label>
            <input type="text" class="form-control form-control-sm" id="m-agent-display"
                   value="${escapeHtml(agent.displayName || '')}" maxlength="100">
        </div>
        <div class="mb-2">
            <label class="form-label small">Описание</label>
            <textarea class="form-control form-control-sm" id="m-agent-description"
                      rows="2" maxlength="500">${escapeHtml(agent.description || '')}</textarea>
        </div>
        <div class="row g-2 mb-2">
            <div class="col-8">
                <label class="form-label small">Модель LM Studio (пусто — из appsettings)</label>
                <input type="text" class="form-control form-control-sm font-monospace"
                       id="m-agent-model" value="${escapeHtml(agent.model || '')}">
            </div>
            <div class="col-4">
                <label class="form-label small">MaxSteps (1–30)</label>
                <input type="number" class="form-control form-control-sm" id="m-agent-maxsteps"
                       min="1" max="30" value="${agent.maxSteps}">
            </div>
        </div>
        <div class="mb-2">
            <label class="form-label small">System prompt</label>
            <textarea class="form-control form-control-sm font-monospace" id="m-agent-prompt"
                      rows="6">${escapeHtml(agent.systemPrompt || '')}</textarea>
        </div>
        <div class="mb-2">
            <label class="form-label small">AllowedTools (по одному в строке)</label>
            <textarea class="form-control form-control-sm font-monospace" id="m-agent-tools"
                      rows="6">${escapeHtml(allowedToolsValue)}</textarea>
        </div>
        <div class="form-check mb-2">
            <input type="checkbox" class="form-check-input" id="m-agent-approval"
                   ${agent.requiresApprovalByDefault ? 'checked' : ''}>
            <label class="form-check-label small" for="m-agent-approval">Требует подтверждения (approval)</label>
        </div>
        <div class="form-check">
            <input type="checkbox" class="form-check-input" id="m-agent-disabled"
                   ${agent.disabled ? 'checked' : ''}>
            <label class="form-check-label small" for="m-agent-disabled">Отключён (не виден в Chat)</label>
        </div>
    `, async () => {
        // --- Валидация ---
        const displayName = document.getElementById('m-agent-display').value.trim();
        if (!displayName)
            return { ok: false, message: 'Отображаемое имя обязательно' };

        const maxSteps = parseInt(document.getElementById('m-agent-maxsteps').value, 10) || 10;
        if (maxSteps < 1 || maxSteps > 30)
            return { ok: false, message: 'MaxSteps должен быть в диапазоне 1–30' };

        // --- Сборка тела ---
        const modelRaw = document.getElementById('m-agent-model').value.trim();
        const toolsRaw = document.getElementById('m-agent-tools').value;
        const allowedTools = toolsRaw
            .split('\n')
            .map(x => x.trim())
            .filter(x => x.length > 0);

        const body = {
            displayName: displayName,
            description: document.getElementById('m-agent-description').value.trim() || null,
            model: modelRaw || null,
            maxSteps: maxSteps,
            requiresApprovalByDefault: document.getElementById('m-agent-approval').checked,
            disabled: document.getElementById('m-agent-disabled').checked,
            systemPrompt: document.getElementById('m-agent-prompt').value || null,
            allowedTools: allowedTools
        };

        // --- Отправка ---
        const res = await fetch(`/api/admin/agents/${encodeURIComponent(name)}`, {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(r => r.json());

        if (!res.success)
            return { ok: false, message: res.message || 'Ошибка сохранения' };

        toast('Агент сохранён', 'success');
        await loadAgents();
        return { ok: true };
    });
}

// ---------- Сброс ----------

/**
 * Сбрасывает агента к значениям из appsettings.json.
 * @param {string} name Техническое имя агента
 */
async function resetAgent(name) {
    if (!confirm(`Сбросить агента "${name}" к значениям по умолчанию?`)) return;

    const res = await fetch(`/api/admin/agents/${encodeURIComponent(name)}/reset`, {
        method: 'POST',
        credentials: 'same-origin'
    }).then(r => r.json());

    if (!res.success) {
        toast(res.message || 'Ошибка сброса', 'error');
        return;
    }

    toast('Агент сброшен', 'success');
    await loadAgents();
}