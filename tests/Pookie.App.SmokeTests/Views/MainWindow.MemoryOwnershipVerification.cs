// SPDX-License-Identifier: BSD-3-Clause
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using System.Reflection;
namespace Pookie.App;
internal sealed partial class MainWindow
{
    private async Task VerifyUnrestoredVoiceDraftRetentionAsync()
    {
        var account=chatAccountId;var ready=chatAccountReady;
        var directory=Path.Combine(dataPaths.Root,"Chats","424242","VoiceDrafts");
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".wav");
        try
        {
            await File.WriteAllBytesAsync(path,new byte[44]);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-3));
            chatAccountId=424242;chatAccountReady=false;
            CleanupChatVoiceFiles();await Task.Delay(100);
            if(!File.Exists(path))throw new InvalidOperationException("Closing before workspace restore deleted a durable voice draft.");
            Console.WriteLine("VOICE_DRAFT_RESTORE_OK: an unread index never triggers recording cleanup");
        }
        finally{chatAccountId=account;chatAccountReady=ready;File.Delete(path);}
    }
    private void VerifyChatMenuOwnership()
    {
        var row=new ChatBubbleRow(this);
        var message=new Pookie.SoundCloud.ChatMessage("ownership-menu",42,"plain text",DateTimeOffset.UtcNow);
        row.Bind(message);var menu=row.Root.ContextMenu;var items=menu!.Items.ToArray();
        for(var i=0;i<10000;i++)row.Bind(message);
        if(!ReferenceEquals(menu,row.Root.ContextMenu)||!menu.Items.SequenceEqual(items))
            throw new InvalidOperationException("Polling rebuilt chat menu models/subscriptions.");
        row.Clear();row.Root.Dispose();Console.WriteLine("CHAT_MENU_OWNERSHIP_OK: 10000 rebinds reuse one menu and its items");
    }
    private async Task VerifyVoiceStartupOwnershipAsync()
    {
        timer.Stop();
        using var ready=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var players=new List<ChatVideoSession>();
        var nativeGate=typeof(ChatVideoSession).GetField("initialization",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var blocker=Task.Run(()=>{lock(nativeGate){ready.Set();release.Wait();}});
        try
        {
            if(!ready.Wait(3000))throw new InvalidOperationException("Native startup blocker unavailable");
            videoMusicGain=0;
            var pcm=ChatVoicePcm.Encode(new float[16000*3],preferOpus:false).Data;
            var one=PlayChatVoiceBytesAsync(pcm.ToArray(),"audit:one");
            var two=PlayChatVoiceBytesAsync(pcm.ToArray(),"audit:two");
            Task<ChatVideoSession> Pending(Task operation)
            {
                var machine=operation.GetType().GetField("StateMachine",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(operation)!;
                foreach(var field in machine.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)){
                    var value=field.GetValue(machine);if(value==null)continue;
                    var taskField=value.GetType().GetField("m_task",BindingFlags.Instance|BindingFlags.NonPublic);
                    if(taskField?.GetValue(value) is Task<ChatVideoSession> pending)return pending;
                }
                throw new InvalidOperationException("Startup awaiter not found");
            }
            var startOne=Pending(one);var startTwo=Pending(two);
            Console.WriteLine($"VOICE_RACE_PENDING: first={one.IsCompleted}; second={two.IsCompleted}; generation={chatVoiceGeneration}");
            release.Set();await blocker;
            await Task.WhenAll(one,two).WaitAsync(TimeSpan.FromSeconds(20));
            players.Add(await startOne);players.Add(await startTwo);
            var isDisposed=typeof(ChatVideoSession).GetField("disposed",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var active=players.Count(p=>!(bool)isDisposed.GetValue(p)!);
            var orphan=players.Count(p=>!ReferenceEquals(p,chatVoicePlayer)&&!(bool)isDisposed.GetValue(p)!);
            Console.WriteLine($"VOICE_RACE: created={players.Count}; undisposed={active}; lost_owner={orphan}; current={chatVoicePlayingId}");
            if(orphan!=0 || active!=1 || chatVoicePlayingId!="audit:two")throw new InvalidOperationException("Overlapping voice starts leaked or published a stale session.");
                        await StopChatVoicePlaybackAsync();
            release.Reset(); ready.Reset();
            var stoppingBlocker=Task.Run(()=>{lock(nativeGate){ready.Set();release.Wait();}});
            if(!ready.Wait(3000))throw new InvalidOperationException("Stop blocker unavailable");
            var stoppedStart=PlayChatVoiceBytesAsync(pcm.ToArray(),"audit:stopped");
            var stoppedPlayer=Pending(stoppedStart);
            await StopChatVoicePlaybackAsync();release.Set();await stoppingBlocker;await stoppedStart;
            var late=await stoppedPlayer;players.Add(late);
            if(chatVoicePlayer!=null || !(bool)isDisposed.GetValue(late)!)throw new InvalidOperationException("Stop did not invalidate a pending voice start.");
            Console.WriteLine("VOICE_OWNERSHIP_OK: superseded and stopped starts disposed; no orphan players");
        }
        catch { throw; }
        finally
        {
            release.Set();await blocker;chatVoiceTimer.Stop();chatVoicePlayer=null;
            foreach(var p in players.Distinct())await p.DisposeAsync();
            chatVoicePlayingId=null;chatVoiceAudioActive=false;RestoreMusicAfterChatVideo();
        }
    }
}
