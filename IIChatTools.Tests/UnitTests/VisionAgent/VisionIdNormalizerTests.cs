using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// KI-193: тесты нормализации id UI-элементов.
    /// </summary>
    public class VisionIdNormalizerTests
    {
        [Theory]
        [InlineData("search_btn", "search_button")]
        [InlineData("search_butt", "search_button")]
        [InlineData("login_lnk", "login_link")]
        [InlineData("email_field", "email_input")]
        [InlineData("email_inp", "email_input")]
        [InlineData("username_tb", "username_input")]
        [InlineData("title_txt", "title_text")]
        [InlineData("remember_chk", "remember_checkbox")]
        [InlineData("remember_cb", "remember_checkbox")]
        [InlineData("country_dd", "country_dropdown")]
        [InlineData("country_sel", "country_dropdown")]
        [InlineData("filter_opt", "filter_option")]
        public void Normalize_ShortSuffix_ReturnsLongForm(string input, string expected)
        {
            Assert.Equal(expected, VisionIdNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData("search_button")]
        [InlineData("login_link")]
        [InlineData("email_input")]
        [InlineData("search_input")]
        [InlineData("main_heading")]
        [InlineData("article_title")]
        [InlineData("language_link")]
        [InlineData("new_chat_button")]
        public void Normalize_AlreadyNormalized_ReturnsSame(string input)
        {
            Assert.Equal(input, VisionIdNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData("Search_BTN", "Search_button")]
        [InlineData("Login_LNK", "Login_link")]
        public void Normalize_CasePreservedInPrefix(string input, string expected)
        {
            Assert.Equal(expected, VisionIdNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Normalize_NullOrEmpty_ReturnsInput(string input)
        {
            Assert.Equal(input, VisionIdNormalizer.Normalize(input));
        }
    }
}