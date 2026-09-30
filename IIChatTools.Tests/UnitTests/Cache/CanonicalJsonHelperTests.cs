using System;
using IIChatTools.Services.Implementation.Cache;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Cache
{
    /// <summary>
    /// Тесты <see cref="CanonicalJsonHelper"/> (v1.8.2, Шаг 1.3).
    ///
    /// <para>
    /// Покрывают: канонизацию (порядок ключей, рекурсия, массивы),
    /// SHA-256, формат ключа кэша.
    /// </para>
    /// </summary>
    public class CanonicalJsonHelperTests
    {
        // ============================================================
        // Canonicalize
        // ============================================================

        [Fact]
        public void Canonicalize_SameKeysDifferentOrder_SameOutput()
        {
            var a = new JObject { ["b"] = 2, ["a"] = 1 };
            var b = new JObject { ["a"] = 1, ["b"] = 2 };

            Assert.Equal(
                CanonicalJsonHelper.Canonicalize(a),
                CanonicalJsonHelper.Canonicalize(b));
        }

        [Fact]
        public void Canonicalize_NestedObjects_SortsRecursively()
        {
            var a = new JObject
            {
                ["z"] = new JObject { ["y"] = 1, ["x"] = 2 },
                ["a"] = new JObject { ["b"] = 3, ["a"] = 4 }
            };
            var b = new JObject
            {
                ["a"] = new JObject { ["a"] = 4, ["b"] = 3 },
                ["z"] = new JObject { ["x"] = 2, ["y"] = 1 }
            };

            Assert.Equal(
                CanonicalJsonHelper.Canonicalize(a),
                CanonicalJsonHelper.Canonicalize(b));
        }

        [Fact]
        public void Canonicalize_ArrayOrderPreserved()
        {
            var a = new JObject { ["arr"] = new JArray { 1, 2, 3 } };
            var b = new JObject { ["arr"] = new JArray { 3, 2, 1 } };

            // Порядок в массиве — семантически значим → канонизация НЕ сортирует.
            Assert.NotEqual(
                CanonicalJsonHelper.Canonicalize(a),
                CanonicalJsonHelper.Canonicalize(b));
        }

        [Fact]
        public void Canonicalize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => CanonicalJsonHelper.Canonicalize(null));
        }

        // ============================================================
        // Sha256Hex
        // ============================================================

        [Fact]
        public void Sha256Hex_EmptyInput_ReturnsKnownHash()
        {
            // SHA-256("") — стандартное значение.
            const string expected =
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

            Assert.Equal(expected, CanonicalJsonHelper.Sha256Hex(""));
        }

        [Fact]
        public void Sha256Hex_LowercaseHex()
        {
            // Проверяем, что hash — lowercase (важно для консистентности ключей).
            var hash = CanonicalJsonHelper.Sha256Hex("abc");

            Assert.Equal(hash, hash.ToLowerInvariant());
            Assert.DoesNotContain("A", hash);
            Assert.DoesNotContain("F", hash);
        }

        // ============================================================
        // BuildCacheKey
        // ============================================================

        [Fact]
        public void BuildCacheKey_Format_HasToolNameAndUserId()
        {
            var key = CanonicalJsonHelper.BuildCacheKey(
                "web_search", 1, new JObject());

            Assert.StartsWith("tool:", key);
            Assert.Contains(":web_search:", key);
            Assert.Contains(":u1:", key);
        }

        [Fact]
        public void BuildCacheKey_DifferentUsers_DifferentKeys()
        {
            var args = new JObject { ["q"] = "x" };

            var k1 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, args);
            var k2 = CanonicalJsonHelper.BuildCacheKey("web_search", 2, args);

            Assert.NotEqual(k1, k2);
        }

        [Fact]
        public void BuildCacheKey_DifferentTools_DifferentKeys()
        {
            var args = new JObject { ["q"] = "x" };

            var k1 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, args);
            var k2 = CanonicalJsonHelper.BuildCacheKey("wikipedia_search", 1, args);

            Assert.NotEqual(k1, k2);
        }

        [Fact]
        public void BuildCacheKey_DifferentKeyOrder_SameKey()
        {
            var a = new JObject { ["a"] = 1, ["b"] = 2 };
            var b = new JObject { ["b"] = 2, ["a"] = 1 };

            var k1 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, a);
            var k2 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, b);

            Assert.Equal(k1, k2);
        }

        [Fact]
        public void BuildCacheKey_NullArguments_TreatedAsEmptyObject()
        {
            var k1 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, null);
            var k2 = CanonicalJsonHelper.BuildCacheKey("web_search", 1, new JObject());

            Assert.Equal(k1, k2);
        }
    }
}