using System.Text.Json;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 验证 JsonFlexibleStringConverter 注册到 NarakaApiClient.JsonOptions 后，
    /// 能正确处理后端 string 字段返回的数字 / 字符串混合形态。
    /// unified 接口中 season.metrics[].value 声明为 string?，后端实测里既有
    /// "42"（string）也可能回 42（int）或 4.8（double），该 converter 保证全部解析为 string。
    /// </summary>
    public class JsonFlexibleStringConverterTests
    {
        [Fact]
        public void MetricValue_Integer_Should_Deserialize_To_String()
        {
            var json = "{\"code\":\"round\",\"label\":\"对局数\",\"value\":247,\"unit\":\"\"}";
            var entry = JsonSerializer.Deserialize<Metric>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("247", entry!.Value);
        }

        [Fact]
        public void MetricValue_Double_Should_Deserialize_To_String()
        {
            var json = "{\"code\":\"kd\",\"label\":\"K/D\",\"value\":1.51,\"unit\":\"\"}";
            var entry = JsonSerializer.Deserialize<Metric>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("1.51", entry!.Value);
        }

        [Fact]
        public void MetricValue_String_Should_Stay_String()
        {
            var json = "{\"code\":\"win_rate\",\"label\":\"第一率\",\"value\":\"4.9%\",\"unit\":\"\"}";
            var entry = JsonSerializer.Deserialize<Metric>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("4.9%", entry!.Value);
        }

        [Fact]
        public void NarakaApiClient_JsonOptions_Should_Register_FlexibleStringConverter()
        {
            var hasConverter = false;
            foreach (var c in NarakaApiClient.JsonOptions.Converters)
            {
                if (c is JsonFlexibleStringConverter) { hasConverter = true; break; }
            }
            Assert.True(hasConverter, "NarakaApiClient.JsonOptions 应注册 JsonFlexibleStringConverter");
        }

        /// <summary>
        /// 嵌套场景：完整 season 响应解析（含 List&lt;Metric&gt;），
        /// metrics 数组里 int / double / string 三种 value token 都能正确反序列化为 string。
        /// </summary>
        [Fact]
        public void GetSeasonSummaryResponse_Real_Sample_Should_Deserialize_Nested_Metrics()
        {
            var json = "{\"code\":0,\"msg\":\"\",\"data\":{"
                + "\"seasonCode\":\"S1\","
                + "\"rank\":{\"name\":\"无双修罗\",\"iconUrl\":\"\",\"score\":5299,\"level\":\"Ⅱ\"},"
                + "\"metrics\":["
                + "{\"code\":\"round\",\"label\":\"对局数\",\"value\":247,\"unit\":\"\"},"
                + "{\"code\":\"kd\",\"label\":\"K/D\",\"value\":1.51,\"unit\":\"\"},"
                + "{\"code\":\"win_rate\",\"label\":\"第一率\",\"value\":\"4.9%\",\"unit\":\"\"}"
                + "]}}";
            var response = JsonSerializer.Deserialize<GetSeasonSummaryResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.Equal(0, response!.Code);
            Assert.NotNull(response.Data);
            Assert.Equal(5299, response.Data!.Rank!.Score);
            Assert.NotNull(response.Data.Metrics);
            Assert.Equal(3, response.Data.Metrics!.Count);
            Assert.Equal("247", response.Data.Metrics[0].Value);
            Assert.Equal("1.51", response.Data.Metrics[1].Value);
            Assert.Equal("4.9%", response.Data.Metrics[2].Value);
        }

        /// <summary>
        /// NumberHandling.AllowReadingFromString 防御：后端把 code 写成字符串 "200"，
        /// DTO 是 double?，应能正确解析为 200。
        /// </summary>
        [Fact]
        public void JsonOptions_Should_Accept_Number_From_String_Token()
        {
            var json = "{\"code\":\"200\",\"msg\":\"ok\",\"data\":null}";
            var response = JsonSerializer.Deserialize<GetSeasonSummaryResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Code);
        }
    }
}
