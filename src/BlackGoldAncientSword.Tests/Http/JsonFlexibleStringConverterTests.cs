using System.Text.Json;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{
    /// <summary>
    /// 验证 JsonFlexibleStringConverter 注册到 NarakaApiClient.JsonOptions 后，
    /// 能正确处理后端 stats[].value 字段返回的数字 / 字符串混合形态。
    /// 这是 Newtonsoft → STJ 迁移的关键回归测试：后端实测响应里 value 既有
    /// 247（int）也有 "4.9%"（string）也有 1.51（double），DTO 全部声明为 string?。
    /// </summary>
    public class JsonFlexibleStringConverterTests
    {
        [Fact]
        public void StatEntry_Value_Integer_Should_Deserialize_To_String()
        {
            var json = "{\"name\":\"对局数\",\"key\":\"round\",\"value\":247}";
            var entry = JsonSerializer.Deserialize<StatEntry>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("247", entry!.Value);
        }

        [Fact]
        public void StatEntry_Value_Double_Should_Deserialize_To_String()
        {
            var json = "{\"name\":\"K/D\",\"key\":\"kd\",\"value\":1.51}";
            var entry = JsonSerializer.Deserialize<StatEntry>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("1.51", entry!.Value);
        }

        [Fact]
        public void StatEntry_Value_String_Should_Stay_String()
        {
            var json = "{\"name\":\"第一率\",\"key\":\"win_rate\",\"value\":\"4.9%\"}";
            var entry = JsonSerializer.Deserialize<StatEntry>(json, NarakaApiClient.JsonOptions);
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
        /// 验证嵌套场景：完整 PlayerStatsResponse 解析（含 List&lt;StatEntry&gt; 嵌套），
        /// stats 数组里 int / double / string 三种 value token 都能正确反序列化为 string。
        /// 这是用户截图 "数据全 0" 报告的真实链路回归。
        /// </summary>
        [Fact]
        public void GetPlayerStatsResponse_Real_Sample_Should_Deserialize_Nested_Stats()
        {
            var json = "{\"code\":200,\"msg\":\"ok\",\"data\":{\"grade\":{\"gradeId\":5020062,\"gradeName\":\"无双修罗\",\"gradeScore\":5299},\"dragonKill\":0,\"stats\":["
                + "{\"name\":\"对局数\",\"key\":\"round\",\"value\":247},"
                + "{\"name\":\"K/D\",\"key\":\"kd\",\"value\":1.51},"
                + "{\"name\":\"第一率\",\"key\":\"win_rate\",\"value\":\"4.9%\"},"
                + "{\"name\":\"场均生存\",\"key\":\"avg_total_live_time\",\"value\":\"8'19\\\"\"}"
                + "]}}";
            var response = JsonSerializer.Deserialize<GetPlayerStatsResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Code);
            Assert.NotNull(response.Data);
            Assert.Equal(5299, response.Data!.Grade!.GradeScore);
            Assert.NotNull(response.Data.Stats);
            Assert.Equal(4, response.Data.Stats!.Count);
            Assert.Equal("247", response.Data.Stats[0].Value);   // int → "247"
            Assert.Equal("1.51", response.Data.Stats[1].Value);  // double → "1.51"
            Assert.Equal("4.9%", response.Data.Stats[2].Value);  // string → "4.9%"
            Assert.Equal("8'19\"", response.Data.Stats[3].Value); // string with escape
        }

        /// <summary>
        /// 验证 NumberHandling.AllowReadingFromString 防御行为：
        /// 假设后端某天把 code 字段写成字符串 "200"，DTO 是 double?，应能正确解析为 200。
        /// </summary>
        [Fact]
        public void JsonOptions_Should_Accept_Number_From_String_Token()
        {
            var json = "{\"code\":\"200\",\"msg\":\"ok\",\"data\":null}";
            var response = JsonSerializer.Deserialize<GetPlayerStatsResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.Equal(200, response!.Code);
        }
    }
}
