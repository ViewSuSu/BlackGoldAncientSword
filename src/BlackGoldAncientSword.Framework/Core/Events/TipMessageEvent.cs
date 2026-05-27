namespace BlackGoldAncientSword.Framework.Core.Events
{
    public class TipMessageWithHighlightArgs
    {
        public string Message { get; set; }
        public List<string> HighlightTexts { get; set; }

        public TipMessageWithHighlightArgs(string message, List<string>? highlightTexts = null)
        {
            Message = message;
            HighlightTexts = highlightTexts ?? new List<string>();
        }
    }

    public class TipMessageEvent : PubSubEvent<TipMessageWithHighlightArgs> { }
}
