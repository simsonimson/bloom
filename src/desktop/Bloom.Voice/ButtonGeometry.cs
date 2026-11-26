using Bloom.Core.Voice;

namespace Bloom.Voice;

/// <summary>Conversions for the calibrated Roblox microphone button position.</summary>
public static class ButtonGeometry
{
    /// <summary>Client point (physical pixels) → stored position (96-dpi units). Null if outside the client area.</summary>
    public static ButtonPosition? FromClient(int x, int y, int clientWidth, int clientHeight, double scale)
    {
        if (scale <= 0 || x < 0 || y < 0 || x >= clientWidth || y >= clientHeight) return null;
        return new ButtonPosition(Math.Round(x / scale, 1), Math.Round(y / scale, 1));
    }

    /// <summary>Stored position → client point for the current DPI. Null if it would fall outside the window.</summary>
    public static (int X, int Y)? ToClient(ButtonPosition position, int clientWidth, int clientHeight, double scale)
    {
        var x = (int)Math.Round(position.X * scale);
        var y = (int)Math.Round(position.Y * scale);
        return x < 0 || y < 0 || x >= clientWidth || y >= clientHeight ? null : (x, y);
    }
}
