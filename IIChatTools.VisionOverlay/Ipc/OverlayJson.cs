using System.Text.Json;
using System.Text.Json.Serialization;

namespace IIChatTools.VisionOverlay.Ipc
{
    /// <summary>
    /// Общие настройки JSON для IPC overlay.
    /// Один источник истины: и сервер (<c>OverlayPipeServer</c>),
    /// и клиент (<c>WpfVisionOverlayHandle</c> в IIChatTools.Services)
    /// используют одинаковые опции.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6.
    /// </para>
    /// <para>
    /// <b>Формат:</b> одна JSON-строка на команду, разделитель — <c>\n</c>.
    /// camelCase property-имена (совместимо со стилем SSE и MVC).
    /// <c>PropertyNameCaseInsensitive = true</c> — обе стороны терпимы к регистру.
    /// </para>
    /// </remarks>
    internal static class OverlayJson
    {
        /// <summary>
        /// Канонические опции. Использовать и на сервере, и на клиенте
        /// — иначе camelCase-имена разъедутся.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Сериализует команду в одну строку JSON (без переноса строки в конце).
        /// </summary>
        public static string Serialize<T>(T value) =>
            JsonSerializer.Serialize(value, Options);

        /// <summary>
        /// Десериализует строку JSON. Возвращает <c>null</c> при ошибке —
        /// сервер не должен падать из-за мусорной команды.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (JsonException)
            {
                return default;
            }
        }
    }
}