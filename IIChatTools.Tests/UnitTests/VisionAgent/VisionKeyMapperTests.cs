using IIChatTools.Services.Implementation.VisionAgent;
using Xunit;

namespace IIChatTools.Tests.UnitTests.VisionAgent
{
    /// <summary>
    /// Unit-тесты для <see cref="VisionKeyMapper"/> (v1.12.0, KI-131, Ф2.6).
    /// </summary>
    public class VisionKeyMapperTests
    {
        // ============ Простые клавиши ============

        [Theory]
        [InlineData("Enter", 0x0D)]
        [InlineData("Return", 0x0D)]
        [InlineData("Tab", 0x09)]
        [InlineData("Escape", 0x1B)]
        [InlineData("Esc", 0x1B)]
        [InlineData("Space", 0x20)]
        [InlineData("Backspace", 0x08)]
        [InlineData("Delete", 0x2E)]
        [InlineData("Del", 0x2E)]
        [InlineData("Home", 0x24)]
        [InlineData("End", 0x23)]
        [InlineData("PageUp", 0x21)]
        [InlineData("PgUp", 0x21)]
        [InlineData("PageDown", 0x22)]
        [InlineData("Left", 0x25)]
        [InlineData("Right", 0x27)]
        [InlineData("Up", 0x26)]
        [InlineData("Down", 0x28)]
        public void GetVirtualKey_NamedKey_ReturnsExpected(string name, int expected)
        {
            var vk = VisionKeyMapper.GetVirtualKey(name);
            Assert.NotNull(vk);
            Assert.Equal((ushort)expected, vk.Value);
        }

        [Theory]
        [InlineData("A", 0x41)]
        [InlineData("a", 0x41)]
        [InlineData("Z", 0x5A)]
        [InlineData("0", 0x30)]
        [InlineData("9", 0x39)]
        public void GetVirtualKey_LetterOrDigit_ReturnsExpected(string name, int expected)
        {
            var vk = VisionKeyMapper.GetVirtualKey(name);
            Assert.Equal((ushort)expected, vk);
        }

        [Theory]
        [InlineData("F1", 0x70)]
        [InlineData("F5", 0x74)]
        [InlineData("F12", 0x7B)]
        public void GetVirtualKey_FunctionKey_ReturnsExpected(string name, int expected)
        {
            var vk = VisionKeyMapper.GetVirtualKey(name);
            Assert.Equal((ushort)expected, vk);
        }

        // ============ Case-insensitive ============

        [Theory]
        [InlineData("ENTER")]
        [InlineData("enter")]
        [InlineData("EnTeR")]
        public void GetVirtualKey_CaseInsensitive_ReturnsExpected(string name)
        {
            Assert.Equal((ushort)0x0D, VisionKeyMapper.GetVirtualKey(name));
        }

        // ============ Неизвестные / null ============

        [Theory]
        [InlineData("Qwerty")]
        [InlineData("F13")]
        [InlineData("F0")]
        [InlineData("F99")]
        [InlineData("SuperKey")]
        public void GetVirtualKey_Unknown_ReturnsNull(string name)
        {
            Assert.Null(VisionKeyMapper.GetVirtualKey(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetVirtualKey_Empty_ReturnsNull(string name)
        {
            Assert.Null(VisionKeyMapper.GetVirtualKey(name));
        }

        // ============ Модификаторы ============

        [Theory]
        [InlineData("Ctrl", 0xA2)]
        [InlineData("Control", 0xA2)]
        [InlineData("LCtrl", 0xA2)]
        [InlineData("RCtrl", 0xA3)]
        [InlineData("Alt", 0xA4)]
        [InlineData("RAlt", 0xA5)]
        [InlineData("Shift", 0xA0)]
        [InlineData("RShift", 0xA1)]
        [InlineData("Win", 0x5B)]
        [InlineData("Meta", 0x5B)]
        [InlineData("RWin", 0x5C)]
        public void GetModifierVirtualKey_Known_ReturnsExpected(string name, int expected)
        {
            var vk = VisionKeyMapper.GetModifierVirtualKey(name);
            Assert.Equal((ushort)expected, vk);
        }

        [Fact]
        public void GetModifierVirtualKey_NotAModifier_ReturnsNull()
        {
            Assert.Null(VisionKeyMapper.GetModifierVirtualKey("A"));
            Assert.Null(VisionKeyMapper.GetModifierVirtualKey("Enter"));
        }

        // ============ IsModifier ============

        [Theory]
        [InlineData("Ctrl")]
        [InlineData("ALT")]
        [InlineData("shift")]
        [InlineData("Win")]
        [InlineData("RAlt")]
        public void IsModifier_KnownModifier_ReturnsTrue(string name)
        {
            Assert.True(VisionKeyMapper.IsModifier(name));
        }

        [Theory]
        [InlineData("A")]
        [InlineData("Enter")]
        [InlineData("F12")]
        [InlineData("")]
        [InlineData(null)]
        public void IsModifier_NotAModifier_ReturnsFalse(string name)
        {
            Assert.False(VisionKeyMapper.IsModifier(name));
        }
    }
}