using Xunit;

namespace Pookie.Audio.Tests;

public sealed class FixedTrackGainTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(0)]
    [InlineData(6)]
    public void CorrectionAppliesToTheFirstFrameAndDoesNotFollowLaterAudio(double db)
    {
        var gain = new FixedTrackGain(48000, 2, db);
        var factor = Math.Pow(10, db / 20);
        foreach (var amplitude in new[] { .03f, .3f, 0f, .04f, .4f })
        {
            var pcm = Enumerable.Repeat(amplitude, 48000 * 2).ToArray();
            gain.Apply(pcm);
            Assert.Equal(amplitude * factor, pcm[0], 5);
            Assert.Equal(amplitude * factor, pcm[^1], 5);
            Assert.Equal(db, gain.GainDb);
        }
        var afterSeek = new FixedTrackGain(48000, 2, db);
        var resumed = new float[] { .1f, .1f };
        afterSeek.Apply(resumed);
        Assert.Equal(.1 * factor, resumed[0], 5);
    }

    [Fact]
    public void BoostedPeaksAreLinkedAndInvalidPcmDoesNotReachTheDevice()
    {
        var gain = new FixedTrackGain(48000, 2, 6);
        var samples = new float[256];
        for (var i=0; i<samples.Length; i+=2) {samples[i]=.9f; samples[i+1]=.45f;}
        samples[0]=float.NaN; samples[1]=float.PositiveInfinity;
        gain.Apply(samples);
        Assert.All(samples, x=>Assert.True(float.IsFinite(x) && Math.Abs(x)<=.842));
        for(var i=2;i<samples.Length;i+=2) Assert.Equal(samples[i]/2, samples[i+1], 6);
        Assert.Equal(6, gain.GainDb);
    }

    [Theory]
    [InlineData(1, .1)]
    [InlineData(2, 1)]
    [InlineData(6, 1)]
    public async Task RealDecoderAppliesTheSuppliedCorrectionWithoutAnalyzingTheTrack(int channels, double seconds)
    {
        var file = Path.Combine(Path.GetTempPath(), $"pookie-gain-{Guid.NewGuid():N}.wav");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var frames = (int)(48000 * seconds);
            using (var writer = new BinaryWriter(File.Create(file)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + frames * channels * 2); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(48000);
                writer.Write(48000 * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(frames * channels * 2);
                for(var i=0;i<frames;i++) for(var ch=0;ch<channels;ch++)
                    writer.Write((short)(short.MaxValue * .08 * Math.Sin(i*Math.PI*2000/48000)));
            }
            using var http = new HttpClient();
            var source = new AudioSource(file, AudioTransport.File) {NormalizationGainDb=-6};
            using var provider = new StreamingProvider(http, source, 0, _=>false, timeout.Token);
            await provider.Ready.WaitAsync(timeout.Token);
            var buffer = new float[128 * channels];
            double energy=0; long count=0;
            while(!provider.Ended)
            {
                var before=provider.Position; provider.ReadBytes(buffer);
                var read=provider.Position-before;
                for(var i=0;i<read;i++) energy+=(double)buffer[i]*buffer[i];
                count+=read;
                if(read==0 && !provider.Ended) await provider.WaitForDataAsync(timeout.Token);
            }
            await provider.Completion.WaitAsync(timeout.Token);
            Assert.Null(provider.Error);
            Assert.Equal(-6, provider.NormalizationGainDb);
            Assert.InRange(Math.Sqrt(energy/count), .0282, .0285);
            await using var player = new SoundFlowPlayer(silent:true);
            await player.PlayAsync(source, timeout.Token);
            Assert.Equal(-6, player.Poll().NormalizationGainDb);
            await player.SeekAsync(seconds/2, timeout.Token);
            Assert.Equal(-6, player.Poll().NormalizationGainDb);
        }
        finally {File.Delete(file);}
    }
}
