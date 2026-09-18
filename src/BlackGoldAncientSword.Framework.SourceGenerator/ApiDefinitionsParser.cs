using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BlackGoldAncientSword.Framework.SourceGenerator
{

    internal static class ApiDefinitionsParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static ApiDefinitionsRoot? Parse(string json)
        {
            return JsonSerializer.Deserialize<ApiDefinitionsRoot>(json, JsonOptions);
        }

        public static string ResolveType(string type) => type switch
        {
            "string" => "string",
            "int" => "double",
            "long" => "long",
            "float" => "float",
            "double" => "double",
            "bool" => "bool",
            "decimal" => "decimal",
            "DateTime" => "System.DateTime",
            "Guid" => "System.Guid",
            _ => type
        };

        public static bool IsReferenceType(string type)
        {
            if (type == "string" || type.StartsWith("List<") || type.StartsWith("Dictionary<")) return true;
            if (type == "int" || type == "long" || type == "float" || type == "double" ||
                type == "bool" || type == "decimal" || type == "System.DateTime" ||
                type == "System.Guid" || type == "DateTime" || type == "Guid") return false;
            return true;
        }

        public static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            if (name.IndexOf('_') < 0)
                return char.ToUpperInvariant(name[0]) + name.Substring(1);

            var sb = new StringBuilder(name.Length);
            var upperNext = true;
            foreach (var ch in name)
            {
                if (ch == '_') { upperNext = true; continue; }
                sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                upperNext = false;
            }

            return sb.ToString();
        }

        public static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var pascal = ToPascalCase(name);
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        public static string GetParamDescription(string paramName) => paramName switch
        {
            "roleId" => "玩家角色ID（小程序 roleId）",
            "seasonId" => "赛季ID，通过 QuerySeasons 接口获取",
            "gameMode" => "游戏模式ID，见 GameMode 枚举定义",
            "battleId" => "对局ID，通过 GetRecentBattles 接口获取",
            "name" => "搜索关键词（玩家昵称或角色ID）",
            "game_type" => "游戏标识，永劫无间固定 yjwj",
            "q" => "搜索关键词（玩家昵称）",
            "role_id" => "玩家角色 UID（取自玩家搜索的 game_id）",
            "server" => "目标玩家所在服务器 id（取自玩家搜索的 ext，国服为 163）",
            "season" => "赛季 key（取自主页数据的 seasons[].key）",
            "battle_tid" => "模式 key（取自主页数据的 mode[].key）",
            "match_id" => "对局 id（取自对局列表项的 match_id）",
            "offset" => "分页偏移",
            "limit" => "单页条数",
            _ => $"参数 {paramName}"
        };
    }

    internal class ApiDefinitionsRoot
    {
        public string BaseUrl { get; set; } = string.Empty;
        public Dictionary<string, string> DefaultHeaders { get; set; } = new();
        public List<string> EnumTypeNames { get; set; } = new();
        public ApiEnvelopeDefinition Envelope { get; set; } = new();
        public List<ApiEndpointDefinition> Apis { get; set; } = new();
    }

    internal class ApiEnvelopeDefinition
    {
        public string SuccessProperty { get; set; } = "code";

        public List<string> SuccessValues { get; set; } = new() { "200", "0" };

        public string MessageProperty { get; set; } = "msg";

        public bool IsEnvelope(TypeDefinition type) =>
            type.Properties.ContainsKey(SuccessProperty) && type.Properties.ContainsKey(MessageProperty);
    }

    internal class ApiEndpointDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = string.Empty;
        public Dictionary<string, string> PathParameters { get; set; } = new();
        public Dictionary<string, string> QueryParameters { get; set; } = new();
        public Dictionary<string, string> Headers { get; set; } = new();
        public TypeDefinition? RequestBody { get; set; }
        public TypeDefinition? ResponseBody { get; set; }
    }

    internal class TypeDefinition
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, PropertyDefinition> Properties { get; set; } = new();
        public Dictionary<string, TypeDefinition> NestedTypes { get; set; } = new();
    }

    internal class PropertyDefinition
    {
        public string Type { get; set; } = "string";
        public bool? Nullable { get; set; }
        public string? JsonName { get; set; }
    }
}
