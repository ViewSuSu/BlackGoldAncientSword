using System.Threading.Tasks;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions
{
    public interface ISettingsService
    {
        AppSettings Current { get; }
        void Load();
        Task SaveAsync();
    }
}