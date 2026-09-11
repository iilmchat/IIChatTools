/**
 * Точка входа для ES-модулей IIChatTools.
 * Регистрирует глобальный namespace window.IIChatTools.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */
import { toast, toastApi, escapeHtml, formatUptime } from './modules/ui.js';
import { apiGet, apiPost } from './modules/api.js';

// Глобальный namespace для использования в inline-обработчиках
window.IIChatTools = {
    toast: toastApi,
    toastRaw: toast,
    escapeHtml,
    formatUptime,
    api: { get: apiGet, post: apiPost }
};

console.log('[IIChatTools] main.js loaded, version 1.0');