using System;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Математические операции над векторами для RAG
    /// (v1.5.0, KI-083, Шаг 2B).
    ///
    /// <para>
    /// Все методы — статические, без состояния. Оптимизированы для
    /// горячего пути <c>IVectorStore.Search</c>: после L2-нормализации
    /// cosine similarity = dot product (экономия на делении в цикле).
    /// </para>
    /// </summary>
    public static class VectorMath
    {
        /// <summary>
        /// Порог «нулевой нормы»: <c>||v|| &lt; 1e-12</c> считается нулевым
        /// вектором (защита от деления на ноль при float-погрешностях).
        /// </summary>
        private const double ZeroNormThreshold = 1e-12;

        /// <summary>
        /// L2-нормализация: <c>v / ||v||</c>, где <c>||v|| = sqrt(Σ v[i]²)</c>.
        ///
        /// <para>
        /// Возвращает новый массив, не мутирует входной. Бросает исключение,
        /// если вектор пустой или нулевой (норма = 0).
        /// </para>
        /// </summary>
        /// <param name="vector">Исходный вектор</param>
        /// <returns>Новый нормализованный вектор (длина = 1)</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="vector"/> = null</exception>
        /// <exception cref="ArgumentException">Если вектор пустой или нулевой</exception>
        public static float[] L2Normalize(float[] vector)
        {
            if (vector == null)
                throw new ArgumentNullException(nameof(vector));

            if (vector.Length == 0)
                throw new ArgumentException("Вектор не может быть пустым", nameof(vector));

            var norm = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                norm += (double)vector[i] * vector[i];
            }
            norm = Math.Sqrt(norm);

            if (norm < ZeroNormThreshold)
                throw new ArgumentException(
                    "Вектор имеет нулевую норму (нельзя нормализовать)", nameof(vector));

            var result = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                result[i] = (float)(vector[i] / norm);
            }
            return result;
        }

        /// <summary>
        /// Скалярное произведение: <c>Σ a[i] · b[i]</c>.
        ///
        /// <para>
        /// Для L2-нормализованных векторов результат равен cosine similarity,
        /// поэтому этот метод — основной в горячем цикле <c>Search</c>.
        /// </para>
        /// </summary>
        /// <param name="a">Первый вектор</param>
        /// <param name="b">Второй вектор</param>
        /// <returns>Скалярное произведение</returns>
        /// <exception cref="ArgumentNullException">Если любой из векторов = null</exception>
        /// <exception cref="ArgumentException">Если длины векторов не совпадают</exception>
        public static float DotProduct(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.Length != b.Length)
                throw new ArgumentException(
                    $"Длины векторов не совпадают: {a.Length} vs {b.Length}");

            // double-аккумулятор: снижает погрешность на длинных векторах
            // (768 dim × 10k операций) — для float-накопления потеря точности заметна.
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += (double)a[i] * b[i];
            }
            return (float)sum;
        }

        /// <summary>
        /// Cosine similarity: <c>dot(a, b) / (||a|| · ||b||)</c>.
        ///
        /// <para>
        /// Универсальный метод (не требует предварительной нормализации).
        /// Внутри <c>IVectorStore.Search</c> используется <see cref="DotProduct"/> —
        /// там векторы уже нормализованы при <c>Add</c>/<c>Search</c>.
        /// </para>
        /// </summary>
        /// <param name="a">Первый вектор</param>
        /// <param name="b">Второй вектор</param>
        /// <returns>Cosine similarity (-1..1)</returns>
        /// <exception cref="ArgumentNullException">Если любой из векторов = null</exception>
        /// <exception cref="ArgumentException">
        /// Если длины не совпадают, вектор пустой или один из векторов нулевой
        /// </exception>
        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.Length != b.Length)
                throw new ArgumentException(
                    $"Длины векторов не совпадают: {a.Length} vs {b.Length}");

            if (a.Length == 0)
                throw new ArgumentException("Векторы не могут быть пустыми");

            var dot = DotProduct(a, b);

            double normA = 0.0, normB = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                normA += (double)a[i] * a[i];
                normB += (double)b[i] * b[i];
            }
            normA = Math.Sqrt(normA);
            normB = Math.Sqrt(normB);

            if (normA < ZeroNormThreshold || normB < ZeroNormThreshold)
                throw new ArgumentException("Один из векторов имеет нулевую норму");

            return (float)(dot / (normA * normB));
        }
    }
}