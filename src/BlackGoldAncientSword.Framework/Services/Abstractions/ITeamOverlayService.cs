using BlackGoldAncientSword.Framework.UI.Controls;
using System;
using System.Collections.Generic;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface ITeamOverlayService
    {
        void Show(IList<TeamOverlayMemberItem> members);
        void Hide();

        Action? RefreshAction { get; set; }
        event Action? Dismissed;
        event Action? NavigateToTeamInfoRequested;
    }
}
