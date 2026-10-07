namespace IIChatTools.Services.DTO.VisionAgent
{
    /// <summary>
    /// Конфигурация подключения к Chrome через CDP
    /// (Chrome DevTools Protocol) — KI-161.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-161). Секция <c>VisionAgent:CoordinateProvider:Cdp</c>
    /// в appsettings.
    /// </para>
    /// <para>
    /// <b>Идея:</b> Chrome, запущенный с <c>--remote-debugging-port=9222</c>,
    /// отдаёт DOM-структуру через CDP. <c>DomCoordinateProvider</c>
    /// подключается к нему и находит элемент по label / позиции / типу —
    /// координаты <b>0 px ошибки</b>, в отличие от VL (±20-30 px).
    /// </para>
    /// <para>
    /// Порт CDP слушает <b>только loopback</b> (127.0.0.1) — недоступен извне.
    /// </para>
    /// </remarks>
    public class VisionCdpOptions
    {
        /// <summary>
        /// Включено ли CDP-подключение. Default: <c>true</c>.
        /// При <c>false</c> — backend не пытается подключиться,
        /// DOM-режим недоступен (только VL-fallback).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// URL CDP-эндпоинта. Default: <c>http://127.0.0.1:9222</c>.
        /// Меняется, если порт 9222 занят другим процессом.
        /// </summary>
        public string BrowserUrl { get; set; } = "http://127.0.0.1:9222";

        /// <summary>
        /// Таймаут подключения к CDP, мс. Default: 5000.
        /// Если Chrome не отвечает за это время — DOM-режим отключается
        /// для текущей задачи (без ошибки пользователю).
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Таймаут поиска элемента в DOM, мс. Default: 2000.
        /// Применяется к JS-скрипту внутри страницы (для позиционного матчинга).
        /// </summary>
        public int ElementSearchTimeoutMs { get; set; } = 2000;
    }
}