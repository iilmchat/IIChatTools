/**
 * UI-модуль IIChatTools: тосты и вспомогательные функции.
 * Экспортируется как ES-модуль.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */

/**
 * Отображает тост-уведомление.
 * @param {string} message Текст сообщения
 * @param {'info'|'success'|'warning'|'error'} [type='info'] Тип уведомления
 */
export function toast(message, type = 'info') {
    const colors = {
        info: 'bg-primary',
        success: 'bg-success',
        warning: 'bg-warning text-dark',
        error: 'bg-danger'
    };

    const container = document.getElementById('toast-container') || (() => {
        const c = document.createElement('div');
        c.id = 'toast-container';
        c.className = 'toast-container position-fixed top-0 end-0 p-3';
        c.style.zIndex = '1100';
        document.body.appendChild(c);
        return c;
    })();

    const el = document.createElement('div');
    el.className = `toast align-items-center text-white ${colors[type] || colors.info} border-0 mb-2`;
    el.role = 'alert';
    el.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">${escapeHtml(message)}</div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
        </div>`;
    container.appendChild(el);

    const bsToast = new bootstrap.Toast(el, { delay: 4000 });
    bsToast.show();
    el.addEventListener('hidden.bs.toast', () => el.remove());
}

/**
 * Экранирует HTML-спецсимволы.
 * @param {string} str Исходная строка
 * @returns {string} Безопасная строка
 */
export function escapeHtml(str) {
    if (str === null || str === undefined) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/**
 * Форматирует длительность в человекочитаемый вид (секунды → дни/часы/минуты).
 * @param {number} totalSeconds Количество секунд
 * @returns {string} Строка вида "2d 5h 12m"
 */
export function formatUptime(totalSeconds) {
    const s = Math.max(0, Math.floor(totalSeconds));
    const d = Math.floor(s / 86400);
    const h = Math.floor((s % 86400) / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    const parts = [];
    if (d) parts.push(`${d}d`);
    if (h) parts.push(`${h}h`);
    if (m) parts.push(`${m}m`);
    if (!d && !h) parts.push(`${sec}s`);
    return parts.join(' ');
}

// Интерфейс-обёртка для использования из inline onclick в HTML
export const toastApi = {
    info: (msg) => toast(msg, 'info'),
    success: (msg) => toast(msg, 'success'),
    warning: (msg) => toast(msg, 'warning'),
    error: (msg) => toast(msg, 'error')
};