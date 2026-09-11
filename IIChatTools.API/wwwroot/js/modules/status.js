/**
 * Модуль страницы статуса: polling /api/status/snapshot.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */
import { apiGet } from './api.js';
import { escapeHtml, formatUptime, toast } from './ui.js';

let _pollTimer = null;

/**
 * Запускает периодический опрос статуса.
 * @param {number} intervalMs Интервал опроса в миллисекундах
 */
export function startStatusPolling(intervalMs = 5000) {
    refreshOnce();
    if (_pollTimer) clearInterval(_pollTimer);
    _pollTimer = setInterval(refreshOnce, intervalMs);

    const btn = document.getElementById('btn-refresh');
    if (btn) btn.addEventListener('click', refreshOnce);
}

/**
 * Однократное обновление данных статуса.
 */
async function refreshOnce() {
    const res = await apiGet('/api/status/snapshot');
    if (!res.success) {
        toast(res.message || 'Ошибка загрузки статуса', 'error');
        return;
    }
    render(res.data);
}

/**
 * Отрисовка снимка статуса.
 * @param {object} s Снимок состояния
 */
function render(s) {
    setText('stat-version', s.appVersion);
    setText('stat-uptime', formatUptime(s.uptimeSeconds));
    setText('stat-users', `${s.activeUsers} / ${s.totalUsers}`);
    setText('stat-actions', s.totalActions);
    setText('stat-pending', s.pendingApprovals);

    const dbBadge = document.getElementById('db-status');
    if (dbBadge) {
        dbBadge.textContent = s.databaseOnline ? 'Онлайн' : 'Оффлайн';
        dbBadge.className = s.databaseOnline
            ? 'badge bg-success'
            : 'badge bg-danger';
    }

    const depsList = document.getElementById('deps-list');
    if (depsList && s.dependencies) {
        const items = Object.entries(s.dependencies).map(([name, version]) => {
            const ok = !!version;
            const badge = ok
                ? `<span class="badge bg-success">Установлено</span>`
                : `<span class="badge bg-secondary">Не установлено</span>`;
            return `<li class="list-group-item d-flex justify-content-between align-items-center">
                        <span><strong>${escapeHtml(name)}</strong> ${version ? `<code>${escapeHtml(version)}</code>` : ''}</span>
                        ${badge}
                    </li>`;
        }).join('');
        depsList.innerHTML = items || '<li class="list-group-item text-muted">—</li>';
    }

    const tbody = document.getElementById('recent-actions-body');
    if (tbody) {
        const rows = (s.recentActions || []).map(a => `
            <tr>
                <td>${escapeHtml(a.userName)}</td>
                <td><code>${escapeHtml(a.toolName)}</code></td>
                <td>${statusBadge(a.status)}</td>
                <td>${a.durationMs} ms</td>
                <td>${new Date(a.createdAt).toLocaleString()}</td>
            </tr>
        `).join('');
        tbody.innerHTML = rows || '<tr><td colspan="5" class="text-muted text-center">—</td></tr>';
    }
}

/**
 * Возвращает HTML-бейдж статуса.
 * @param {string} status Статус
 * @returns {string}
 */
function statusBadge(status) {
    const map = {
        Success: 'bg-success',
        Error: 'bg-danger',
        Pending: 'bg-warning text-dark',
        Cancelled: 'bg-secondary'
    };
    const cls = map[status] || 'bg-secondary';
    return `<span class="badge ${cls}">${escapeHtml(status)}</span>`;
}

/**
 * Устанавливает текст элемента по id.
 * @param {string} id Идентификатор
 * @param {string} value Значение
 */
function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value ?? '—';
}