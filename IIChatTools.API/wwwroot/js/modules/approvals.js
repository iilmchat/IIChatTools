/**
 * Модуль подтверждения действий агента.
 * Отображает модальное окно, ожидает решения пользователя, отправляет решение на сервер,
 * а затем выполняет ожидающий запрос на инструмент с approvalId.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';

/**
 * Запрашивает подтверждение действия у пользователя.
 * @param {number} actionId Идентификатор ожидающего действия
 * @param {string} toolName Имя инструмента
 * @param {string} parametersJson Параметры в виде JSON-строки
 * @param {number} expiresAt Время истечения в UTC (миллисекунды)
 * @returns {Promise<'approved'|'rejected'|'expired'>}
 */
export function requestApproval(actionId, toolName, parametersJson, expiresAt) {
    return new Promise(resolve => {
        const modalEl = document.getElementById('approvalModal');
        if (!modalEl) {
            toast('Модальное окно подтверждения не найдено', 'error');
            resolve('rejected');
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

        const updateCountdown = () => {
            const left = Math.max(0, expiresAt - Date.now());
            const sec = Math.floor(left / 1000);
            const mm = Math.floor(sec / 60);
            const ss = sec % 60;
            countdownEl.textContent = `Осталось: ${mm}:${ss.toString().padStart(2, '0')}`;
            if (left <= 0 && !settled) {
                settled = true;
                clearInterval(timer);
                modal.hide();
                toast('Время подтверждения истекло', 'warning');
                resolve('expired');
            }
        };
        const timer = setInterval(updateCountdown, 1000);
        updateCountdown();

        const onApprove = async () => {
            if (settled) return;
            settled = true;
            clearInterval(timer);
            approveBtn.disabled = rejectBtn.disabled = true;

            const res = await apiPost(`/api/approvals/${actionId}/approve`, {});
            modal.hide();
            if (!res.success) {
                toast(res.message || 'Ошибка подтверждения', 'error');
                resolve('rejected');
                return;
            }
            resolve('approved');
        };

        const onReject = async () => {
            if (settled) return;
            settled = true;
            clearInterval(timer);
            approveBtn.disabled = rejectBtn.disabled = true;

            const reason = prompt('Причина отклонения (необязательно):', '') || '';
            const res = await apiPost(`/api/approvals/${actionId}/reject`, { reason });
            modal.hide();
            if (!res.success) {
                toast(res.message || 'Ошибка отклонения', 'error');
            }
            resolve('rejected');
        };

        approveBtn.addEventListener('click', onApprove, { once: true });
        rejectBtn.addEventListener('click', onReject, { once: true });

        modalEl.addEventListener('hidden.bs.modal', () => {
            approveBtn.disabled = rejectBtn.disabled = false;
        }, { once: true });

        modal.show();
    });
}