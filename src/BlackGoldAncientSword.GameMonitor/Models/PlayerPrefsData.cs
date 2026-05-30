namespace BlackGoldAncientSword.GameMonitor.Models
{
    public class PlayerPrefsData
    {
        public string PlayerName { get; set; } = string.Empty;
        public string OriginalPlayerName { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public int PlayerLevel { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public int MaxMember { get; set; }
        public bool IsLoaded { get; set; }
    }
}
