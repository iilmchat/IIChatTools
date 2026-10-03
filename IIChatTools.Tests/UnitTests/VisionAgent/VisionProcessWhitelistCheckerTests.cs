using System;
using System.Collections.Generic;
using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionProcessWhitelistChecker"/>
    /// (v1.12.0, KI-131, Ф2.9). Кроссплатформенные — не зависят от Win32.
    /// </summary>
    public class VisionProcessWhitelistCheckerTests
    {
        private static readonly List<string> DefaultWhitelist = new List<string>
        {
            "chrome", "msedge", "firefox"
        };

        // ============ IsProcessAllowed — positive ============

        [Theory]
        [InlineData("chrome")]
        [InlineData("msedge")]
        [InlineData("firefox")]
        [InlineData("Chrome")]
        [InlineData("CHROME")]
        [InlineData("ChRoMe")]
        public void IsProcessAllowed_Known_ReturnsTrue(string name)
        {
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                name, DefaultWhitelist, out _));
        }

        [Theory]
        [InlineData("chrome.exe")]
        [InlineData("Chrome.EXE")]
        [InlineData("chrome.exe ")]
        public void IsProcessAllowed_WithExeExtension_ReturnsTrue(string name)
        {
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                name, DefaultWhitelist, out _));
        }

        [Fact]
        public void IsProcessAllowed_WhitelistWithExe_MatchesBareName()
        {
            var whitelist = new List<string> { "chrome.exe", "msedge.exe" };
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                "chrome", whitelist, out _));
        }

        // ============ IsProcessAllowed — negative ============

        [Theory]
        [InlineData("notepad")]
        [InlineData("outlook")]
        [InlineData("1c")]
        [InlineData("excel")]
        public void IsProcessAllowed_Unknown_ReturnsFalse(string name)
        {
            Assert.False(VisionProcessWhitelistChecker.IsProcessAllowed(
                name, DefaultWhitelist, out var error));
            Assert.Contains(name, error);
            Assert.Contains("не в whitelist", error);
        }

        [Fact]
        public void IsProcessAllowed_Unknown_ErrorContainsAllowedList()
        {
            Assert.False(VisionProcessWhitelistChecker.IsProcessAllowed(
                "notepad", DefaultWhitelist, out var error));
            Assert.Contains("chrome", error);
            Assert.Contains("firefox", error);
        }

        // ============ Whitelist null / empty → allow all ============

        [Fact]
        public void IsProcessAllowed_NullWhitelist_ReturnsTrue()
        {
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                "notepad", null, out _));
        }

        [Fact]
        public void IsProcessAllowed_EmptyWhitelist_ReturnsTrue()
        {
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                "notepad", new List<string>(), out _));
        }

        [Fact]
        public void IsProcessAllowed_WhitespaceOnlyWhitelist_ReturnsTrue()
        {
            var whitelist = new List<string> { "", "   ", null };
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                "notepad", whitelist, out _));
        }

        // ============ ProcessName null / empty → allow (fail-open) ============

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsProcessAllowed_NullOrEmptyProcessName_ReturnsTrue(string name)
        {
            // Не можем проверить — не блокируем (fail-open).
            Assert.True(VisionProcessWhitelistChecker.IsProcessAllowed(
                name, DefaultWhitelist, out _));
        }

        // ============ NormalizeProcessName ============

        [Theory]
        [InlineData("chrome", "chrome")]
        [InlineData("chrome.exe", "chrome")]
        [InlineData("CHROME.EXE", "CHROME")]
        [InlineData("Chrome.EXE", "Chrome")]
        [InlineData("  chrome  ", "chrome")]
        [InlineData("  chrome.exe  ", "chrome")]
        public void NormalizeProcessName_ReturnsExpected(string input, string expected)
        {
            Assert.Equal(expected, VisionProcessWhitelistChecker.NormalizeProcessName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeProcessName_NullOrEmpty_ReturnsEmpty(string input)
        {
            Assert.Equal(string.Empty, VisionProcessWhitelistChecker.NormalizeProcessName(input));
        }

        [Fact]
        public void NormalizeProcessName_ExeButNotEnding_NoStrip()
        {
            Assert.Equal("chrome.old", VisionProcessWhitelistChecker.NormalizeProcessName("chrome.old"));
        }
    }
}