namespace NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions
{
    public interface IPlayerPrefsService
    {
        PlayerPrefsData Current { get; }
        void Load();
    }
}
