using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.GameMonitor;

public class GameLogMonitorTests
{
    private const string NarakaLogDir = @"C:\Users\Administrator\AppData\LocalLow\24Entertainment\Naraka";
    private const string LocalUserUuid = @"l77c000015949400120163";

    private readonly ITestOutputHelper _output;

    public GameLogMonitorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 从 Player.log 的回放记录 JSON 中提取所有对局的 players 列表，
    /// 标明本地用户 UUID，输出到 test output。
    /// </summary>
    [Fact]
    public void ExtractPlayersFromReplayRecords_PlayerLog()
    {
        var logPath = Path.Combine(NarakaLogDir, "Player.log");
        Assert.True(File.Exists(logPath), $"日志文件不存在: {logPath}");

        // 使用 ReadWrite 共享模式读取被占用的日志文件
        string content;
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
        {
            content = reader.ReadToEnd();
        }

        // 定位"录像列表下载成功"所在行，提取 JSON 部分
        var jsonStart = content.IndexOf("录像列表下载成功", StringComparison.Ordinal);
        Assert.True(jsonStart >= 0, "未找到回放记录行");

        var afterLabel = content[jsonStart..];
        var braceIdx = afterLabel.IndexOf('{');
        Assert.True(braceIdx >= 0, "未找到 JSON 起始");

        // 从 {"code":0... 提取完整 JSON
        var jsonPart = afterLabel[braceIdx..];
        var lastCloseBrace = jsonPart.LastIndexOf('}');
        if (lastCloseBrace < 0) lastCloseBrace = jsonPart.Length - 1;
        var json = jsonPart[..(lastCloseBrace + 1)];

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("code").GetInt32());

        var list = root.GetProperty("data").GetProperty("list");
        var matchCount = list.GetArrayLength();

        // 去重玩家总数
        var allUuids = new HashSet<string>();
        var matchStats = new List<MatchRecord>();

        foreach (var item in list.EnumerateArray())
        {
            var roomId = item.GetProperty("room_id").GetString()!;
            var payload = item.GetProperty("payload");
            var battleId = payload.GetProperty("battle_id").GetInt32();
            var mapId = payload.GetProperty("map_id").GetInt32();
            var totalTeam = payload.GetProperty("total_team").GetInt32();

            var players = new List<string>();
            foreach (var p in payload.GetProperty("players").EnumerateArray())
            {
                var uuid = p.GetProperty("uuid").GetString()!;
                players.Add(uuid);
                allUuids.Add(uuid);
            }

            matchStats.Add(new MatchRecord(roomId, battleId, mapId, totalTeam, players));
        }

        // 输出统计
        _output.WriteLine("=== 回放记录统计 ===");
        _output.WriteLine($"对局总数: {matchCount}");
        _output.WriteLine($"去重玩家总数: {allUuids.Count}");
        _output.WriteLine($"本地用户 UUID: {LocalUserUuid}");
        _output.WriteLine("");

        for (var i = 0; i < matchStats.Count; i++)
        {
            var m = matchStats[i];
            _output.WriteLine($"--- 对局 #{i + 1} ---");
            _output.WriteLine($"  room_id    : {m.RoomId}");
            _output.WriteLine($"  battle_id  : {m.BattleId}");
            _output.WriteLine($"  map_id     : {m.MapId}");
            _output.WriteLine($"  队伍数     : {m.TotalTeam}");
            _output.WriteLine($"  玩家数     : {m.Players.Count}");
            _output.WriteLine("  玩家列表:");

            for (var j = 0; j < m.Players.Count; j++)
            {
                var marker = m.Players[j] == LocalUserUuid ? " <== 本地用户" : "";
                _output.WriteLine($"    [{j + 1,2}] {m.Players[j]}{marker}");
            }
            _output.WriteLine("");
        }
    }

    private sealed record MatchRecord(string RoomId, int BattleId, int MapId, int TotalTeam, List<string> Players);

    [Fact]
    public void BattleEventArgs_DefaultValues_AreEmpty()
    {
        var args = new BattleEventArgs();

        Assert.Equal(string.Empty, args.BattleId);
        Assert.Equal(string.Empty, args.MapId);
        Assert.Equal(string.Empty, args.RoomId);
        Assert.Equal(string.Empty, args.RoomType);
        Assert.Equal(default, args.Timestamp);
    }

    [Fact]
    public void BattleEventArgs_WithValues_StoresCorrectly()
    {
        var timestamp = DateTimeOffset.Now;
        var args = new BattleEventArgs
        {
            BattleId = "battle_001",
            MapId = "map_42",
            RoomId = "room_abc",
            RoomType = "1",
            Timestamp = timestamp,
        };

        Assert.Equal("battle_001", args.BattleId);
        Assert.Equal("map_42", args.MapId);
        Assert.Equal("room_abc", args.RoomId);
        Assert.Equal("1", args.RoomType);
        Assert.Equal(timestamp, args.Timestamp);
    }

    [Fact]
    public void PlayerPrefsData_DefaultValues_AreSane()
    {
        var data = new PlayerPrefsData();

        Assert.Equal(string.Empty, data.PlayerName);
        Assert.Equal(string.Empty, data.PlayerId);
        Assert.Equal(0, data.PlayerLevel);
        Assert.Equal(string.Empty, data.ServerId);
        Assert.Equal(0, data.MaxMember);
        Assert.False(data.IsLoaded);
    }

    [Fact]
    public void IGameLogMonitor_Mock_EventsFire()
    {
        var mock = new Mock<IGameLogMonitor>();
        BattleEventArgs? receivedArgs = null;

        mock.Object.BattleStarted += (_, args) => receivedArgs = args;

        mock.Raise(m => m.BattleStarted += null,
            new BattleEventArgs { BattleId = "test_123" });

        Assert.NotNull(receivedArgs);
        Assert.Equal("test_123", receivedArgs!.BattleId);
    }

    [Fact]
    public void IGameLogMonitor_Mock_IsInBattle()
    {
        var mock = new Mock<IGameLogMonitor>();
        mock.Setup(m => m.IsInBattle).Returns(true);
        mock.Setup(m => m.CurrentBattleId).Returns("battle_active");

        Assert.True(mock.Object.IsInBattle);
        Assert.Equal("battle_active", mock.Object.CurrentBattleId);
    }
}
