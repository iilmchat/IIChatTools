/**
 * Модуль API-клиента IIChatTools.
 * Обёртка над fetch с единой обработкой ответов { success, data, message }.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */

/**
 * Выполняет GET-запрос к API.
 * @param {string} url URL эндпоинта
 * @returns {Promise<{success: boolean, data?: any, message?: string}>}
 */
export async function apiGet(url) {
    return apiFetch(url, { method: 'GET' });
}

/**
 * Выполняет POST-запрос к API с JSON-телом.
 * @param {string} url URL эндпоинта
 * @param {object} body Тело запроса
 * @returns {Promise<{success: boolean, data?: any, message?: string}>}
 */
export async function apiPost(url, body = {}) {
    return apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
}

/**
 * Базовый метод fetch с обработкой ошибок.
 * @param {string} url URL
 * @param {RequestInit} options Параметры fetch
 * @returns {Promise<object>}
 */
async function apiFetch(url, options) {
    try {
        const response = await fetch(url, {
            ...options,
            credentials: 'same-origin'
        });

        if (!response.ok) {
            return { success: false, message: `HTTP ${response.status}` };
        }

        const text = await response.text();
        if (!text) return { success: true };

        try {
            return JSON.parse(text);
        } catch {
            return { success: true, data: text };
        }
    } catch (ex) {
        return { success: false, message: ex.message || 'Network error' };
    }
}