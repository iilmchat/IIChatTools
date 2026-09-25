using System;
using IIChatTools.Services.Implementation.Rag;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты математических операций над векторами
    /// (v1.5.0, KI-083, Шаг 2B).
    ///
    /// <para>
    /// Float-точность: сравнение через tolerance <c>1e-5</c>.
    /// DESIGN § 4.2.4 (5 тестов) → +1 edge-case (different lengths) = 6.
    /// </para>
    /// </summary>
    public class VectorMathTests
    {
        /// <summary>Допуск для сравнения float-значений.</summary>
        private const float Tolerance = 1e-5f;

        /// <summary>
        /// L2Normalize на векторе [3, 4] → [0.6, 0.8] (норма = 1).
        /// </summary>
        [Fact]
        public void L2Normalize_ValidVector_ReturnsUnitVector()
        {
            var input = new[] { 3f, 4f };
            var result = VectorMath.L2Normalize(input);

            Assert.Equal(2, result.Length);
            Assert.Equal(0.6f, result[0], Tolerance);
            Assert.Equal(0.8f, result[1], Tolerance);

            // Длина = sqrt(0.36 + 0.64) = 1
            var length = Math.Sqrt(result[0] * result[0] + result[1] * result[1]);
            Assert.Equal(1.0, length, 5);
        }

        /// <summary>
        /// L2Normalize на нулевом векторе → ArgumentException.
        /// </summary>
        [Fact]
        public void L2Normalize_ZeroVector_Throws()
        {
            var input = new[] { 0f, 0f, 0f };
            Assert.Throws<ArgumentException>(() => VectorMath.L2Normalize(input));
        }

        /// <summary>
        /// DotProduct ортогональных векторов [1,0] · [0,1] = 0.
        /// </summary>
        [Fact]
        public void DotProduct_OrthogonalVectors_ReturnsZero()
        {
            var a = new[] { 1f, 0f };
            var b = new[] { 0f, 1f };

            var result = VectorMath.DotProduct(a, b);

            Assert.Equal(0f, result, Tolerance);
        }

        /// <summary>
        /// DotProduct с разными длинами → ArgumentException.
        /// </summary>
        [Fact]
        public void DotProduct_DifferentLengths_Throws()
        {
            var a = new[] { 1f, 2f };
            var b = new[] { 1f, 2f, 3f };

            Assert.Throws<ArgumentException>(() => VectorMath.DotProduct(a, b));
        }

        /// <summary>
        /// CosineSimilarity противоположных векторов [-1,0] и [1,0] = -1.
        /// </summary>
        [Fact]
        public void CosineSimilarity_OppositeVectors_ReturnsMinusOne()
        {
            var a = new[] { -1f, 0f };
            var b = new[] { 1f, 0f };

            var result = VectorMath.CosineSimilarity(a, b);

            Assert.Equal(-1f, result, Tolerance);
        }

        /// <summary>
        /// CosineSimilarity параллельных векторов [1,0] и [1,0] = 1.
        /// </summary>
        [Fact]
        public void CosineSimilarity_ParallelVectors_ReturnsOne()
        {
            var a = new[] { 1f, 0f };
            var b = new[] { 1f, 0f };

            var result = VectorMath.CosineSimilarity(a, b);

            Assert.Equal(1f, result, Tolerance);
        }
    }
}