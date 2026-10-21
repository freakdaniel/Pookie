using Pookie.App.Messages;
using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatPreferencesTests
{
    [Fact]
    public void DisablingDraftPersistenceRemovesOldDraftsAndPreservesOutboxAndReadPositions()
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie-chat-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ChatLocalStore(new AppDataPaths(path), 42);
            var created = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
            var pending = new ChatPending(90, "Message awaiting confirmation", "Message", created, true, ["before"]);
            var unread = new ChatUnreadState("last-read", created, 3);
            var state = new ChatLocalState(new() { [90] = "Private draft" }, [pending], new() { [90] = unread });
            store.Save(state);
            Assert.Single(store.Load().Drafts);

            store.Save(state, saveDrafts: false);

            var restored = new ChatLocalStore(new AppDataPaths(path), 42).Load();
            Assert.Empty(restored.Drafts);
            var restoredPending = Assert.Single(restored.Pending);
            Assert.Equal(pending.RecipientId, restoredPending.RecipientId);
            Assert.Equal(pending.Body, restoredPending.Body);
            Assert.True(restoredPending.Uncertain);
            Assert.Equal(pending.BeforeIds, restoredPending.BeforeIds);
            Assert.Equal(unread, restored.Unread![90]);
            Assert.DoesNotContain("Private draft", File.ReadAllText(Path.Combine(path, "Chats", "42", "drafts.json")));
            Assert.Single(state.Drafts);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }
}
