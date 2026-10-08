using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Merge OCR-слов в VL-описание (KI-137).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-137). См.
    /// <c>docs/development/v1.13/DESIGN_VISION_OCR.md</c> § 4.8.
    /// </para>
    /// <para>
    /// <b>Стратегия:</b> матчить OCR-слова с <c>ui_elements</c> VL через
    /// центр bbox (расстояние ≤ <c>maxDistancePx</c>).
    /// Не матчившиеся слова в <c>ui_elements</c> не попадают, но сохраняются
    /// в <see cref="ScreenDescriptionDto.OcrText"/> — общий текст со экрана.
    /// </para>
    /// <para>
    /// <b>Source</b> элемента после merge:
    /// <list type="bullet">
    ///   <item><c>"ocr"</c> — у VL был пустой label, добавлен из OCR;</item>
    ///   <item><c>"merged"</c> — у VL был непустой label, обогащён OCR;</item>
    ///   <item><c>"vl"</c> (default) — элемент не тронут.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Static, без состояния.</b> Тестируется без DI.
    /// </para>
    /// </remarks>
    public static class OcrVlMergeHelper
    {
        /// <summary>
        /// Обогащает VL-описание OCR-словами.
        /// </summary>
        /// <param name="screen">
        /// VL-описание. <b>Мутируется</b> — обновляются <c>Label</c> и
        /// <c>Source</c> элементов, заполняются <c>OcrText</c> и
        /// <c>OcrWordsCount</c>.
        /// </param>
        /// <param name="ocrLayer">
        /// Результат OCR (координаты в full-res PNG pixels, top-left origin).
        /// Может быть <c>null</c> — тогда <paramref name="screen"/> вернётся
        /// без изменений.
        /// </param>
        /// <param name="screenshotScaleX">
        /// Коэффициент «full-res / screenshot» по X. Пример: 1.875,
        /// если full-res 1920, а screenshot 1024. Значения ≤ 0 трактуются
        /// как 1.0.
        /// </param>
        /// <param name="screenshotScaleY">
        /// Коэффициент «full-res / screenshot» по Y (см. <paramref name="screenshotScaleX"/>).
        /// </param>
        /// <param name="maxDistancePx">
        /// Максимальное расстояние (в screenshot-space) между центром
        /// OCR-слова и центром VL-элемента для merge. Больше — слово
        /// не привязывается к элементу (но остаётся в <c>OcrText</c>).
        /// </param>
        /// <returns>
        /// Тот же объект <paramref name="screen"/> (мутирован). Никогда
        /// не <c>null</c> — если вход был <c>null</c>, создаётся новый DTO.
        /// </returns>
        public static ScreenDescriptionDto Merge(
            ScreenDescriptionDto screen,
            PageTextLayerDto ocrLayer,
            double screenshotScaleX,
            double screenshotScaleY,
            int maxDistancePx)
        {
            // 1. Защита от null: screen всегда не-null на выходе.
            if (screen == null)
            {
                screen = new ScreenDescriptionDto();
            }

            // 2. Пустой OCR — нечего мёржить.
            if (ocrLayer?.Words == null || ocrLayer.Words.Count == 0)
            {
                screen.OcrWordsCount = 0;
                return screen;
            }

            // 3. Конвертация OCR-слов из full-res в screenshot-space.
            //    Значения ≤ 0 трактуются как 1.0 (защита от деления на 0).
            var scaleX = screenshotScaleX > 0 ? screenshotScaleX : 1.0;
            var scaleY = screenshotScaleY > 0 ? screenshotScaleY : 1.0;

            var wordsInShotSpace = ocrLayer.Words
                .Where(w => !string.IsNullOrWhiteSpace(w?.Text))
                .Select(w => new OcrWord
                {
                    Text = w.Text,
                    X = w.X / scaleX,
                    Y = w.Y / scaleY,
                    W = w.W / scaleX,
                    H = w.H / scaleY
                })
                .ToList();

            screen.OcrWordsCount = wordsInShotSpace.Count;

            // 4. OcrText — общий текст со экрана.
            screen.OcrText = BuildFullText(wordsInShotSpace);

            // 5. Нет VL-элементов — матчить не с чем. OcrText уже заполнен.
            if (screen.UiElements == null || screen.UiElements.Count == 0)
            {
                return screen;
            }

            // 6. Матчинг: для каждого OCR-слова — ближайший VL-элемент.
            //    Используем ссылочное равенство UiElementDto (Equals/GetHashCode
            //    не переопределены) — ключи словаря = те же объекты из screen.UiElements.
            var wordsByElement = new Dictionary<UiElementDto, List<OcrWord>>();

            foreach (var word in wordsInShotSpace)
            {
                var wordCx = word.X + word.W / 2;
                var wordCy = word.Y + word.H / 2;

                UiElementDto bestElement = null;
                var bestDist = double.MaxValue;

                foreach (var el in screen.UiElements)
                {
                    if (el.Center == null) continue;

                    var dx = el.Center.X - wordCx;
                    var dy = el.Center.Y - wordCy;
                    var dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestElement = el;
                    }
                }

                // Не нашли элемента / вышли за порог — слово остаётся
                // только в OcrText (общий текст), к ui_elements не привязано.
                if (bestElement == null || bestDist > maxDistancePx) continue;

                if (!wordsByElement.TryGetValue(bestElement, out var list))
                {
                    list = new List<OcrWord>();
                    wordsByElement[bestElement] = list;
                }
                list.Add(word);
            }

            // 7. Обновление Label / Source.
            foreach (var kvp in wordsByElement)
            {
                var el = kvp.Key;
                var words = kvp.Value;

                // Слова — в порядке чтения: сверху-вниз, слева-направо.
                var text = string.Join(" ", words
                    .OrderBy(w => w.Y)
                    .ThenBy(w => w.X)
                    .Select(w => w.Text));

                if (string.IsNullOrWhiteSpace(text)) continue;

                var hadLabel = !string.IsNullOrWhiteSpace(el.Label);
                el.Label = text.Trim();
                el.Source = hadLabel ? "merged" : "ocr";
            }

            return screen;
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Собирает единый текст из OCR-слов: слова группируются в строки
        /// по overlap y-диапазона (tolerance 5 px), внутри строки —
        /// сортировка по X. Строки соединяются через <c>\n</c>.
        /// </summary>
        /// <param name="words">OCR-слова (в screenshot-space).</param>
        /// <returns>
        /// Многострочный текст или <c>null</c>, если <paramref name="words"/>
        /// пуст/null.
        /// </returns>
        private static string BuildFullText(List<OcrWord> words)
        {
            if (words == null || words.Count == 0) return null;

            // 1. Сортируем по (Y, X) — «порядок чтения».
            var sorted = words
                .OrderBy(w => w.Y)
                .ThenBy(w => w.X)
                .ToList();

            // 2. Группируем в строки: слово принадлежит текущей строке,
            //    если его вертикальный центр отклоняется от среднего центра
            //    строки не более чем на LineTolerance.
            const double LineTolerance = 5.0;   // px в screenshot-space

            var lines = new List<List<OcrWord>>();

            foreach (var w in sorted)
            {
                var line = lines.LastOrDefault();
                if (line != null)
                {
                    var lineY = line.Average(l => l.Y + l.H / 2);
                    var wordY = w.Y + w.H / 2;

                    if (Math.Abs(lineY - wordY) <= LineTolerance)
                    {
                        line.Add(w);
                        continue;
                    }
                }

                lines.Add(new List<OcrWord> { w });
            }

            // 3. Формируем текст.
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var lineText = string.Join(" ", line
                    .OrderBy(w => w.X)
                    .Select(w => w.Text));

                sb.AppendLine(lineText);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Внутренний DTO одного OCR-слова (в screenshot-space).
        /// Не выходит за пределы <see cref="OcrVlMergeHelper"/>.
        /// </summary>
        private sealed class OcrWord
        {
            /// <summary>Текст слова.</summary>
            public string Text { get; set; }

            /// <summary>X-координата левого верхнего угла (screenshot-space).</summary>
            public double X { get; set; }

            /// <summary>Y-координата левого верхнего угла (screenshot-space).</summary>
            public double Y { get; set; }

            /// <summary>Ширина слова (screenshot-space).</summary>
            public double W { get; set; }

            /// <summary>Высота слова (screenshot-space).</summary>
            public double H { get; set; }
        }
    }
}