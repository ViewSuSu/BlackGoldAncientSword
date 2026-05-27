using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Events;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    internal class TipMessageService : ITipMessageService
    {
        private readonly IEventAggregator _eventAggregator;

        public TipMessageService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void Show(string message, TipMessageType type = TipMessageType.Info)
        {
            var highlightTexts = type == TipMessageType.Error
                ? new List<string> { "Error" }
                : new List<string> { "Info" };

            _eventAggregator.GetEvent<TipMessageEvent>()
                .Publish(new TipMessageWithHighlightArgs(message, highlightTexts));
        }

        public void ShowError(string message) => Show(message, TipMessageType.Error);
        public void ShowInfo(string message) => Show(message, TipMessageType.Info);
    }
}