using BlackGoldAncientSword.Framework.UI.Controls;

using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ITeamOverlayService
    {
        void Show(IList<TeamOverlayMemberItem> members);
        void Hide();
    }
}

