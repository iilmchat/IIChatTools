/**
 * Модуль подтверждения действий агента.
 * Отображает модальное окно, ожидает решения пользователя, отправляет решение на сервер,
 * а затем выполняет ожидающий запрос на инструмент с approvalId.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.1.0
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';

/**
 * Запрашивает подтверждение действия у пользователя.
 * @param {number} actionId Идентификатор ожидающего действия
 * @param {string} toolName Имя инструмента
 * @param {string} parametersJson Параметры в виде JSON-строки
 * @param {number} expiresAt Время истечения в UTC (миллисекунды)
 * @returns {Promise<{decision: 'approved'|'rejected'|'expired', reason?: string}>}
 *   decision — решение пользователя; reason — причина отклонения (для 'rejected')
 */
export function requestApproval(actionId, toolName, parametersJson, expiresAt) {
    return new Promise(resolve => {
        const modalEl = document.getElementById('approvalModal');
        if (!modalEl) {
            toast('Модальное окно подтверждения не найдено', 'error');
            resolve({ decision: 'rejected', reason: 'Модальное окно не найдено' });
            return;
        }

        document.getElementById('approval-tool-name').textContent = toolName;

        let pretty = parametersJson;
        try { pretty = JSON.stringify(JSON.parse(parametersJson), null, 2); } catch { /* оставляем как есть */ }
        document.getElementById('approval-params').textContent = pretty;

        const countdownEl = document.getElementById('approval-countdown');
        const approveBtn = document.getElementById('approval-approve');
        const rejectBtn = document.getElementById('approval-reject');

        const modal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });
        let settled = false;
        let timer = null;

        // Снимает все обработчики и останавливает таймер.
        // Вызывается ровно один раз — при принятии решения (approve/reject/expired).
        const cleanup = () => {
            if (timer) {
                clearInterval(timer);
                timer = null;
            }
            approveBtn.removeEventListener('click', onApprove);
            rejectBtn.removeEventListener('click', onReject);
        };

        const updateCountdown = () => {
            const left = Math.max(0, expiresAt - Date.now());
            const sec = Math.floor(left / 1000);
            const mm = Math.floor(sec / 60);
            const ss = sec % 60;
            countdownEl.textContent = `Осталось: ${mm}:${ss.toString().padStart(2, '0')}`;

            if (left <= 0 && !settled) {
                settled = true;
                cleanup();
                modal.hide();
                toast('Время подтверждения истекло', 'warning');
                resolve({ decision: 'expired' });
            }
        };
        timer = setInterval(updateCountdown, 1000);
        updateCountdown();

        const onApprove = async () => {
            if (settled) return;
            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;

            try {
                const res = await apiPost(`/api/approvals/${actionId}/approve`, {});
                modal.hide();
                if (!res.success) {
                    toast(res.message || 'Ошибка подтверждения', 'error');
                    resolve({ decision: 'rejected', reason: res.message || 'Ошибка подтверждения' });
                    return;
                }
                resolve({ decision: 'approved' });
            } catch (ex) {
                modal.hide();
                toast(ex.message || 'Ошибка подтверждения', 'error');
                resolve({ decision: 'rejected', reason: ex.message || 'Ошибка подтверждения' });
            }
        };

        const onReject = async () => {
            if (settled) return;

            // Запрос причины. Cancel в prompt() = пользователь передумал:
            // модалка остаётся открытой, обработчики живы, settled = false.
            const raw = prompt('Причина отклонения (необязательно):', '');
            if (raw === null) {
                return;
            }
            const reason = raw.trim();

            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;

            try {
                const res = await apiPost(`/api/approvals/${actionId}/reject`, { reason });
                modal.hide();
                if (!res.success) {
                    toast(res.message || 'Ошибка отклонения', 'error');
                    resolve({ decision: 'rejected', reason: res.message || 'Ошибка отклонения' });
                    return;
                }
                resolve({ decision: 'rejected', reason });
            } catch (ex) {
                modal.hide();
                toast(ex.message || 'Ошибка отклонения', 'error');
                resolve({ decision: 'rejected', reason: ex.message || 'Ошибка отклонения' });
            }
        };

        // Без { once: true } — снимаем обработчики сами через cleanup()
        approveBtn.addEventListener('click', onApprove);
        rejectBtn.addEventListener('click', onReject);

        // Восстанавливаем кнопки при следующем открытии модалки (не once!)
        modalEl.addEventListener('hidden.bs.modal', () => {
            approveBtn.disabled = rejectBtn.disabled = false;
        });

        modal.show();
    });
}