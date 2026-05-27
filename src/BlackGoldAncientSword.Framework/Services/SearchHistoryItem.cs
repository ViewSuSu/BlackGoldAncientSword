namespace BlackGoldAncientSword.Framework.Services
{
    public class SearchHistoryItem
    {
        public string Query { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}