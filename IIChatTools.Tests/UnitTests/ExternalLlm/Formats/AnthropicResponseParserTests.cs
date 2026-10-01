using System;
using IIChatTools.Services.Implementation.ExternalLlm.Formats;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm.Formats
{
    /// <summary>
    /// Тесты <see cref="AnthropicResponseParser"/> (v1.9.0, KI-110a, Фаза 2.2).
    /// </summary>
    public class AnthropicResponseParserTests
    {
        /// <summary>
        /// Собирает типовой успешный ответ Anthropic с указанными
        /// text-блоками. Используется большинством тестов.
        /// </summary>
        private static JObject ResponseWithContent(params string[] texts)
        {
            var contentArray = new JArray();
            foreach (var text in texts)
            {
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = text
                });
            }

            return new JObject
            {
                ["id"] = "msg_01ABC",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = contentArray,
                ["model"] = "claude-haiku-4-5",
                ["stop_reason"] = "end_turn",
                ["usage"] = new JObject
                {
                    ["input_tokens"] = 20,
                    ["output_tokens"] = 3
                }
            };
        }

        [Fact]
        public void Parse_SingleTextBlock_ReturnsContent()
        {
            var response = ResponseWithContent("Четыре");

            var (content, prompt, completion) = AnthropicResponseParser.Parse(response);

            Assert.Equal("Четыре", content);
            Assert.Equal(20, prompt);
            Assert.Equal(3, completion);
        }

        [Fact]
        public void Parse_MultipleTextBlocks_JoinedWithNewline()
        {
            var response = ResponseWithContent("Первая", "Вторая", "Третья");

            var (content, _, _) = AnthropicResponseParser.Parse(response);

            Assert.Equal("Первая\nВторая\nТретья", content);
        }

        [Fact]
        public void Parse_EmptyContentArray_ReturnsEmptyString()
        {
            var response = new JObject
            {
                ["content"] = new JArray(),
                ["usage"] = new JObject
                {
                    ["input_tokens"] = 10,
                    ["output_tokens"] = 0
                }
            };

            var (content, prompt, completion) = AnthropicResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(10, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_NoContentField_ReturnsEmptyString()
        {
            var response = new JObject
            {
                ["usage"] = new JObject
                {
                    ["input_tokens"] = 5,
                    ["output_tokens"] = 2
                }
            };

            var (content, prompt, completion) = AnthropicResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(5, prompt);
            Assert.Equal(2, completion);
        }

        [Fact]
        public void Parse_ToolUseBlocksIgnored()
        {
            var response = new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "tool_use", ["id"] = "tool_1", ["name"] = "foo" },
                    new JObject { ["type"] = "text", ["text"] = "Только это" },
                    new JObject { ["type"] = "tool_use", ["id"] = "tool_2", ["name"] = "bar" }
                },
                ["usage"] = new JObject
                {
                    ["input_tokens"] = 50,
                    ["output_tokens"] = 30
                }
            };

            var (content, prompt, completion) = AnthropicResponseParser.Parse(response);

            Assert.Equal("Только это", content);
            Assert.Equal(50, prompt);
            Assert.Equal(30, completion);
        }

        [Fact]
        public void Parse_EmptyTextBlock_SkippedWithoutExtraNewline()
        {
            // Пустой text-блок не должен добавлять лишний \n.
            var response = ResponseWithContent("Первая", "", "Вторая");

            var (content, _, _) = AnthropicResponseParser.Parse(response);

            Assert.Equal("Первая\nВторая", content);
        }

        [Fact]
        public void Parse_UsageMissing_ReturnsZeroTokens()
        {
            var response = new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = "ok" }
                }
            };

            var (content, prompt, completion) = AnthropicResponseParser.Parse(response);

            Assert.Equal("ok", content);
            Assert.Equal(0, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_NullResponse_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => AnthropicResponseParser.Parse(null));
        }
    }
}