using System.Reflection;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 提供当前客户端（App 程序集）版本号，供 UA / 排查头等场景复用。
    /// <para>
    /// 版本号权威来源是 App 层程序集的 <see cref="AssemblyInformationalVersionAttribute"/>。
    /// Framework 无法反向引用 App，故由 App 启动时调用 <see cref="Initialize"/> 注入其程序集；
    /// 未初始化时回退到当前 Framework 程序集，避免版本头为空。
    /// </para>
    /// </summary>
    public static class ClientVersionProvider
    {
        private static string? _version;

        /// <summary>形如 <c>1.0.0.22</c>。App 启动时应先调用 <see cref="Initialize"/> 注入 App 程序集。</summary>
        public static string Version => _version ??= Read(typeof(ClientVersionProvider).Assembly);

        /// <summary>由 App 启动时注入 App 程序集，确定版本号的权威来源。</summary>
        public static void Initialize(Assembly appAssembly) => _version = Read(appAssembly);

        private static string Read(Assembly assembly)
        {
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plusIndex = informational.IndexOf('+');
                return plusIndex > 0 ? informational[..plusIndex] : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
