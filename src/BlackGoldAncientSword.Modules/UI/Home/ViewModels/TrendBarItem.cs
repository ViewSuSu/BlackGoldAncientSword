using System;
using System.Windows;

namespace BlackGoldAncientSword.Modules.UI.Home.ViewModels
{

    public sealed class TrendBarItem
    {

        public double Ratio { get; init; }

        public GridLength SpacerWeight => new(Math.Max(1 - Ratio, 0), GridUnitType.Star);

        public GridLength BarWeight => new(Ratio, GridUnitType.Star);

        public string ValueText { get; init; } = string.Empty;

        public string DateText { get; init; } = string.Empty;

        public string Tooltip { get; init; } = string.Empty;
    }
}
