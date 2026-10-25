using Pookie.App.Messages;
using Pookie.App.Storage;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatWorkspaceTests
{
    private static ChatMessage Message(string id,string text,long sender=90,int minute=0)=>new(id,sender,text,DateTimeOffset.Parse("2026-10-25T10:00:00Z").AddMinutes(minute));
    [Fact] public async Task HistoryRestoresSearchAndRangesWithoutCrossAccountData()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-history-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppDataPaths(path);
            await using(var history=new ChatHistoryStore(paths,42))
            {
                await history.SaveDialogsAsync([new("42:90",new(){Id=90,Username="Друг"},Message("3","новый"),true)]);
                await history.SavePageAsync(42,90,new([Message("3","новый",minute:3),Message("2","Привет музыка",minute:2)],"cursor-two"));
                var window=await history.LoadWindowAsync(90);Assert.False(window.Complete);Assert.Equal("cursor-two",window.Cursor);
                await history.SavePageAsync(42,90,new([Message("1","Привет мир",minute:1)],null),"cursor-two");
                Assert.True((await history.LoadWindowAsync(90)).Complete);
                Assert.Equal(2,(await history.SearchAsync("Привет")).Length);
                Assert.Single(await history.SearchAsync("муз"));
                Assert.Empty(await history.SearchAsync("Привет",91));
                Assert.Equal("Привет музыка",(await history.FindMessageAsync(90,"2"))?.Body);
                Assert.Null(await history.FindMessageAsync(91,"2"));
                Assert.Null(await history.FindMessageAsync(90,"missing"));
                await history.SavePageAsync(42,90,new([Message("2","Привет музыка",minute:2)],"cursor-two"));
                Assert.Equal(3,(await history.LoadWindowAsync(90)).Messages.Length);
            }
            await using(var restored=new ChatHistoryStore(paths,42))
            {Assert.Single(await restored.LoadDialogsAsync());Assert.Equal(3,(await restored.LoadWindowAsync(90)).Messages.Length);Assert.Equal("Привет мир",(await restored.FindMessageAsync(90,"1"))?.Body);}
            await using(var other=new ChatHistoryStore(paths,7)){Assert.Empty(await other.LoadDialogsAsync());Assert.Empty(await other.SearchAsync("Привет"));Assert.Null(await other.FindMessageAsync(90,"1"));}
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public async Task ClearingCacheKeepsFoldersFavoritesPositionsAndOutbox()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-durable-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppDataPaths(path);var workspace=new ChatWorkspaceStore(paths,42);
            workspace.Save(new([new("music","Музыка",[90,91])],[90],[new(90,Message("1","Сохранено"),DateTimeOffset.UtcNow)],new(){[90]=new("1",-12,false)},90));
            var local=new ChatLocalStore(paths,42);local.Save(new(new(){[90]="Черновик"},[new(90,"не подтверждено","Текст",DateTimeOffset.UtcNow,true)]));
            await using(var history=new ChatHistoryStore(paths,42)){await history.SavePageAsync(42,90,new([Message("1","Сохранено")],null));await history.ClearAsync();Assert.Empty(await history.SearchAsync("Сохранено"));}
            var result=workspace.Load();Assert.Single(result.Folders);Assert.Single(result.Favorites);Assert.Equal(-12,result.Positions[90].Y);Assert.Single(local.Load().Pending);Assert.Single(local.Load().Drafts);
            var cache=new ChatMediaDiskCache(paths,42,100);await cache.PutAsync("a",new byte[60],default);await cache.PutAsync("b",new byte[60],default);Assert.True(await cache.SizeAsync()<=100);await cache.ClearAsync();Assert.Single(workspace.Load().Favorites);
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public async Task FavoriteCiphertextSurvivesCacheEvictionAndClear()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-pinned-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppDataPaths(path);var cache=new ChatMediaDiskCache(paths,42,100);
            await cache.PutAsync("favorite",new byte[60],default);await cache.SetPinnedAsync(["favorite"]);
            await cache.PutAsync("old",new byte[60],default);await cache.PutAsync("new",new byte[60],default);
            Assert.True(await cache.SizeAsync()<=100);await cache.ClearAsync();
            Assert.Equal(60,(await cache.ReadAsync("favorite",default))!.Length);
            Assert.Empty(Directory.GetFiles(Path.Combine(paths.Cache,"Chats","42","Media")));
            Assert.Single(Directory.GetFiles(Path.Combine(paths.Root,"Chats","42","FavoriteMedia")));
            await cache.SetPinnedAsync([]);await cache.ClearAsync();Assert.Null(await cache.ReadAsync("favorite",default));
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public async Task ConfirmationCanBeFoundOutsideActiveWindowWithoutSendingAgain()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-confirm-"+Guid.NewGuid().ToString("N"));
        try
        {
            await using var history=new ChatHistoryStore(new(path),42);
            var message=Message("confirmed","Ответ подтвержден",42);
            await history.SavePageAsync(42,90,new([message],"gap"));
            var pending=new ChatPending(90,message.Body,"Ответ",message.SentAt,true,["previous"]);
            Assert.Equal("confirmed",(await history.FindConfirmationAsync(90,42,pending))?.Id);
            Assert.Null(await history.FindConfirmationAsync(91,42,pending));
            Assert.Null(await history.FindConfirmationAsync(90,42,pending with{BeforeIds=["confirmed"]}));
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public void ProtocolValidatesConversationAndKeepsUnknownWireReadable()
    {
        var wire=ChatProtocol.Reply(42,90,"target","Привет","Ответ");
        var projected=ChatProtocol.Project(Message("reply",wire,42),90,42);
        Assert.Equal("Ответ",projected.Text);Assert.Equal("target",projected.Reply!.MessageId);
        Assert.Null(ChatProtocol.Project(Message("reply",wire,42),42,91).Reply);
        Assert.Equal("a",ChatProtocol.Clip("a👨‍👩‍👧‍👦b",5));
        Assert.Equal(new string('a',159),ChatProtocol.Clip(new string('a',159)+"🔥",160));
        Assert.Equal("p2:broken",ChatProtocol.Project(Message("broken","p2:broken"),42,90).Text);
        Assert.Throws<ArgumentException>(()=>ChatProtocol.Reply(42,90,"target","",new string('ю',20000)));
        Assert.DoesNotContain("Ответ",new ChatEnvelope(2,"reply","x",42,"target","Ответ").ToString());
    }
    [Fact] public async Task ReactionProjectionIsIdempotentAndSurvivesUnloadedTargets()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-reaction-"+Guid.NewGuid().ToString("N"));
        try
        {
            var first=Message("a",ChatProtocol.React(42,90,"missing","❤️"),42,1);
            var second=Message("b",ChatProtocol.React(42,90,"missing","🔥"),42,2);
            var third=Message("c",ChatProtocol.React(42,90,"missing","👍"),90,3);
            Assert.Equal(2,ChatProtocol.Fold([first,first,second,third],42,90)["missing"].Count);
            await using var history=new ChatHistoryStore(new(path),42);
            await history.SavePageAsync(42,90,new([second,first,first,third],null));
            Assert.Empty(await history.SearchAsync("reaction"));
            var authors=(await history.ReactionsAsync(90))["missing"];Assert.Equal("🔥",authors[42]);Assert.Equal("👍",authors[90]);
            await history.SavePageAsync(42,90,new([first with{Id="duplicate",SentAt=first.SentAt.AddMinutes(8)}],null));
            Assert.Equal("🔥",(await history.ReactionsAsync(90))["missing"][42]);
            await history.SavePageAsync(42,90,new([Message("d",ChatProtocol.React(42,90,"missing",null),42,4)],null));
            Assert.Single((await history.ReactionsAsync(90))["missing"]);
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public async Task LargeHistoryEvictsOldRowsAndReadsBoundedWindowsInBothDirections()
    {
        var path=Path.Combine(Path.GetTempPath(),"pookie-chat-budget-"+Guid.NewGuid().ToString("N"));
        try
        {
            const long budget=1024*1024;
            await using var history=new ChatHistoryStore(new(path),42,budget);
            var messages=Enumerable.Range(0,5000).Select(i=>Message(i.ToString("D6"),"Синтетическая история "+i+new string('x',400),minute:i)).ToArray();
            await history.SavePageAsync(42,90,new(messages,null));
            var latest=await history.LoadWindowAsync(90);
            Assert.InRange(latest.Messages.Length,1,200);Assert.Equal("004999",latest.Messages[^1].Id);
            Assert.True(await history.SizeAsync()<=budget);Assert.False(latest.Complete);
            var older=await history.BeforeAsync(90,latest.Messages[^1],25);Assert.Equal(25,older.Length);
            var newer=await history.AfterAsync(90,older[0],25);Assert.Equal(25,newer.Length);
            Assert.Empty(await history.SearchAsync("0",91));
        }
        finally{Directory.Delete(path,true);}
    }
    [Fact] public void VoiceEncoderAndResumeDecodeNeverNeedAMicrophone()
    {
        var pcm=Enumerable.Range(0,16000).Select(i=>(float)(.3*Math.Sin(i*.08))).ToArray();
        var encoded=ChatVoicePcm.Encode(pcm);
        Assert.Contains(encoded.Extension,new[]{".ogg",".wav"});Assert.InRange(encoded.Data.Length,1,ChatMediaCrypto.MaxFileBytes);
        var resumed=ChatVoicePcm.Decode(encoded.Data);Assert.InRange(resumed.Length,15200,16800);
        var buffer=new ChatVoicePcm();buffer.Append(resumed);buffer.Append(new float[16000]);Assert.InRange(buffer.Duration,1.95,2.05);
        buffer.Clear();Assert.Empty(buffer.Peaks());
    }
    [Fact] public void NativeOpusEncodesAt48kAndKeepsQuietWaveformAndDuration()
    {
        var samples = Enumerable.Range(0,ChatVoicePcm.Rate).Select(i => (float)(.006*Math.Sin(i*.09)*(.15+.85*i/ChatVoicePcm.Rate))).ToArray();
        var encoded = ChatVoicePcm.Encode(samples);
        Assert.Equal(".ogg",encoded.Extension);
        Assert.True(encoded.Data.AsSpan(0,4).SequenceEqual("OggS"u8));
        Assert.Equal(1,encoded.Metadata.Duration);
        Assert.Equal(255,encoded.Metadata.Peaks.Max());
        Assert.True(encoded.Metadata.Peaks.Distinct().Count()>8);
        var restored = ChatVoicePcm.Decode(encoded.Data);
        Assert.InRange(restored.Length,15200,16800);
        Assert.Contains(restored,value=>Math.Abs(value)>.001);
    }
    [Fact] public void OpusResamplingHasTheSameSamplesAcrossBlockBoundaries()
    {
        var input = Enumerable.Range(0,10000).Select(i=>(float)(.3*Math.Sin(i*.16))).ToArray();
        var whole = new float[input.Length*3];
        Assert.Equal(whole.Length,ChatVoicePcm.ResampleOpus(input,0,whole));
        var chunked = new List<float>(); var block = new float[4096*3];
        for (var offset=0;offset<input.Length;offset+=4096)
        {
            var length=ChatVoicePcm.ResampleOpus(input,offset,block);
            chunked.AddRange(block.AsSpan(0,length).ToArray());
        }
        Assert.Equal(whole,chunked.ToArray());
        for (var i=0;i<input.Length;i++) Assert.Equal(input[i],whole[i*3]);
        Assert.All(whole,value=>Assert.True(float.IsFinite(value) && Math.Abs(value)<=1));
    }
    [Fact] public void LiveWavePositionNeverMovesBackWhenCapturePacketsArrive()
    {
        var position = new ChatVoiceWavePosition(); position.Append(25,0);
        double previous=position.Position;
        for (var tick=1;tick<200;tick++)
        {
            var now=tick*.016; position.Advance(now);
            Assert.InRange(position.Position,previous,previous+.400001);
            if (tick%3==0)
            {
                var before=position.Position;
                position.Append(25+(long)(now*25),now);
                Assert.Equal(before,position.Position);
            }
            previous=position.Position;
        }
        position.Advance(100); Assert.False(position.Moving);
        var frozen=position.Position; position.Advance(101); Assert.Equal(frozen,position.Position);
    }
    [Fact] public void VoiceCaptureCannotGrowBeyondFiveMinutesOrSixteenMiB()
    {
        var buffer=new ChatVoicePcm();buffer.Append(new float[ChatVoicePcm.MaximumSamples+16000]);
        Assert.Equal(300,buffer.Duration);
        var recording=ChatVoicePcm.Encode(buffer.Snapshot(),preferOpus:false);
        Assert.Equal(300,recording.Metadata.Duration);Assert.Equal(64,recording.Metadata.Peaks.Length);
        Assert.InRange(recording.Data.Length,44,ChatMediaCrypto.MaxFileBytes);
        buffer.Append(new float[16000]);Assert.Equal(300,buffer.Duration);buffer.Clear();
    }
    [Fact] public void LiveVoiceMeterKeepsRecentFixedWindowsAcrossCaptureCallbacks()
    {
        var pcm = new ChatVoicePcm();
        pcm.Append(Enumerable.Repeat(.01f, ChatVoicePcm.MeterSamples / 2).ToArray());
        Assert.Empty(pcm.Meter().Peaks);
        pcm.Append(Enumerable.Repeat(.04f, ChatVoicePcm.MeterSamples / 2).ToArray());
        Assert.Equal(.04f, Assert.Single(pcm.Meter().Peaks));
        pcm.Append(Enumerable.Repeat(.8f, ChatVoicePcm.MeterSamples).ToArray());
        Assert.Equal(new[] {.04f,.8f},pcm.Meter().Peaks);
        // A loud sound several seconds ago must disappear from the live window.
        pcm.Append(new float[ChatVoicePcm.MeterSamples * ChatVoicePcm.MeterCapacity]);
        var quiet = pcm.Meter();
        Assert.Equal(2, quiet.FirstFrame);
        Assert.Equal(ChatVoicePcm.MeterCapacity,quiet.Peaks.Length);
        Assert.All(quiet.Peaks,peak=>Assert.Equal(0,peak));
        pcm.Clear(); Assert.Empty(pcm.Meter().Peaks); Assert.Equal(0,pcm.Meter().FirstFrame);
        pcm.Append(Enumerable.Repeat(float.NaN,ChatVoicePcm.MeterSamples).ToArray());
        Assert.Equal(0,Assert.Single(pcm.Meter().Peaks));
    }
    [Fact] public void SyntheticVoiceHasBoundedDurationWaveAndCompatibleWav()
    {
        var samples=Enumerable.Range(0,16000).Select(i=>(float)(.5*Math.Sin(i*.1))).ToArray();
        var voice=ChatVoicePcm.Encode(samples,preferOpus:false);
        Assert.Equal(".wav",voice.Extension);Assert.Equal(32044,voice.Data.Length);Assert.Equal(1,voice.Metadata.Duration);Assert.Equal(64,voice.Metadata.Peaks.Length);
        var pcm=new ChatVoicePcm();pcm.Append(samples);Assert.Equal(1,pcm.Duration);pcm.Clear();Assert.Equal(0,pcm.Duration);
        Assert.Throws<InvalidDataException>(()=>ChatVoicePcm.Encode([]));
        var key=new byte[16];var package=new ChatMediaPackage(42,90,"",[new("voice.wav","audio/wav",voice.Data,Voice:voice.Metadata)],Reply:new(90,"original","Превью"));
        var restored=ChatMediaCrypto.Decrypt(ChatMediaCrypto.Encrypt(package,key),key,42,90);Assert.Equal(1,restored.Attachments[0].Voice!.Duration);Assert.Equal("original",restored.Reply!.MessageId);
    }
}
