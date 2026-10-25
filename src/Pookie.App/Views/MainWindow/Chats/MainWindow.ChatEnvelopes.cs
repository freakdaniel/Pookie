// SPDX-License-Identifier: BSD-3-Clause
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatEnvelopeHosting chatEnvelopes=new();

    private async Task<ChatMessage> ResolveChatEnvelopeAsync(ChatMessage message,long account,long peer,CancellationToken token,bool retry=false)
    {
        if(!ChatEnvelopeHosting.IsReference(message.Body) || chatAccountId!=account)return message;
        var host=chatMedia;var resolver=chatEnvelopes;var history=chatHistory;
        var content=message.SenderId!=account ? null :
            chatPending.GetValueOrDefault(peer) is {} pending && pending.Body==message.Body ? pending.ClientContent :
            chatOutgoing.GetValueOrDefault(peer)?.Message is {} outgoing && outgoing.Body==message.Body ? outgoing.ClientContent : null;
        if(content==null && chatRecipient?.Id==peer)
            content=chatMessages.FirstOrDefault(m=>m.SenderId==message.SenderId && m.Body==message.Body)?.ClientContent;
        content??=chatConversations.FirstOrDefault(c=>c.Recipient.Id==peer)?.LastMessage is {} last && last.SenderId==message.SenderId && last.Body==message.Body ? last.ClientContent : null;
        if(content!=null)message=message with{ClientContent=content};
        if(message.ClientContent==null && history!=null)
        {
            var saved=await history.FindMessageAsync(peer,message.Id,token);
            if(saved?.Body==message.Body && saved.SenderId==message.SenderId)message=message with{ClientContent=saved.ClientContent};
        }
        return await resolver.ResolveAsync(host,message,account,peer,token,retry);
    }
    private async Task<ChatMessagePage> GetChatMessagesAsync(long account,long peer,string? cursor=null,CancellationToken token=default)
    {
        var page=await api.GetMessagesAsync(account,peer,cursor,token);
        return page with{Messages=await Task.WhenAll(page.Messages.Select(message=>ResolveChatEnvelopeAsync(message,account,peer,token)))};
    }
    private async Task<ChatConversationPage> GetChatConversationsAsync(long account,string? cursor=null,CancellationToken token=default)
    {
        var page=await api.GetConversationsAsync(account,cursor,token);
        return page with{Conversations=await Task.WhenAll(page.Conversations.Select(async dialog=>dialog.LastMessage is {} last
            ? dialog with{LastMessage=await ResolveChatEnvelopeAsync(last,account,dialog.Recipient.Id,token)} : dialog))};
    }
    private async Task RetryChatEnvelopeAsync(ChatMessage message,long peer)
    {
        var account=chatAccountId;var store=chatHistory;var token=ChatReadToken();
        var resolved=await ResolveChatEnvelopeAsync(message,account,peer,token,retry:true);
        if(account!=chatAccountId || chatRecipient?.Id!=peer)return;
        if(ChatOutgoing(message) is {} outgoing)
        {
            outgoing.Message=resolved;
            if(chatPending.TryGetValue(peer,out var pending) && pending.Body==resolved.Body)
                chatPending[peer]=pending with{ClientContent=resolved.ClientContent};
            SaveChatState();RefreshChatTimeline();return;
        }
        if(store!=null)await store.SavePageAsync(account,peer,new([resolved],null),token:token,updateRange:false);
        if(account!=chatAccountId || chatRecipient?.Id!=peer)return;
        var index=chatMessages.FindIndex(m=>m.Id==message.Id);
        if(index>=0)chatMessages[index]=resolved;
        var reactions=store==null ? null : await store.ReactionsAsync(peer,token,chatMessages.Select(m=>m.Id));
        if(account==chatAccountId && chatRecipient?.Id==peer)
        {if(reactions!=null)chatReactions=reactions;RefreshChatTimeline();}
    }
    private async Task<bool> ResolveStoredChatEnvelopesAsync(ChatHistoryStore store,long account,long peer,CancellationToken token)
    {
        while(true)
        {
            var pending=await store.UnresolvedExtensionsAsync(peer,token);
            if(pending.Length==0)return true;
            var messages=await Task.WhenAll(pending.Select(message=>ResolveChatEnvelopeAsync(message,account,peer,token)));
            if(chatHistory!=store || chatAccountId!=account)throw new OperationCanceledException(token);
            var ready=messages.Where(message=>ChatProtocol.Project(message,account,peer).EventId!=null).ToArray();
            if(ready.Length==0)return false;
            await store.SavePageAsync(account,peer,new(ready,null),token:token,updateRange:false);
        }
    }
}
