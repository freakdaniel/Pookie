// SPDX-License-Identifier: BSD-3-Clause
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Pookie.App.Messages;
using Pookie.App.Storage;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatEnvelopeHostingTests
{
    private static ChatMessage Message(string id,string body,long sender=42,int minute=0)=>new(id,sender,body,DateTimeOffset.Parse("2026-10-25T12:00:00Z").AddMinutes(minute));

    [Fact] public async Task FullLengthReplyUsesAtMost36WireCharactersAndAuthenticatedHosting()
    {
        var server=new Host();using var hosting=new ChatMediaHosting(server);
        var content=ChatProtocol.Reply(42,90,"target","Превью 🔥",new string('ю',20000),hosted:true);
        var wire=await new ChatEnvelopeHosting().UploadAsync(hosting,content,42,90,default);
        Assert.InRange(wire.Length,1,36);Assert.True(ChatEnvelopeHosting.IsReference(wire));
        Assert.DoesNotContain(content,wire);Assert.DoesNotContain("Превью",wire);
        Assert.All(server.Files.Values,bytes=>Assert.DoesNotContain("Превью",System.Text.Encoding.UTF8.GetString(bytes)));
        var received=await new ChatEnvelopeHosting().ResolveAsync(hosting,Message("reply",wire),90,42,default);
        var projection=ChatProtocol.Project(received,90,42);
        Assert.Equal(new string('ю',20000),projection.Text);Assert.Equal("target",projection.Reply!.MessageId);
        Assert.Equal(wire,received.Body);Assert.NotNull(received.ClientContent);
        var wrongPeer=await new ChatEnvelopeHosting().ResolveAsync(hosting,Message("reply",wire),42,91,default);
        Assert.Null(wrongPeer.ClientContent);Assert.Null(ChatProtocol.Project(wrongPeer,42,91).Reply);
        Assert.Null((await new ChatEnvelopeHosting().ResolveAsync(hosting,Message("replay",wire,90),42,90,default)).ClientContent);
        Assert.Null(ChatProtocol.Project(received with{SenderId=7},90,42).Reply);
        Assert.Null(ChatProtocol.Project(received,42,91).Reply);
        Assert.Equal("target",ChatProtocol.Project(received,90,42).Reply!.MessageId);
    }
    [Fact] public async Task HostedReactionsAndReplySurviveRawRefreshRestartSearchAndConfirmation()
    {
        var root=Path.Combine(Path.GetTempPath(),"pookie-hosted-projection-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var hosting=new ChatMediaHosting(new Host());var sender=new ChatEnvelopeHosting();var receiver=new ChatEnvelopeHosting();
            async Task<ChatMessage> Prepare(string id,string content,long author,int minute)
            {
                var wire=await sender.UploadAsync(hosting,content,author,author==42?90:42,default);
                Assert.InRange(wire.Length,1,36);
                return await receiver.ResolveAsync(hosting,Message(id,wire,author,minute),42,90,default);
            }
            var reply=await Prepare("reply",ChatProtocol.Reply(42,90,"target","Цитата","Находимый ответ"),42,1);
            var first=await Prepare("first",ChatProtocol.React(42,90,"target","❤️"),42,2);
            var second=await Prepare("second",ChatProtocol.React(42,90,"target","🔥"),42,3);
            var other=await Prepare("other",ChatProtocol.React(90,42,"target","👍"),90,4);
            var rawReply=reply with{ClientContent=null};
            await using(var history=new ChatHistoryStore(new(root),42))
            {
                await history.SavePageAsync(42,90,new([rawReply],"older"));Assert.Empty(await history.SearchAsync("Находимый"));Assert.Single(await history.UnresolvedExtensionsAsync(90));
                await history.SavePageAsync(42,90,new([reply,first,second,other],"older"));
                await history.SavePageAsync(42,90,new([rawReply,first with{ClientContent=null}],"older"));
                Assert.Single(await history.SearchAsync("Находимый"));Assert.Empty(await history.SearchAsync("Реакция"));
                Assert.Empty(await history.UnresolvedExtensionsAsync(90));
                Assert.Equal("🔥",(await history.ReactionsAsync(90))["target"][42]);
                Assert.Equal("👍",(await history.ReactionsAsync(90))["target"][90]);
                var duplicate=first with{Id="duplicate",SentAt=first.SentAt.AddHours(1)};
                await history.SavePageAsync(42,90,new([duplicate],null),token:default,updateRange:false);
                Assert.Equal("🔥",(await history.ReactionsAsync(90))["target"][42]);
                Assert.Equal(reply.ClientContent,(await history.FindMessageAsync(90,reply.Id))!.ClientContent);
                Assert.NotNull((await history.AfterAsync(90,reply)).First().ClientContent);
                Assert.NotNull((await history.BeforeAsync(90,other)).First().ClientContent);
                var pending=new ChatPending(90,reply.Body,"Ответ",reply.SentAt,true,ClientContent:reply.ClientContent);
                Assert.Equal(reply.Id,(await history.FindConfirmationAsync(90,42,pending))!.Id);
            }
            await using(var restored=new ChatHistoryStore(new(root),42))
            {
                var hit=Assert.Single(await restored.SearchAsync("Находимый"));Assert.Equal("Находимый ответ",ChatProtocol.Project(hit.Message,42,90).Text);
                var window=await restored.LoadWindowAsync(90);Assert.Equal(2,ChatProtocol.Fold(window.Messages,42,90)["target"].Count);
                await restored.ClearAsync();Assert.Empty((await restored.LoadWindowAsync(90)).Messages);
            }
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task DiskCiphertextAndOutboxRestoreWithoutAWorkingHost()
    {
        var root=Path.Combine(Path.GetTempPath(),"pookie-hosted-outbox-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new AppDataPaths(root);var server=new Host();using var hosting=new ChatMediaHosting(server){DiskCache=new(paths,42)};
            var content=ChatProtocol.React(42,90,"target","🔥");
            var wire=await new ChatEnvelopeHosting().UploadAsync(hosting,content,42,90,default);
            var incoming=Message("event",wire);
            Assert.NotNull((await new ChatEnvelopeHosting().ResolveAsync(hosting,incoming,42,90,default)).ClientContent);
            server.Offline=true;
            var disk=await new ChatEnvelopeHosting().ResolveAsync(hosting,incoming,42,90,default);Assert.True(ChatProtocol.Project(disk,42,90).IsReaction);
            var store=new ChatLocalStore(paths,42);
            await store.SaveAsync(new([], [new(90,wire,"Реакция",incoming.SentAt,true,ClientContent:content)]));
            var pending=Assert.Single(store.Load().Pending);Assert.Equal(wire,pending.Body);Assert.Equal(content,pending.ClientContent);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task CorruptUnavailableAndOversizedPackagesStayRetryableAndDoNotResolve()
    {
        var server=new Host();using var hosting=new ChatMediaHosting(server);
        var content=ChatProtocol.Reply(42,90,"target","Превью","Ответ");
        var wire=await new ChatEnvelopeHosting().UploadAsync(hosting,content,42,90,default);
        var resolver=new ChatEnvelopeHosting();var message=Message("reply",wire);server.Offline=true;
        Assert.Null((await resolver.ResolveAsync(hosting,message,42,90,default)).ClientContent);
        server.Offline=false;
        Assert.NotNull((await resolver.ResolveAsync(hosting,message,42,90,default,retry:true)).ClientContent);
        var key=server.Files.Keys.Single();var valid=server.Files[key];server.Files[key]=new byte[1024*1024+1];
        Assert.Null((await new ChatEnvelopeHosting().ResolveAsync(hosting,message,42,90,default)).ClientContent);
        server.Files[key]=valid.ToArray();server.Files[key][^1]^=1;
        Assert.Null((await new ChatEnvelopeHosting().ResolveAsync(hosting,message,42,90,default)).ClientContent);
        Assert.False(ChatEnvelopeHosting.IsReference("p3:p:../evil:AAAAAAAAAAAAAAAAAAAAAA"));
        Assert.Equal("p3:broken",ChatProtocol.Project(Message("bad","p3:broken"),42,90).Text);
    }
    [Fact] public async Task IncomingConcurrentReferencesShareOneDownload()
    {
        var server=new Host();using var hosting=new ChatMediaHosting(server);
        var wire=await new ChatEnvelopeHosting().UploadAsync(hosting,ChatProtocol.React(42,90,"target","👍"),42,90,default);
        server.Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver=new ChatEnvelopeHosting();var message=Message("event",wire);
        var reads=Enumerable.Range(0,20).Select(_=>resolver.ResolveAsync(hosting,message,42,90,default)).ToArray();
        server.Ready.SetResult();await Task.WhenAll(reads);Assert.Equal(1,server.Downloads);
        Assert.All(reads,result=>Assert.NotNull(result.Result.ClientContent));
    }
    private sealed class Host : HttpMessageHandler
    {
        internal readonly ConcurrentDictionary<string,byte[]> Files=new();
        internal bool Offline;internal int Downloads;internal TaskCompletionSource? Ready;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Offline)return new(HttpStatusCode.ServiceUnavailable){Content=new ByteArrayContent([])};
            if(request.Method==HttpMethod.Post && request.RequestUri!.Host=="pomf.lain.la")
            {
                var form=(MultipartFormDataContent)request.Content!;
                var bytes=await form.Single(f=>f.Headers.ContentDisposition?.FileName!=null).ReadAsByteArrayAsync(token);
                var id=(Files.Count+1).ToString("D8");Files[id]=bytes;
                return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{success=true,files=new[]{new{url="https://pomf2.lain.la/f/"+id+".bin"}}}))};
            }
            if(request.Method!=HttpMethod.Get || request.RequestUri!.Host!="pomf2.lain.la")throw new InvalidOperationException("Only the local hosting fixture is allowed.");
            Interlocked.Increment(ref Downloads);if(Ready!=null)await Ready.Task.WaitAsync(token);
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(Files[Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath)])};
        }
    }
}
