using System;
using IIChatTools.Services.Implementation.ExternalLlm.Formats;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.ExternalLlm.Formats
{
    /// <summary>
    /// Тесты <see cref="GeminiResponseParser"/> (v1.10.0, KI-110b, Фаза 2).
    /// </summary>
    public class GeminiResponseParserTests
    {
        /// <summary>
        /// Собирает типовой успешный ответ Gemini с указанными text-блоками
        /// в первом part-элементе (второй аргумент добавляет второй part).
        /// Используется большинством тестов.
        /// </summary>
        private static JObject ResponseWithParts(params string[] texts)
        {
            var partsArray = new JArray();
            foreach (var text in texts)
            {
                partsArray.Add(new JObject { ["text"] = text });
            }

            return new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = partsArray
                        },
                        ["finishReason"] = "STOP",
                        ["index"] = 0
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 20,
                    ["candidatesTokenCount"] = 3,
                    ["totalTokenCount"] = 23
                },
                ["modelVersion"] = "gemini-2.0-flash-001",
                ["responseId"] = "test-id"
            };
        }

        [Fact]
        public void Parse_SingleTextPart_ReturnsContent()
        {
            var response = ResponseWithParts("Четыре");

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("Четыре", content);
            Assert.Equal(20, prompt);
            Assert.Equal(3, completion);
        }

        [Fact]
        public void Parse_MultipleParts_JoinedWithNewline()
        {
            var response = ResponseWithParts("Первая", "Вторая", "Третья");

            var (content, _, _) = GeminiResponseParser.Parse(response);

            Assert.Equal("Первая\nВторая\nТретья", content);
        }

        [Fact]
        public void Parse_EmptyParts_ReturnsEmptyString()
        {
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = new JArray()
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 10,
                    ["candidatesTokenCount"] = 0
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(10, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_NoCandidates_ReturnsEmptyAndUsage()
        {
            // Сценарий: promptFeedback.blockReason — candidates[] отсутствует.
            var response = new JObject
            {
                ["promptFeedback"] = new JObject
                {
                    ["blockReason"] = "SAFETY"
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 15,
                    ["candidatesTokenCount"] = 0
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(15, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_EmptyCandidatesArray_ReturnsEmptyString()
        {
            var response = new JObject
            {
                ["candidates"] = new JArray(),
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 5,
                    ["candidatesTokenCount"] = 0
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(5, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_NoContentField_ReturnsEmptyString()
        {
            // Блоки с functionCall/inlineData без text — не наш сценарий,
            // но парсер не должен падать.
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 5,
                    ["candidatesTokenCount"] = 2
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(5, prompt);
            Assert.Equal(2, completion);
        }

        [Fact]
        public void Parse_ThoughtBlocksIgnored()
        {
            // thought: true — блок размышлений (v1.10.0 без thinking,
            // но структура может прийти от модели, поддерживающей thinking).
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = new JArray
                            {
                                new JObject { ["thought"] = true, ["text"] = "Думаю..." },
                                new JObject { ["text"] = "Только это" },
                                new JObject { ["thought"] = true, ["text"] = "Ещё думаю..." }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 50,
                    ["candidatesTokenCount"] = 30
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("Только это", content);
            Assert.Equal(50, prompt);
            Assert.Equal(30, completion);
        }

        [Fact]
        public void Parse_FunctionCallPartsIgnored()
        {
            // Function call parts (без text) — игнорируются.
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = new JArray
                            {
                                new JObject
                                {
                                    ["functionCall"] = new JObject
                                    {
                                        ["name"] = "get_weather",
                                        ["args"] = new JObject { ["city"] = "Moscow" }
                                    }
                                },
                                new JObject { ["text"] = "Погода в Москве:" }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 100,
                    ["candidatesTokenCount"] = 50
                }
            };

            var (content, _, _) = GeminiResponseParser.Parse(response);

            Assert.Equal("Погода в Москве:", content);
        }

        [Fact]
        public void Parse_EmptyTextPart_SkippedWithoutExtraNewline()
        {
            // Пустой text не должен добавлять лишний \n.
            var response = ResponseWithParts("Первая", "", "Вторая");

            var (content, _, _) = GeminiResponseParser.Parse(response);

            Assert.Equal("Первая\nВторая", content);
        }

        [Fact]
        public void Parse_UsageMetadataMissing_ReturnsZeroTokens()
        {
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = new JArray
                            {
                                new JObject { ["text"] = "ok" }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("ok", content);
            Assert.Equal(0, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_SafetyFinishReason_EmptyContentOk()
        {
            // SAFETY: candidates есть, но parts пуст (модель заблокировала ответ).
            var response = new JObject
            {
                ["candidates"] = new JArray
                {
                    new JObject
                    {
                        ["content"] = new JObject
                        {
                            ["role"] = "model",
                            ["parts"] = new JArray()
                        },
                        ["finishReason"] = "SAFETY",
                        ["index"] = 0
                    }
                },
                ["usageMetadata"] = new JObject
                {
                    ["promptTokenCount"] = 20,
                    ["candidatesTokenCount"] = 0
                }
            };

            var (content, prompt, completion) = GeminiResponseParser.Parse(response);

            Assert.Equal("", content);
            Assert.Equal(20, prompt);
            Assert.Equal(0, completion);
        }

        [Fact]
        public void Parse_NullResponse_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => GeminiResponseParser.Parse(null));
        }
    }
}