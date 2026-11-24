using IIChatTools.Services.Utils;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>D06: тесты ссылки на модель &lt;provider&gt;:&lt;model&gt;.</summary>
    public class ModelReferenceTests
    {
        [Fact]
        public void Parse_WithPrefix_ReturnsProviderAndModel()
        {
            var r = ModelReference.Parse("routerai:qwen/qwen3.8-flash");

            Assert.Equal("routerai", r.Provider);
            Assert.Equal("qwen/qwen3.8-flash", r.Model);
            Assert.Equal("routerai:qwen/qwen3.8-flash", r.ToString());
        }

        [Fact]
        public void Parse_LegacyNoPrefix_DefaultsToLmStudio()
        {
            var r = ModelReference.Parse("qwen/qwen3-4b");

            Assert.Equal("lmstudio", r.Provider);
            Assert.Equal("qwen/qwen3-4b", r.Model);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_Empty_DefaultsToLocalModel(string value)
        {
            var r = ModelReference.Parse(value);

            Assert.Equal("lmstudio", r.Provider);
            Assert.Equal("local-model", r.Model);
        }
    }
}
