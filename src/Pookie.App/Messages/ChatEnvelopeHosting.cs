// SPDX-License-Identifier: BSD-3-Clause
using System.Security.Cryptography;
using System.Text;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Messages;

// Same authenticated hosting and 128-bit keys as attachments; SoundCloud only
// receives a <=36-character reference. The new prefix distinguishes extensions
// from actual files without changing existing p1 packages or legacy p2 reading.
internal sealed class ChatEnvelopeHosting
{
    internal const int MaxWireLength = 36;
    private const string ContentType = "application/vnd.pookie.message";
    private readonly AsyncResourceCache<(long Account,long Peer,long Sender,string Body),string> resolved =
        new(256,new CacheBudget(4*1024*1024),value=>value.Length*2L);
    private readonly ResourceCache<(long,long,long,string),DateTimeOffset> failures = new(256,new CacheBudget(16384),_=>64);
    private readonly SemaphoreSlim downloads = new(4,4);
    private static readonly UTF8Encoding Utf8 = new(false,true);

    internal static ChatMediaReference? Reference(string? body) => body?.StartsWith("p3:",StringComparison.Ordinal)==true
        ? ChatMediaReference.Parse("p1:"+body[3..]) : null;
    internal static bool IsReference(string? body)
    {
        var reference=Reference(body);
        if(reference==null)return false;
        CryptographicOperations.ZeroMemory(reference.Key);return true;
    }
    internal static string StorageCode(string body)
    {
        var reference=Reference(body);if(reference==null)return body;
        try{return reference.Code;}finally{CryptographicOperations.ZeroMemory(reference.Key);}
    }
    internal async Task<string> UploadAsync(ChatMediaHosting hosting,string content,long account,long peer,CancellationToken token)
    {
        var probe=new ChatMessage("extension",account,"p3:p:x:AAAAAAAAAAAAAAAAAAAAAA",DateTimeOffset.UtcNow){ClientContent=content};
        if(ChatProtocol.Project(probe,account,peer).EventId==null)throw new InvalidDataException("Некорректное сообщение Pookie.");
        var bytes=Utf8.GetBytes(content);
        try
        {
            var reference=await hosting.UploadAsync(new(account,peer,"",[new("message.pookie",ContentType,bytes,AsFile:true)]),token);
            try
            {
                var code="p3:"+reference.Code[3..];
                if(code.Length>MaxWireLength)throw new InvalidDataException("Код сообщения превышает 36 символов.");
                resolved[(account,peer,account,code)]=Task.FromResult<string?>(content);
                return code;
            }
            finally{CryptographicOperations.ZeroMemory(reference.Key);}
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    internal async Task<ChatMessage> ResolveAsync(ChatMediaHosting hosting,ChatMessage message,long account,long peer,CancellationToken token,bool retry=false)
    {
        if(!IsReference(message.Body) || message.SenderId!=account && message.SenderId!=peer)return message;
        if(message.ClientContent!=null && ChatProtocol.Project(message,account,peer).EventId!=null)return message;
        var key=(account,peer,message.SenderId,message.Body);
        if(retry)failures.Remove(key);
        if(failures.TryGetValue(key,out var until) && until>DateTimeOffset.UtcNow)return message;
        try
        {
            var content=await resolved.GetAsync(key,()=>DownloadAsync(hosting,message,account,peer,token),token);
            failures.Remove(key);return message with{ClientContent=content};
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or CryptographicException or HttpRequestException or OperationCanceledException or DecoderFallbackException or System.Text.Json.JsonException)
        {failures[key]=DateTimeOffset.UtcNow.AddSeconds(30);return message;}
    }
    private async Task<string?> DownloadAsync(ChatMediaHosting hosting,ChatMessage message,long account,long peer,CancellationToken token)
    {
        await downloads.WaitAsync(token).ConfigureAwait(false);
        var reference=Reference(message.Body)!;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var package=await hosting.DownloadAsync(reference,message.SenderId,message.SenderId==account?peer:account,deadline.Token,maximumBytes:1024*1024).ConfigureAwait(false);
            try
            {
                if(package.Caption!="" || package.GifUrl!=null || package.Reply!=null || package.Attachments is not [{Name:"message.pookie",MediaType:ContentType,AsFile:true} file] || file.Data.Length>ChatProtocol.MaxHostedContent)
                    throw new InvalidDataException("Некорректное сообщение Pookie.");
                var content=Utf8.GetString(file.Data);
                if(ChatProtocol.Project(message with{ClientContent=content},account,peer).EventId==null)throw new InvalidDataException("Некорректное сообщение Pookie.");
                return content;
            }
            finally{foreach(var file in package.Attachments)CryptographicOperations.ZeroMemory(file.Data);}
        }
        finally{CryptographicOperations.ZeroMemory(reference.Key);downloads.Release();}
    }
}
