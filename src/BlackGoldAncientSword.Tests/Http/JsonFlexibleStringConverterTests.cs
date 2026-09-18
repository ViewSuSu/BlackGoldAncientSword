using System.Text.Json;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http
{

    public class JsonFlexibleStringConverterTests
    {
        [Fact]
        public void OverviewValue_Integer_Should_Deserialize_To_String()
        {
            var json = "{\"desc\":\"总场次\",\"value\":247}";
            var entry = JsonSerializer.Deserialize<HeyboxOverviewEntry>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("247", entry!.Value);
        }

        [Fact]
        public void OverviewValue_Double_Should_Deserialize_To_String()
        {
            var json = "{\"desc\":\"KD\",\"value\":1.51}";
            var entry = JsonSerializer.Deserialize<HeyboxOverviewEntry>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("1.51", entry!.Value);
        }

        [Fact]
        public void OverviewValue_String_Should_Stay_String()
        {
            var json = "{\"desc\":\"夺冠率\",\"value\":\"4.9%\"}";
            var entry = JsonSerializer.Deserialize<HeyboxOverviewEntry>(json, NarakaApiClient.JsonOptions);
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

        [Fact]
        public void PlayerHomeResponse_Real_Sample_Should_Deserialize_Nested_Overview()
        {
            var json = "{\"status\":\"ok\",\"msg\":\"\",\"result\":{"
                + "\"role_id\":\"uipe000001677200140163\",\"server\":\"163\","
                + "\"player_info\":{\"name\":\"小窗\",\"avatar\":\"https://img/a.png\",\"lv\":25,\"level\":\"无双修罗\",\"rating\":\"None\"},"
                + "\"overview\":["
                + "{\"desc\":\"总场次\",\"value\":247},"
                + "{\"desc\":\"KD\",\"value\":1.51},"
                + "{\"desc\":\"夺冠率\",\"value\":\"4.9%\"}"
                + "]}}";
            var response = JsonSerializer.Deserialize<HeyboxHomeResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.True(response!.IsSuccess);
            Assert.NotNull(response.Result);

            var info = response.Result!.PlayerInfo;
            Assert.NotNull(info);
            Assert.Equal("小窗", info!.Name);
            Assert.Equal("25", info.Lv);
            Assert.Equal("None", info.Rating);

            Assert.NotNull(response.Result.Overview);
            Assert.Equal(3, response.Result.Overview!.Count);
            Assert.Equal("247", response.Result.Overview[0].Value);
            Assert.Equal("1.51", response.Result.Overview[1].Value);
            Assert.Equal("4.9%", response.Result.Overview[2].Value);
        }

        [Theory]
        [InlineData("login")]
        [InlineData("relogin")]
        [InlineData("failed")]
        public void Envelope_NonOk_Status_Should_Not_Be_Success(string status)
        {
            var json = $"{{\"status\":\"{status}\",\"msg\":\"出错了\",\"result\":{{}}}}";
            var response = JsonSerializer.Deserialize<HeyboxHomeResponse>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(response);
            Assert.False(response!.IsSuccess);
            Assert.Equal("出错了", response.Msg);
        }

        [Fact]
        public void JsonOptions_Should_Accept_Number_From_String_Token()
        {
            var json = "{\"desc\":\"对局\",\"value\":\"248\"}";
            var entry = JsonSerializer.Deserialize<HeyboxOverviewEntry>(json, NarakaApiClient.JsonOptions);
            Assert.NotNull(entry);
            Assert.Equal("248", entry!.Value);
        }
    }
}
