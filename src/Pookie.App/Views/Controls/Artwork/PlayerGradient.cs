using Aprillz.MewUI;
using System.Numerics;

namespace Pookie.App;

// Preserve fractional colour channels throughout a transition, including when
// another track interrupts it. Convert to bytes only when producing pixels.
internal readonly record struct PlayerGradient(Vector3 Start, Vector3 Middle, Vector3 End)
{
    internal static PlayerGradient FromPalette(PlayerPalette palette)
    {
        static Vector3 Channels(Color color) => new(color.R, color.G, color.B);
        return new(Channels(palette.Start), Channels(palette.Middle), Channels(palette.End));
    }

    internal PlayerGradient Lerp(PlayerGradient other, double amount) =>
        new(Vector3.Lerp(Start, other.Start, (float)amount), Vector3.Lerp(Middle, other.Middle, (float)amount),
            Vector3.Lerp(End, other.End, (float)amount));

    internal Vector3 Sample(double position)
    {
        position = Math.Clamp(position, 0, 1);
        var amount = position < .38 ? position / .38 : (position - .38) / .62;
        amount = amount * amount * (3 - 2 * amount);
        return Vector3.Lerp(position < .38 ? Start : Middle, position < .38 ? Middle : End, (float)amount);
    }

    internal PlayerPalette ToPalette()
    {
        static Color ColorOf(Vector3 color) => Color.FromRgb((byte)Math.Round(color.X), (byte)Math.Round(color.Y), (byte)Math.Round(color.Z));
        return new(ColorOf(Start), ColorOf(Middle), ColorOf(End));
    }

    internal static double Noise(int x, int y)
    {
        var hash = unchecked((uint)x * 0x9e3779b9u + (uint)y * 0x85ebca6bu);
        hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du);
        hash ^= hash >> 15; hash = unchecked(hash * 0x846ca68bu); hash ^= hash >> 16;
        return (hash & 0xffffff) / 16777216d;
    }
}
