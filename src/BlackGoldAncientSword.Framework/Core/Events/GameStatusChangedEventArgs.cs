namespace BlackGoldAncientSword.GameMonitor.Models
{
    public class GameStatusChangedEventArgs : EventArgs
    {
        public GameStatus Status { get; init; }
        public DateTimeOffset Timestamp { get; init; }

        public GameStatusChangedEventArgs(GameStatus status)
        {
            Status = status;
            Timestamp = DateTimeOffset.Now;
        }
    }
}