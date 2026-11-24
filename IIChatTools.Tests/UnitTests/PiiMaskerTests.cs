using IIChatTools.Services.Utils;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>D09 / KI-138: тесты маскирования PII.</summary>
    public class PiiMaskerTests
    {
        [Fact]
        public void Mask_Email_Replaced()
        {
            var masked = PiiMasker.Mask("Напиши на ivanov@example.com сегодня");

            Assert.Contains("[EMAIL]", masked);
            Assert.DoesNotContain("ivanov@example.com", masked);
        }

        [Fact]
        public void Mask_RussianPhone_Replaced()
        {
            var masked = PiiMasker.Mask("Позвони +7 916 123-45-67");

            Assert.Contains("[PHONE]", masked);
            Assert.DoesNotContain("916", masked);
        }

        [Fact]
        public void Mask_CardAndSnils_Replaced()
        {
            var masked = PiiMasker.Mask("карта 4111 1111 1111 1111, СНИЛС 123-456-789 12");

            Assert.Contains("[CARD]", masked);
            Assert.Contains("[SNILS]", masked);
        }

        [Fact]
        public void Mask_NoPii_Unchanged()
        {
            const string text = "Обычный текст без персональных данных";
            Assert.Equal(text, PiiMasker.Mask(text));
        }
    }
}
