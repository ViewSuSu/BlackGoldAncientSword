using NarakaBladepoint.StatsAssistant.Framework.Http.Generated;
using NarakaBladepoint.StatsAssistant.Modules.UI.Stats.ViewModels;

namespace NarakaBladepoint.StatsAssistant.Modules.Mappings
{
    public class BattleMappingRegister : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<HonorTitleInfo, HonorTitleDisplayItem>()
                .Map(dest => dest.Icon, src => src.HonorIcon)
                .Map(dest => dest.Desc, src => src.HonorDesc)
                .Map(dest => dest.Name, src => src.HonorName);
        }
    }
}
