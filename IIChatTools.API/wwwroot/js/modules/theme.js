/**
 * IIChatTools — модуль тем оформления (v1.10.x, KI-122, шаг B2).
 *
 * Отвечает за:
 *   - чтение / запись выбранной темы в localStorage["theme"];
 *   - применение темы к <html data-bs-theme="...">;
 *   - fallback на системную тему (prefers-color-scheme: dark);
 *   - событие "theme-changed" для UI-синхронизации (см. B3 — <select>).
 *
 * Поддерживаемые темы (KI-122):
 *   - light           — Bootstrap 5.3.2 default;
 *   - dark            — Bootstrap 5.3.2 default;
 *   - dimmed          — GitHub-Dimmed (site.css);
 *   - solarized-light — Ethan Schoonover (site.css);
 *   - high-contrast   — WCAG AAA (site.css).
 *
 * Anti-FOUC: тот же алгоритм **дублируется** синхронным inline-скриптом
 * в <head> _Layout.cshtml (до первого paint). Дублирование осознанное —
 * ES-модули грузятся асинхронно и не успевают до отрисовки.
 *
 * Стиль кода: camelCase (JS). Локализация — через data-* (RULES § 4.17);
 * в этом файле локализуемых строк нет.
 */

/** Ключ в localStorage. Домен — глобальный (не префиксуется, как chat.*). */
const STORAGE_KEY = "theme";

/** Допустимые значения темы. Порядок = порядок в UI-переключателе (B3). */
const VALID_THEMES = Object.freeze([
    "light",
    "dark",
    "dimmed",
    "solarized-light",
    "high-contrast"
]);

/** Тема, используемая при срабатывании prefers-color-scheme: dark. */
const SYSTEM_DARK_THEME = "dark";

/** Тема по умолчанию (если системная тоже не подсказала). */
const DEFAULT_THEME = "light";

/**
 * Возвращает список допустимых тем (для UI-переключателя, B3).
 * @returns {string[]} Копия массива (immutable на стороне вызывающего).
 */
export function getAvailableThemes() {
    return [...VALID_THEMES];
}

/**
 * Возвращает активную тему:
 *   1. Из localStorage (если сохранена и валидна);
 *   2. Иначе — из prefers-color-scheme (dark → "dark", иначе "light").
 * @returns {string} Имя темы.
 */
export function getTheme() {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored && VALID_THEMES.includes(stored)) {
            return stored;
        }
    } catch (_) {
        // localStorage недоступен (приватный режим / заблокирован политикой) — no-op.
    }

    if (typeof window !== "undefined"
        && typeof window.matchMedia === "function"
        && window.matchMedia("(prefers-color-scheme: dark)").matches) {
        return SYSTEM_DARK_THEME;
    }

    return DEFAULT_THEME;
}

/**
 * Применяет тему: атрибут на <html>, сохранение в localStorage,
 * событие "theme-changed". Если тема неизвестна — предупреждение и false.
 * @param {string} theme Имя темы (см. VALID_THEMES).
 * @returns {boolean} true при успехе.
 */
export function setTheme(theme) {
    if (!VALID_THEMES.includes(theme)) {
        console.warn(`[theme] Неизвестная тема: "${theme}". Допустимые: ${VALID_THEMES.join(", ")}.`);
        return false;
    }

    document.documentElement.setAttribute("data-bs-theme", theme);

    try {
        localStorage.setItem(STORAGE_KEY, theme);
    } catch (_) {
        // Тихо игнорируем — тема применена, но не сохранится между сессиями.
    }

    window.dispatchEvent(new CustomEvent("theme-changed", { detail: { theme } }));
    return true;
}

/**
 * Сбрасывает пользовательский выбор → возврат к системной теме.
 * @returns {string} Тема, которая в итоге применилась.
 */
export function clearTheme() {
    try {
        localStorage.removeItem(STORAGE_KEY);
    } catch (_) {
        // no-op
    }

    const fallback = (typeof window !== "undefined"
        && typeof window.matchMedia === "function"
        && window.matchMedia("(prefers-color-scheme: dark)").matches)
        ? SYSTEM_DARK_THEME
        : DEFAULT_THEME;

    document.documentElement.setAttribute("data-bs-theme", fallback);
    window.dispatchEvent(new CustomEvent("theme-changed", { detail: { theme: fallback } }));
    return fallback;
}

/**
 * Подписывает на изменение системной темы (prefers-color-scheme).
 * Сработает, только если пользователь не выбрал свою тему.
 * Возвращает функцию-отписку.
 * @returns {() => void}
 */
export function watchSystemTheme() {
    if (typeof window === "undefined"
        || typeof window.matchMedia !== "function") {
        return () => {};
    }

    const mql = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = (e) => {
        // Не перебиваем явный выбор пользователя.
        try {
            if (localStorage.getItem(STORAGE_KEY)) return;
        } catch (_) {
            // Если localStorage недоступен — считаем, что явного выбора нет.
        }
        const next = e.matches ? SYSTEM_DARK_THEME : DEFAULT_THEME;
        document.documentElement.setAttribute("data-bs-theme", next);
        window.dispatchEvent(new CustomEvent("theme-changed", { detail: { theme: next } }));
    };

    if (typeof mql.addEventListener === "function") {
        mql.addEventListener("change", handler);
        return () => mql.removeEventListener("change", handler);
    }
    // Safari < 14 (legacy) — не критично, но пусть будет.
    if (typeof mql.addListener === "function") {
        mql.addListener(handler);
        return () => mql.removeListener(handler);
    }
    return () => {};
}

/**
 * Инициализирует UI-переключатель темы (<select>). Шаг B3.
 *
 * - Устанавливает текущее значение из getTheme();
 * - на "change" → setTheme();
 * - подписывается на "theme-changed" (для программных изменений:
 *   clearTheme(), watchSystemTheme()) — синхронизирует value селекта.
 *
 * Идемпотентна: повторный вызов на том же элементе — no-op (маркер
 * через dataset.themeSelectInitialized).
 *
 * @param {HTMLSelectElement | null | undefined} selectEl Элемент <select>.
 * @returns {boolean} true, если инициализация выполнена.
 */
export function initThemeSelect(selectEl) {
    if (!selectEl) {
        console.warn("[theme] initThemeSelect: элемент не найден.");
        return false;
    }

    if (selectEl.dataset.themeSelectInitialized === "1") {
        return true;
    }
    selectEl.dataset.themeSelectInitialized = "1";

    // Синхронизация value с фактической темой (может отличаться от value в HTML —
    // например, при первом заходе пользователя, если data-bs-theme установлен
    // anti-FOUC-скриптом по системной теме).
    selectEl.value = getTheme();

    selectEl.addEventListener("change", (e) => {
        const next = e.target.value;
        if (!setTheme(next)) {
            // Не удалось применить (неизвестная тема) — откатить UI.
            selectEl.value = getTheme();
        }
    });

    // Программные изменения (clearTheme / watchSystemTheme / вызов из другого модуля).
    window.addEventListener("theme-changed", (e) => {
        const next = e && e.detail ? e.detail.theme : null;
        if (next && selectEl.value !== next) {
            selectEl.value = next;
        }
    });

    return true;
}