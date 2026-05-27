namespace BlackGoldAncientSword.ScreenCapture;

/// <summary>
/// 屏幕四象限标志。将窗口分为水平中心线和垂直中心线交叉的四个区域。
/// 支持组合（如 BottomLeft | BottomRight 表示下半部分）。
/// </summary>
[Flags]
public enum ScreenQuadrant
{
    None = 0,
    /// <summary>左上角</summary>
    TopLeft = 1,
    /// <summary>右上角</summary>
    TopRight = 2,
    /// <summary>左下角</summary>
    BottomLeft = 4,
    /// <summary>右下角</summary>
    BottomRight = 8,

    /// <summary>上半部分（TopLeft | TopRight）</summary>
    TopHalf = TopLeft | TopRight,
    /// <summary>下半部分（BottomLeft | BottomRight）</summary>
    BottomHalf = BottomLeft | BottomRight,
    /// <summary>左半部分（TopLeft | BottomLeft）</summary>
    LeftHalf = TopLeft | BottomLeft,
    /// <summary>右半部分（TopRight | BottomRight）</summary>
    RightHalf = TopRight | BottomRight,
    /// <summary>整个窗口</summary>
    Full = TopLeft | TopRight | BottomLeft | BottomRight,
}
