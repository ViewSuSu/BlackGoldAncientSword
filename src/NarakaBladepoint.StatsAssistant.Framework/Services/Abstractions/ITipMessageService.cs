namespace NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions
{
    public enum TipMessageType
    {
        Info,
        Error
    }

    public interface ITipMessageService
    {
        void Show(string message, TipMessageType type = TipMessageType.Info);
        void ShowError(string message);
        void ShowInfo(string message);
    }
}
