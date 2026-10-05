namespace Pookie.App.Diagnostics;

internal static class DemoAudio
{
    public static string Create(double frequency = 440, int seconds = 12)
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        const int rate = 22050;
        var length = seconds * rate;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(length * 2);
        for (var i = 0; i < length; i++)
        {
            var fade = Math.Min(1, Math.Min(i, length - i) / (rate * .08));
            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 2200 * fade));
        }
        return path;
    }
}
