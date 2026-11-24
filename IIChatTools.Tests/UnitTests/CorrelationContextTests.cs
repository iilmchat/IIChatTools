using System;
using IIChatTools.Services.Utils;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>D10: тесты correlation context.</summary>
    public class CorrelationContextTests
    {
        [Fact]
        public void Begin_SetsAndRestores()
        {
            var previous = CorrelationContext.Current;
            using (var scope = CorrelationContext.Begin("test-123"))
            {
                Assert.Equal("test-123", CorrelationContext.Current);
            }
            Assert.Equal(previous, CorrelationContext.Current);
        }

        [Fact]
        public void Begin_WithoutId_GeneratesNonEmpty()
        {
            using (CorrelationContext.Begin())
            {
                Assert.False(string.IsNullOrWhiteSpace(CorrelationContext.Current));
            }
        }
    }
}
