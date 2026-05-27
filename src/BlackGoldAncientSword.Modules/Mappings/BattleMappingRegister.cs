using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Modules.UI.Stats.ViewModels;

namespace BlackGoldAncientSword.Modules.Mappings
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
