using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Тесты парсинга SSE-формата (без реального LM Studio).
    /// Проверяют извлечение content, tool_calls, finish_reason.
    /// </summary>
    public class LmStudioSseParseTests
    {
        /// <summary>
        /// Разбирает строку "data: {...}" в JObject (эмуляция логики LmStudioClient).
        /// </summary>
        private static Newtonsoft.Json.Linq.JObject ParseSseLine(string line)
        {
            if (!line.StartsWith("data:")) return null;
            var json = line.Substring(5).Trim();
            if (json == "[DONE]") return null;
            return Newtonsoft.Json.Linq.JObject.Parse(json);
        }

        [Fact]
        public void ParseSseLine_ContentDelta_ExtractsContent()
        {
            var line = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}";
            var chunk = ParseSseLine(line);

            var content = chunk["choices"]?[0]?["delta"]?["content"]?.ToString();
            Assert.Equal("Hello", content);
        }

        [Fact]
        public void ParseSseLine_ToolCallDelta_ExtractsToolCall()
        {
            var line = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\"}}]},\"finish_reason\":null}]}";
            var chunk = ParseSseLine(line);

            var toolCall = chunk["choices"]?[0]?["delta"]?["tool_calls"]?[0];
            Assert.NotNull(toolCall);
            Assert.Equal("call_1", toolCall["id"]?.ToString());
            Assert.Equal("read_file", toolCall["function"]?["name"]?.ToString());
        }

        [Fact]
        public void ParseSseLine_FinishReasonStop_Extracts()
        {
            var line = "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}";
            var chunk = ParseSseLine(line);

            var finishReason = chunk["choices"]?[0]?["finish_reason"]?.ToString();
            Assert.Equal("stop", finishReason);
        }

        [Fact]
        public void ParseSseLine_DoneMarker_ReturnsNull()
        {
            var line = "data: [DONE]";
            var chunk = ParseSseLine(line);
            Assert.Null(chunk);
        }

        [Fact]
        public void ParseSseLine_EmptyOrComment_ReturnsNull()
        {
            Assert.Null(ParseSseLine(""));
            Assert.Null(ParseSseLine(": keep-alive"));
            Assert.Null(ParseSseLine("event: message"));
        }
    }
}