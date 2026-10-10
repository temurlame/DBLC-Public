using DBLC;
using System.IO;
using System.Text.Json;

internal static partial class Checks
{
    private static async Task PersistenceRepairChecks(string temporary)
    {
        var root = Path.Combine(temporary, "persistence-repairs");
        Directory.CreateDirectory(root);

        // A failed active-campaign deletion must also undo fallback Session creation metadata.
        var fallbackRoot = Path.Combine(root, "empty-fallback-campaign");
        var failFallbackSave = false;
        var fallbackStore = new ConversationStore(fallbackRoot, () => { if (failFallbackSave) throw new IOException("Injected active-campaign deletion failure"); });
        var activeCampaign = new Campaign { Name = "Active", LastPlayedAt = DateTimeOffset.Parse("2002-01-01T00:00:00+00:00") };
        var emptyFallback = new Campaign { Name = "Empty fallback", LastPlayedAt = DateTimeOffset.Parse("2001-01-01T00:00:00+00:00") };
        var activeSession = new Conversation { CampaignId = activeCampaign.Id, SessionNumber = 0 };
        activeCampaign.SessionOrder.Add(activeSession.Id);
        fallbackStore.Save(new SavedState("local", "fixture", "", null, [activeSession], [activeCampaign, emptyFallback], activeCampaign.Id));
        var fallbackState = NewRepairState(fallbackStore, Path.Combine(fallbackRoot, "archive.db"));
        var activeAsset = Path.Combine(fallbackStore.DataRoot, "campaigns", activeCampaign.Id.ToString("N"), "npcs", "fixture.png");
        var fallbackAsset = Path.Combine(fallbackStore.DataRoot, "campaigns", emptyFallback.Id.ToString("N"), "npcs", "fixture.png");
        Directory.CreateDirectory(Path.GetDirectoryName(activeAsset)!); File.WriteAllText(activeAsset, "active asset");
        Directory.CreateDirectory(Path.GetDirectoryName(fallbackAsset)!); File.WriteAllText(fallbackAsset, "fallback asset");
        var beforeCampaigns = JsonSerializer.Serialize(fallbackState.Campaigns);
        var beforeConversations = JsonSerializer.Serialize(fallbackState.Conversations);
        var beforeSavedState = JsonSerializer.Serialize(fallbackStore.Load());
        var beforeCurrentId = fallbackState.Current?.Id;
        failFallbackSave = true;
        try { fallbackState.DeleteCampaign(activeCampaign.Id); throw new InvalidOperationException("Expected active-campaign snapshot failure"); }
        catch (IOException) { }
        Require(JsonSerializer.Serialize(fallbackState.Campaigns) == beforeCampaigns &&
            JsonSerializer.Serialize(fallbackState.Conversations) == beforeConversations &&
            fallbackState.ActiveCampaignId == activeCampaign.Id && fallbackState.Current?.Id == beforeCurrentId &&
            JsonSerializer.Serialize(fallbackStore.Load()) == beforeSavedState &&
            File.Exists(activeAsset) && File.Exists(fallbackAsset),
            "failed active-campaign deletion restores both campaigns, Session order, last-played metadata, selection, and assets");
        failFallbackSave = false;
        fallbackState.DeleteCampaign(activeCampaign.Id);
        var fallbackSession = fallbackState.Conversations.Single(item => item.CampaignId == emptyFallback.Id);
        Require(fallbackState.Campaigns.Count == 1 && fallbackState.ActiveCampaignId == emptyFallback.Id &&
            fallbackState.Current?.Id == fallbackSession.Id && fallbackState.Campaigns[0].SessionOrder.SequenceEqual([fallbackSession.Id]) &&
            !fallbackStore.Load().Campaigns!.Any(item => item.Id == activeCampaign.Id) &&
            !File.Exists(activeAsset) && File.Exists(fallbackAsset),
            "retry deletes the active campaign and creates one valid fallback Session before scoped cleanup");

        // A failed logical deletion must leave the authoritative snapshot and all bytes intact.
        var deletionRoot = Path.Combine(root, "deletion");
        var failSave = false;
        var store = new ConversationStore(deletionRoot, () => { if (failSave) throw new IOException("Injected snapshot replacement failure"); });
        var first = new Campaign { Name = "Keep" };
        var second = new Campaign { Name = "Delete" };
        var firstSession = new Conversation { CampaignId = first.Id, SessionNumber = 1 };
        var secondSession = new Conversation { CampaignId = second.Id, SessionNumber = 1 };
        store.Save(new SavedState("local", "fixture", "", null, [firstSession, secondSession], [first, second], first.Id));
        var archivePath = Path.Combine(deletionRoot, "archive.db");
        var state = NewRepairState(store, archivePath);
        var sessionAsset = Path.Combine(store.DataRoot, "campaigns", first.Id.ToString("N"), "sessions", firstSession.Id.ToString("N"), "chronicle-images", "fixture.png");
        var campaignAsset = Path.Combine(store.DataRoot, "campaigns", second.Id.ToString("N"), "imported-maps", "fixture.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sessionAsset)!); File.WriteAllText(sessionAsset, "session asset");
        Directory.CreateDirectory(Path.GetDirectoryName(campaignAsset)!); File.WriteAllText(campaignAsset, "campaign asset");
        failSave = true;
        try { state.Delete(state.Conversations.Single(item => item.Id == firstSession.Id)); throw new InvalidOperationException("Expected session snapshot failure"); }
        catch (IOException) { }
        Require(state.Conversations.Any(item => item.Id == firstSession.Id) && store.Load().Conversations.Any(item => item.Id == firstSession.Id) && File.Exists(sessionAsset),
            "failed Session deletion restores memory and leaves snapshot and assets intact");
        try { state.DeleteCampaign(second.Id); throw new InvalidOperationException("Expected campaign snapshot failure"); }
        catch (IOException) { }
        Require(state.Campaigns.Any(item => item.Id == second.Id) && store.Load().Campaigns!.Any(item => item.Id == second.Id) && File.Exists(campaignAsset),
            "failed campaign deletion restores memory and leaves snapshot and assets intact");
        failSave = false;
        state.Delete(state.Conversations.Single(item => item.Id == firstSession.Id));
        Require(!store.Load().Conversations.Any(item => item.Id == firstSession.Id) && !File.Exists(sessionAsset), "committed Session deletion removes its data only after the snapshot and backup advance");
        var backupAfterSession = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(Path.Combine(store.DataRoot, "state.json.previous")))!;
        Require(!backupAfterSession.Conversations.Any(item => item.Id == firstSession.Id), "recovery copy no longer references Session assets before cleanup");
        state.DeleteCampaign(second.Id);
        Require(!store.Load().Campaigns!.Any(item => item.Id == second.Id) && !File.Exists(campaignAsset), "committed campaign deletion removes its data after logical deletion");
        var backupAfterCampaign = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(Path.Combine(store.DataRoot, "state.json.previous")))!;
        Require(!backupAfterCampaign.Campaigns!.Any(item => item.Id == second.Id), "recovery copy no longer references campaign assets before cleanup");

        // Crash after the first deletion snapshot, before the backup barrier: restart retries the journal.
        var retryRoot = Path.Combine(root, "restart-cleanup");
        var saveNumber = 0;
        var failBarrier = false;
        var retryStore = new ConversationStore(retryRoot, () => { if (failBarrier && ++saveNumber == 2) throw new IOException("Injected backup barrier failure"); });
        var retryCampaign = new Campaign();
        var retrySession = new Conversation { CampaignId = retryCampaign.Id };
        retryStore.Save(new SavedState("local", "fixture", "", null, [retrySession], [retryCampaign], retryCampaign.Id));
        var retryState = NewRepairState(retryStore, Path.Combine(retryRoot, "archive.db"));
        var retryAsset = Path.Combine(retryStore.DataRoot, "campaigns", retryCampaign.Id.ToString("N"), "sessions", retrySession.Id.ToString("N"), "chronicle-images", "fixture.png");
        Directory.CreateDirectory(Path.GetDirectoryName(retryAsset)!); File.WriteAllText(retryAsset, "retry");
        failBarrier = true;
        retryState.Delete(retryState.Conversations.Single(item => item.Id == retrySession.Id));
        Require(File.Exists(retryAsset) && retryStore.Load().PendingDataCleanup?.Count == 1,
            "interrupted cleanup keeps bytes and a durable retry intent");
        failBarrier = false;
        _ = NewRepairState(new ConversationStore(retryRoot), Path.Combine(retryRoot, "archive.db"));
        Require(!File.Exists(retryAsset) && new ConversationStore(retryRoot).Load().PendingDataCleanup?.Count == 0,
            "restart completes pending cleanup without restoring a deleted Session");

        var campaignRetryRoot = Path.Combine(root, "restart-campaign-cleanup");
        var campaignSaveNumber = 0;
        var failCampaignBarrier = false;
        var campaignRetryStore = new ConversationStore(campaignRetryRoot, () => { if (failCampaignBarrier && ++campaignSaveNumber == 2) throw new IOException("Injected campaign backup barrier failure"); });
        var campaignToDelete = new Campaign();
        var campaignToKeep = new Campaign();
        var deletedCampaignSession = new Conversation { CampaignId = campaignToDelete.Id };
        var keptCampaignSession = new Conversation { CampaignId = campaignToKeep.Id };
        campaignRetryStore.Save(new SavedState("local", "fixture", "", null,
            [deletedCampaignSession, keptCampaignSession], [campaignToDelete, campaignToKeep], campaignToKeep.Id));
        var campaignArchivePath = Path.Combine(campaignRetryRoot, "archive.db");
        var campaignArchive = new ChronicleArchiveStore(campaignArchivePath);
        campaignArchive.Archive(deletedCampaignSession.Id, [new ChatLine { Sequence = 1, Role = "user", Content = "archived fixture" }]);
        var campaignRetryState = NewRepairState(campaignRetryStore, campaignArchivePath);
        var campaignRetryAsset = Path.Combine(campaignRetryStore.DataRoot, "campaigns", campaignToDelete.Id.ToString("N"), "npcs", "fixture.png");
        var keptAsset = Path.Combine(campaignRetryStore.DataRoot, "campaigns", campaignToKeep.Id.ToString("N"), "npcs", "fixture.png");
        var legacySheet = Path.Combine(campaignRetryStore.DataRoot, "player-sheet.md");
        File.WriteAllText(legacySheet, "old campaign paper");
        Directory.CreateDirectory(Path.GetDirectoryName(campaignRetryAsset)!); File.WriteAllText(campaignRetryAsset, "private asset");
        Directory.CreateDirectory(Path.GetDirectoryName(keptAsset)!); File.WriteAllText(keptAsset, "still referenced");
        failCampaignBarrier = true;
        campaignRetryState.DeleteCampaign(campaignToDelete.Id);
        Require(File.Exists(campaignRetryAsset) && File.Exists(legacySheet) && campaignRetryState.LegacyPaperFallbackDisabled &&
            campaignArchive.GetArchivedLines(deletedCampaignSession.Id).Count == 1 && campaignRetryStore.Load().PendingDataCleanup?.Count == 1,
            "interrupted campaign cleanup keeps archive and assets with a durable retry intent");
        var newCampaignPapers = new PaperDocumentState(campaignRetryState,
            Isolate(new WorkspaceNotesStore(), "_folder", campaignRetryStore.DataRoot),
            Isolate(new PlayerSheetStore(), "_folder", campaignRetryStore.DataRoot));
        Require(newCampaignPapers.PlayerMarkdown == "", "new first campaign does not inherit a deleted campaign's legacy sheet while cleanup is pending");
        failCampaignBarrier = false;
        _ = NewRepairState(new ConversationStore(campaignRetryRoot), campaignArchivePath);
        Require(!File.Exists(campaignRetryAsset) && !File.Exists(legacySheet) && campaignArchive.GetArchivedLines(deletedCampaignSession.Id).Count == 0 && File.Exists(keptAsset),
            "restart completes campaign cleanup without touching another campaign or leaking legacy paper fallback");

        // Cleanup may finish, then the journal-retirement save can fail. Repeating cleanup must be safe.
        var retireRoot = Path.Combine(root, "cleanup-retirement");
        var retireSaveNumber = 0;
        var failRetirement = false;
        var retireStore = new ConversationStore(retireRoot, () => { if (failRetirement && ++retireSaveNumber == 3) throw new IOException("Injected cleanup retirement failure"); });
        var retireCampaign = new Campaign();
        var retireSession = new Conversation { CampaignId = retireCampaign.Id };
        retireStore.Save(new SavedState("local", "fixture", "", null, [retireSession], [retireCampaign], retireCampaign.Id));
        var retireState = NewRepairState(retireStore, Path.Combine(retireRoot, "archive.db"));
        var retireAsset = Path.Combine(retireStore.DataRoot, "campaigns", retireCampaign.Id.ToString("N"), "npcs", "fixture.png");
        Directory.CreateDirectory(Path.GetDirectoryName(retireAsset)!); File.WriteAllText(retireAsset, "retry after removal");
        failRetirement = true;
        retireState.DeleteCampaign(retireCampaign.Id);
        Require(!File.Exists(retireAsset) && retireStore.Load().PendingDataCleanup?.Count == 1,
            "completed asset cleanup retains its journal when the retirement snapshot fails");
        failRetirement = false;
        _ = NewRepairState(new ConversationStore(retireRoot), Path.Combine(retireRoot, "archive.db"));
        Require(new ConversationStore(retireRoot).Load().PendingDataCleanup?.Count == 0,
            "restart safely repeats completed cleanup and retires the durable intent");

        // A read failure is distinct from a missing paper, and a failed save blocks switching.
        var paperRoot = Path.Combine(root, "papers");
        var paperStore = new ConversationStore(paperRoot);
        var paperCampaign = new Campaign();
        var otherCampaign = new Campaign();
        paperStore.Save(new SavedState("local", "fixture", "", null,
            [new Conversation { CampaignId = paperCampaign.Id }, new Conversation { CampaignId = otherCampaign.Id }],
            [paperCampaign, otherCampaign], paperCampaign.Id));
        var paperState = NewRepairState(paperStore, Path.Combine(paperRoot, "archive.db"));
        var playerStore = Isolate(new PlayerSheetStore(), "_folder", paperStore.DataRoot);
        var workspaceStore = Isolate(new WorkspaceNotesStore(), "_folder", paperStore.DataRoot);
        playerStore.Save(paperCampaign.Id, "original sheet");
        workspaceStore.Save(paperCampaign.Id, "original notes");
        var playerPath = Path.Combine(paperStore.DataRoot, "campaigns", paperCampaign.Id.ToString("N"), "player-sheet.md");
        var workspacePath = Path.Combine(paperStore.DataRoot, "campaigns", paperCampaign.Id.ToString("N"), "workspace.md");
        File.Move(playerPath, playerPath + ".held"); Directory.CreateDirectory(playerPath);
        File.Move(workspacePath, workspacePath + ".held"); Directory.CreateDirectory(workspacePath);
        var papers = new PaperDocumentState(paperState, workspaceStore, playerStore);
        Require(papers.PlayerUnavailable && papers.NotesUnavailable && papers.SaveAll() && Directory.Exists(playerPath) && Directory.Exists(workspacePath),
            "unreadable papers are surfaced and save performs no overwrite");
        try { _ = papers.ReadPlayerMarkdown(paperCampaign.Id); throw new InvalidOperationException("Expected Player Sheet read failure"); }
        catch (IOException) { }
        try { _ = papers.ReadWorkspaceNotes(paperCampaign.Id); throw new InvalidOperationException("Expected Workspace read failure"); }
        catch (IOException) { }
        Require(Directory.Exists(playerPath) && Directory.Exists(workspacePath), "model-facing paper reads do not misrepresent inaccessible files as empty documents");
        Directory.Delete(playerPath); File.Move(playerPath + ".held", playerPath);
        Directory.Delete(workspacePath); File.Move(workspacePath + ".held", workspacePath);
        papers.RetryUnavailablePapers();
        Require(!papers.PlayerUnavailable && !papers.NotesUnavailable && papers.PlayerMarkdown == "original sheet" && papers.Notes == "original notes",
            "restored access recovers the original paper contents");
        papers.SetNotes("unsaved notes"); papers.SetPlayerMarkdown("unsaved sheet");
        File.Move(playerPath, playerPath + ".held"); Directory.CreateDirectory(playerPath);
        File.Move(workspacePath, workspacePath + ".held"); Directory.CreateDirectory(workspacePath);
        Require(!papers.SaveAll(), "failed paper writes retain dirty text and visible save errors");
        paperState.SelectCampaign(otherCampaign.Id);
        Require(paperState.ActiveCampaignId == paperCampaign.Id && papers.Notes == "unsaved notes" && papers.PlayerMarkdown == "unsaved sheet",
            "campaign switching cannot discard dirty papers after a failed save");
        Directory.Delete(playerPath); File.Move(playerPath + ".held", playerPath);
        Directory.Delete(workspacePath); File.Move(workspacePath + ".held", workspacePath);
        Require(papers.SaveAll() && playerStore.Load(paperCampaign.Id) == "unsaved sheet" && workspaceStore.Load(paperCampaign.Id) == "unsaved notes",
            "restoring write access saves retained drafts");
        paperState.SelectCampaign(otherCampaign.Id);
        Require(paperState.ActiveCampaignId == otherCampaign.Id, "campaign switching succeeds after paper recovery");

        foreach (var withImage in new[] { false, true })
        {
            var sendRoot = Path.Combine(root, withImage ? "send-with-image" : "send-without-image");
            var rejectNext = false;
            var sendStore = new ConversationStore(sendRoot, () => { if (rejectNext) { rejectNext = false; throw new IOException("Injected first replacement failure"); } });
            var sendCampaign = new Campaign();
            var sendSession = new Conversation { CampaignId = sendCampaign.Id };
            sendStore.Save(new SavedState("local", "fixture", "", null, [sendSession], [sendCampaign], sendCampaign.Id));
            var sendState = NewRepairState(sendStore, Path.Combine(sendRoot, "archive.db"));
            var images = Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(sendStore.DataRoot, "campaigns"));
            var key = withImage ? images.Save(sendCampaign.Id, sendSession.Id, "fixture.png", TestMapPng()) : null;
            var vision = withImage ? new LlamaVisionImage("image/png", "data:image/png;base64," + Convert.ToBase64String(TestMapPng())) : null;
            rejectNext = true;
            var accepted = false; string? rejected = null;
            await sendState.SendAsync("Inspect the room.", new LlamaClient(), visionImage: vision, imageAttachmentKey: key,
                onAccepted: () => accepted = true, onRejected: reason => rejected = reason);
            Require(!accepted && rejected is not null && sendState.Current!.Lines.Count == 0 && sendState.Current.NextSequence == 1,
                $"failed pre-accept save rolls back lines and sequence (image={withImage})");
            sendState.Select(sendState.Current!);
            Require(sendStore.Load().Conversations.Single().Lines.Count == 0, $"later successful save cannot capture rejected send (image={withImage})");
            if (key is not null) Require(images.ReadDataUrl(sendCampaign.Id, sendSession.Id, key) is not null, "unaccepted attachment remains available for a safe retry");
            using var server = new ModelStub((_, _) => Text("The room is quiet."));
            sendState.Endpoint = server.Endpoint;
            await sendState.SendAsync("Inspect the room.", new LlamaClient(), visionImage: vision, imageAttachmentKey: key);
            var persisted = sendStore.Load().Conversations.Single().Lines;
            Require(persisted.Count(line => line.Role == "user" && line.Content == "Inspect the room.") == 1 &&
                (key is null || persisted.Single(line => line.Role == "user").ImageAttachmentKey == key),
                $"retry persists exactly one accepted player message (image={withImage})");
            if (key is not null)
            {
                new ChronicleArchiveStore(Path.Combine(sendRoot, "archive.db")).Archive(sendSession.Id,
                    [new ChatLine { Sequence = 20, Role = "user", Content = "Archived image", ImageAttachmentKey = key }]);
                sendState.Current!.Lines.RemoveAll(line => line.ImageAttachmentKey == key);
                sendState.Select(sendState.Current);
                Require(sendState.HasChronicleImageReference(sendCampaign.Id, sendSession.Id, key),
                    "attachment cleanup checks archived Chronicle references as well as active lines");
            }
        }
    }

    private static AppState NewRepairState(ConversationStore store, string archivePath) => new(store, new LibraryService(),
        new ChronicleArchiveStore(archivePath),
        Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(store.DataRoot, "campaigns")),
        Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(store.DataRoot, "campaigns")),
        Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(store.DataRoot, "campaigns")),
        new CampaignMapAssetStore(Path.Combine(store.DataRoot, "campaigns")));
}
