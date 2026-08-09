namespace BlackGoldAncientSword.GameMonitor.Services.Abstractions;

/// <summary>
/// CCMini 语音日志监控器。游戏启动时会新建 <c>ccmini\ccmini_new\logs\m*.log</c>，
/// 其中 <c>set-uid-vol</c> 记录（队伍语音为每个队友设置音量）会携带队友角色 UID，
/// 且英雄选择阶段（进入对局后约 1 秒）即实时落盘。
/// <para>
/// 本接口监听该日志并持续跟踪"最近活跃"的队友 UID 集合：队友在英雄选择阶段退出/换人时，
/// 集合会变化并再次触发 <see cref="TeammatesReady"/>（对局中打开软件时也能回放当前日志拿到在局队友）。
/// </para>
/// </summary>
public interface ICcMiniTeammateMonitor : IDisposable
{
    /// <summary>
    /// 队友 UID 集合变化（首次识别 / 新队友加入 / 队友换人）时触发。
    /// <see cref="CcMiniTeammatesEventArgs.TeammateUids"/> 为当前最近活跃的队友 UID（不含本地用户）。
    /// </summary>
    event EventHandler<CcMiniTeammatesEventArgs>? TeammatesReady;

    /// <summary>最近一次识别到的队友 UID 列表（最近活跃，含已去重），尚未识别时为空。</summary>
    IReadOnlyList<string> TeammateUids { get; }

    /// <summary>是否已至少触发过一次队友名单。</summary>
    bool HasRecognized { get; }

    /// <summary>开始监控。进程未运行或日志目录不存在时静默返回（调用方按需重试）。</summary>
    void Start();

    /// <summary>停止监控并释放资源。</summary>
    void Stop();

    /// <summary>重置识别状态（清空已识别 UID 与触发快照），用于进入新对局 / 重新回放当前日志。</summary>
    /// <param name="matchStartTime">本局英雄选择开始时间；非空时只接受不早于该时间的 set-uid-vol（跨局复用 m*.log 时丢弃上一局旧记录）。</param>
    void Reset(DateTime? matchStartTime = null);
}
