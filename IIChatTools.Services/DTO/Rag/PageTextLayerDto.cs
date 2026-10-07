using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Текстовый слой одной страницы документа
    /// (v1.13.x, KI-207).
    ///
    /// <para>
    /// Хранится в <c>page-N.json</c> рядом с <c>page-N.png</c>.
    /// Координаты — в natural PNG pixels (top-left origin, y↓).
    /// Frontend масштабирует через <c>transform: scale()</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Источник слов:</b>
    /// <list type="bullet">
    ///   <item>OCR-страницы → Tesseract <c>Page.GetWords()</c>;</item>
    ///   <item>текстовые PDF → PdfPig <c>Page.GetWords()</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PageTextLayerDto
    {
        /// <summary>Ширина natural PNG (в пикселях).</summary>
        public int Width { get; set; }

        /// <summary>Высота natural PNG (в пикселях).</summary>
        public int Height { get; set; }

        /// <summary>
        /// Слова с bounding box'ами (в порядке обхода страницы).
        /// Пустой список — нет текстового слоя (не сохраняем JSON).
        /// </summary>
        public List<WordBoxDto> Words { get; set; } = new List<WordBoxDto>();
    }

    /// <summary>
    /// Bounding box одного слова (natural PNG pixels, top-left origin).
    /// </summary>
    public class WordBoxDto
    {
        /// <summary>Текст слова (без пробелов).</summary>
        public string Text { get; set; }

        /// <summary>X-координата левого верхнего угла.</summary>
        public double X { get; set; }

        /// <summary>Y-координата левого верхнего угла.</summary>
        public double Y { get; set; }

        /// <summary>Ширина.</summary>
        public double W { get; set; }

        /// <summary>Высота.</summary>
        public double H { get; set; }
    }
}