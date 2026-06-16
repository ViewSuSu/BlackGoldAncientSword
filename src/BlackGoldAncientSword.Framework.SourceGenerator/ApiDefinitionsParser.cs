using System.Collections.Generic;
using System.Text.Json;

namespace BlackGoldAncientSword.Framework.SourceGenerator
{
    /// <summary>
    /// 共享的 api-definitions.json 解析与类型映射逻辑。
    /// 由 HttpApiSourceGenerator 与 HttpApiTestSourceGenerator 共用，保证 DTO/Client 与测试代码一致。
    /// </summary>
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

        /// <summary>
        /// 定义文件中的逻辑类型 → C# 类型字符串。
        /// 注意：项目约定 int 统一映射为 double（避免 JSON 数字精度边界问题）。
        /// </summary>
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
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        public static string GetParamDescription(string paramName) => paramName switch
        {
            "roleId" => "玩家角色ID（小程序 roleId）",
            "seasonId" => "赛季ID，通过 QuerySeasons 接口获取",
            "gameMode" => "游戏模式ID，见 GameMode 枚举定义",
            "battleId" => "对局ID，通过 GetRecentBattles 接口获取",
            "name" => "搜索关键词（玩家昵称或角色ID）",
            _ => $"参数 {paramName}"
        };
    }

    internal class ApiDefinitionsRoot
    {
        public string BaseUrl { get; set; } = string.Empty;
        public Dictionary<string, string> DefaultHeaders { get; set; } = new();
        public List<string> EnumTypeNames { get; set; } = new();
        public List<ApiEndpointDefinition> Apis { get; set; } = new();
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
