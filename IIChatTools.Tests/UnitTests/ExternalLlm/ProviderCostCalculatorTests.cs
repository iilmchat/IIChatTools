using IIChatTools.Services.DTO.ExternalLlm;
using IIChatTools.Services.Implementation.ExternalLlm;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm
{
    /// <summary>
    /// Тесты <see cref="ProviderCostCalculator"/> (v1.8.1, KI-109, Фаза 2.4).
    /// </summary>
    public class ProviderCostCalculatorTests
    {
        private static ExternalProviderOptions Provider(
            decimal inputPer1k = 0.001m,
            decimal outputPer1k = 0.002m)
            => new ExternalProviderOptions
            {
                CostPer1kInputUsd = inputPer1k,
                CostPer1kOutputUsd = outputPer1k
            };

        [Fact]
        public void Calculate_NullOptions_ReturnsZero()
        {
            Assert.Equal(0m, ProviderCostCalculator.Calculate(100, 200, null));
        }

        [Fact]
        public void Calculate_ZeroTokens_ZeroCost()
        {
            Assert.Equal(0m, ProviderCostCalculator.Calculate(0, 0, Provider()));
        }

        [Fact]
        public void Calculate_OnlyInput_CalculatesCorrectly()
        {
            // 2000 токенов * 0.001 / 1000 = 0.002
            Assert.Equal(0.002m, ProviderCostCalculator.Calculate(2000, 0, Provider()));
        }

        [Fact]
        public void Calculate_OnlyOutput_CalculatesCorrectly()
        {
            // 500 токенов * 0.002 / 1000 = 0.001
            Assert.Equal(0.001m, ProviderCostCalculator.Calculate(0, 500, Provider()));
        }

        [Fact]
        public void Calculate_Both_ReturnsSum()
        {
            // 1000 * 0.001 / 1000 = 0.001 (input)
            // 2000 * 0.002 / 1000 = 0.004 (output)
            // total = 0.005
            Assert.Equal(0.005m, ProviderCostCalculator.Calculate(1000, 2000, Provider()));
        }

        [Fact]
        public void Calculate_NegativeCost_ClampedToZero()
        {
            var provider = Provider(inputPer1k: -0.001m, outputPer1k: -0.002m);
            Assert.Equal(0m, ProviderCostCalculator.Calculate(1000, 1000, provider));
        }

        [Fact]
        public void Calculate_NegativeTokens_ClampedToZero()
        {
            Assert.Equal(0m, ProviderCostCalculator.Calculate(-100, -200, Provider()));
        }

        [Fact]
        public void Calculate_FreeProvider_ZeroCost()
        {
            // Groq: CostPer1k* = 0
            var provider = Provider(inputPer1k: 0m, outputPer1k: 0m);
            Assert.Equal(0m, ProviderCostCalculator.Calculate(10000, 5000, provider));
        }

        [Fact]
        public void Calculate_RealisticDeepSeek_Pricing()
        {
            // DeepSeek: input=0.00014, output=0.00028.
            // 245 prompt + 512 completion.
            // input:  245 * 0.00014 / 1000 = 0.0000343
            // output: 512 * 0.00028 / 1000 = 0.00014336
            // total ≈ 0.00017766
            var provider = Provider(inputPer1k: 0.00014m, outputPer1k: 0.00028m);
            var cost = ProviderCostCalculator.Calculate(245, 512, provider);

            Assert.Equal(0.00017766m, cost);
        }
    }
}