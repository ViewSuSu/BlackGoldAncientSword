using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackGoldAncientSword.Framework.Http
{
    /// <summary>
    /// 兼容 Newtonsoft.Json 历史行为：把后端返回的 number / bool 自动转字符串映射到 string 属性。
    /// 例如 stats[].value 字段后端既可能返回 247（int）也可能返回 "4.9%"（string），
    /// 而 DTO 全部声明为 string?，需要本 converter 把非字符串 token 容错为字符串，
    /// 否则 STJ 严格匹配下会抛 JsonException 让整个响应解析失败（迁移前 Newtonsoft 默认有此容错）。
    /// </summary>
    public sealed class JsonFlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Cannot convert token {reader.TokenType} to string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
