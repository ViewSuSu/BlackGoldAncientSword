using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using Moq;

namespace BlackGoldAncientSword.Tests.GameMonitor;

public class GameLogMonitorTests
{
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
