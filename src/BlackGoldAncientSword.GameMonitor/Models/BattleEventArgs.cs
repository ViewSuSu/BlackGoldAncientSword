namespace BlackGoldAncientSword.GameMonitor.Models
{
    public class BattleEventArgs : EventArgs
    {
        public string BattleId { get; init; } = string.Empty;
        public string MapId { get; init; } = string.Empty;
        public string RoomId { get; init; } = string.Empty;
        public string RoomType { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; }
    }
}
