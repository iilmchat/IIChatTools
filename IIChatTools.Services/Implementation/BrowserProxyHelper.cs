using System;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Хелпер для проверки, задан ли РЕАЛЬНЫЙ URL прокси (а не placeholder).
    ///
    /// <para>
    /// v1.13.6 (KI-218): общий helper для <see cref="BrowserSessionManager"/>
    /// и <c>BrowserOpenPageTool</c>. Регрессия KI-059 — placeholder
    /// <c>CHANGE_ME_VIA_USER_SECRETS</c> из appsettings трактовался как
    /// реальный прокси, Chromium падал с
    /// <c>net::ERR_PROXY_CONNECTION_FAILED</c>.
    /// </para>
    ///
    /// <para>
    /// Логика идентична <c>Startup.IsRealProxyUrl</c> (там — для
    /// <c>HttpClientFactory</c>) — общий источник истины для консистентности.
    /// </para>
    /// </summary>
    public static class BrowserProxyHelper
    {
        /// <summary>
        /// Проверяет, является ли значение реальным URL прокси.
        /// <list type="bullet">
        ///   <item><description>Пустое / whitespace → false.</description></item>
        ///   <item><description>Начинается с <c>CHANGE_ME</c> → false.</description></item>
        ///   <item><description>Не абсолютный URL → false.</description></item>
        ///   <item><description>Схема не http/https/socks → false.</description></item>
        ///   <item><description>Иначе → true.</description></item>
        /// </list>
        /// </summary>
        /// <param name="url">Значение из конфигурации (<c>Browser:ProxyServer</c>).</param>
        /// <returns>true, если URL валиден и не является placeholder.</returns>
        public static bool IsRealProxyUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Placeholder из appsettings.json (см. README → User Secrets).
            if (url.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                return false;

            // Должен быть абсолютный URL со схемой http/https/socks.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme.Equals("socks", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase);
        }
    }
}