using DBLC;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;

internal static partial class Checks
{
    private static async Task FinalPersistenceChecks(string temporary)
    {
        var root = Path.Combine(temporary, "final-persistence");
        Directory.CreateDirectory(root);

        var ownerRoot = Path.Combine(root, "writer");
        using (var owner = new DataRootWriterLock(ownerRoot))
        {
            try { using var competing = new DataRootWriterLock(ownerRoot); throw new InvalidOperationException("Expected single-writer rejection"); }
            catch (IOException) { }
        }
        using (var recovered = new DataRootWriterLock(ownerRoot))
            Require(File.Exists(Path.Combine(ownerRoot, ".writer.lock")), "data-root ownership rejects a second writer and is released after disposal");

        var campaign = new Campaign { Name = "Source" };
        var session = new Conversation { CampaignId = campaign.Id, SessionNumber = 0, Title = "Original" };
        session.Lines.Add(new ChatLine { Sequence = 1, Role = "assistant", EntryType = "campaign_record", Content = "First record" });
        session.Lines.Add(new ChatLine { Sequence = 2, Role = "user", Content = "Player text" });
        session.Lines.Add(new ChatLine { Sequence = 3, Role = "assistant", EntryType = "campaign_record", Content = "Second record" });
        session.Lines.Add(new ChatLine { Sequence = 4, Role = "assistant", Content = "GM text" });
        session.NextSequence = 5;
        campaign.SessionOrder.Add(session.Id);
        var failSave = false;
        var store = new ConversationStore(Path.Combine(root, "import-copy"), () => { if (failSave) throw new IOException("Injected snapshot failure"); });
        store.Save(new SavedState("local", "fixture", "", null, [session], [campaign], campaign.Id));
        var archive = new ChronicleArchiveStore(Path.Combine(root, "import-copy", "archive.db"));
        var state = NewRepairState(store, Path.Combine(root, "import-copy", "archive.db"));
        var markdown = "# Session 0 — Imported\n\nStatus: In progress\nLast activity: 2026-10-05\n\n## Chronicle\n\n" +
            "### Campaign Record · 2026-10-05 08:00\n\nFirst record\n\n" +
            "### Player · 2026-10-05 08:01\n\nPlayer text\n\n" +
            "### Campaign Record · 2026-10-05 08:02\n\nSecond record\n\n" +
            "### GM · 2026-10-05 08:03\n\nGM text\n";
        failSave = true;
        try { state.ImportSessionMarkdown(campaign.Id, markdown, out _); throw new InvalidOperationException("Expected import failure"); }
        catch (IOException) { }
        Require(state.Conversations.Count == 1 && state.Current?.Id == session.Id && state.Campaigns.Single().SessionOrder.SequenceEqual([session.Id]),
            "failed Session import leaves no in-memory record or changed selection");
        failSave = false;
        state.SaveSettings();
        Require(store.Load().Conversations.Count == 1, "later save cannot commit rejected Session import");
        Require(state.ImportSessionMarkdown(campaign.Id, markdown, out _), "Session import succeeds after access recovery");
        var imported = state.Current!;
        Require(imported.Lines.Select(line => line.EntryType).SequenceEqual(["campaign_record", null, "campaign_record", null]) &&
            imported.Lines.Select(line => line.Content).SequenceEqual(["First record", "Player text", "Second record", "GM text"]) &&
            imported.Lines.Select(line => line.Sequence).SequenceEqual([1L, 2L, 3L, 4L]),
            "Markdown import preserves Campaign Records before and between ordinary messages in order");

        var copyId = Guid.NewGuid();
        var images = Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(store.DataRoot, "campaigns"));
        var imageKey = images.Save(campaign.Id, session.Id, "fixture.png", TestMapPng());
        state.Conversations.Single(item => item.Id == session.Id).Lines[0].ImageAttachmentKey = imageKey;
        state.SaveSettings();
        archive.Archive(session.Id, [session.Lines[0]]);
        var sourceImagePath = Path.Combine(store.DataRoot, "campaigns", campaign.Id.ToString("N"), "sessions", session.Id.ToString("N"), "chronicle-images", imageKey);
        var blockedCopyId = Guid.NewGuid();
        using (var lockedImage = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { state.DuplicateCampaign(campaign.Id, blockedCopyId); throw new InvalidOperationException("Expected image-copy failure"); }
            catch (IOException) { }
        }
        Require(state.Campaigns.All(item => item.Id != blockedCopyId) && state.Conversations.All(item => item.CampaignId != blockedCopyId) &&
            store.Load().Campaigns!.All(item => item.Id != blockedCopyId),
            "failed image copy cannot leave a duplicate campaign or Session in memory or on disk");
        failSave = true;
        try { state.DuplicateCampaign(campaign.Id, copyId); throw new InvalidOperationException("Expected duplicate failure"); }
        catch (IOException) { }
        Require(state.Campaigns.All(item => item.Id != copyId) && state.Conversations.All(item => item.CampaignId != copyId) &&
            store.Load().Campaigns!.All(item => item.Id != copyId) &&
            (!Directory.Exists(Path.Combine(store.DataRoot, "campaigns", copyId.ToString("N"))) ||
             !Directory.EnumerateFiles(Path.Combine(store.DataRoot, "campaigns", copyId.ToString("N")), "*", SearchOption.AllDirectories).Any()),
            "failed duplicate snapshot restores state and removes copied assets");
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "import-copy", "archive.db")}"))
        {
            connection.Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM archived_lines WHERE conversation_id <> $source;";
            count.Parameters.AddWithValue("$source", session.Id.ToString("N"));
            Require(Convert.ToInt64(count.ExecuteScalar()) == 0, "failed duplicate removes copied archive rows");
        }
        failSave = false;
        state.DuplicateCampaign(campaign.Id, copyId);
        Require(state.Campaigns.Any(item => item.Id == copyId) && state.Conversations.Any(item => item.CampaignId == copyId),
            "duplicate succeeds after failed copy is rolled back");

        var overlap = state.Conversations.Single(item => item.Id == session.Id);
        var distinct = new ChatLine { Sequence = 10, Role = "user", Content = session.Lines[0].Content };
        overlap.Lines.Add(distinct);
        state.SaveSettings();
        var merged = state.GetCompleteChronicleLines(overlap);
        Require(merged.Count(line => line.Sequence == 1) == 1 && merged.Any(line => line.Sequence == 10),
            "archive/active overlap is reconciled by sequence without collapsing distinct same-text messages");
        var reloaded = NewRepairState(new ConversationStore(Path.Combine(root, "import-copy")), Path.Combine(root, "import-copy", "archive.db"));
        var reloadedSession = reloaded.Conversations.Single(item => item.Id == session.Id);
        Require(reloaded.GetCompleteChronicleLines(reloadedSession).Count(line => line.Sequence == 1) == 1,
            "archive/active overlap remains reconciled after reload");
        archive.Archive(session.Id, [session.Lines[0]]);
        Require(archive.GetArchivedLines(session.Id).Count(line => line.Sequence == 1) == 1,
            "subsequent archive compaction remains idempotent for the overlapping sequence");

        var portraitRoot = Path.Combine(root, "portraits");
        var portraits = Isolate(new NpcPortraitStore(), "_root", portraitRoot);
        var npcId = Guid.NewGuid();
        var oldImage = Path.Combine(root, "old.png");
        var newImage = Path.Combine(root, "new.jpg");
        File.WriteAllBytes(oldImage, [1, 2, 3]);
        File.WriteAllBytes(newImage, [4, 5, 6]);
        portraits.Save(campaign.Id, npcId, oldImage);
        state.SaveNpc(campaign.Id, "Portrait fixture", "Fixture", createOnly: true, npcId: npcId);
        state.SetNpcPortraitExtension(campaign.Id, npcId, ".png");
        failSave = true;
        try { portraits.ReplaceNpcPortrait(campaign.Id, npcId, newImage, value => state.SetNpcPortraitExtension(campaign.Id, npcId, value)); throw new InvalidOperationException("Expected portrait failure"); }
        catch (IOException) { }
        failSave = false;
        Require(portraits.ReadDataUrl(campaign.Id, npcId, ".png") is not null && portraits.ReadDataUrl(campaign.Id, npcId, ".jpg") is null,
            "failed NPC portrait metadata save restores the old image and removes the replacement");
        Require(state.Campaigns.Single(item => item.Id == campaign.Id).Npcs.Single(item => item.Id == npcId).PortraitExtension == ".png" &&
            store.Load().Campaigns!.Single(item => item.Id == campaign.Id).Npcs.Single(item => item.Id == npcId).PortraitExtension == ".png",
            "failed NPC portrait replacement restores both in-memory and saved metadata");

        var workbenchRoot = Path.Combine(root, "workbench-cleanup");
        var saveCount = 0;
        var failCleanupBarrier = false;
        var workbenchStore = new ConversationStore(workbenchRoot, () => { if (failCleanupBarrier && ++saveCount == 2) throw new IOException("Injected cleanup barrier failure"); });
        var workbenchCampaign = new Campaign();
        var parent = new Conversation { CampaignId = workbenchCampaign.Id, SessionNumber = 0 };
        workbenchStore.Save(new SavedState("local", "fixture", "", null, [parent], [workbenchCampaign], workbenchCampaign.Id));
        var workbenchState = NewRepairState(workbenchStore, Path.Combine(workbenchRoot, "archive.db"));
        var workbench = workbenchState.CreateWorkbench(parent.Id, "Temporary")!;
        failCleanupBarrier = true;
        var returned = workbenchState.EndWorkbench(workbench.Id, ["Retained fact"]);
        Require(returned?.Id == parent.Id && !workbenchState.Conversations.Any(item => item.Id == workbench.Id) &&
            workbenchStore.Load().PendingDataCleanup?.Any(item => item.SessionIds.Contains(workbench.Id)) == true,
            "durably completed Workbench returns success even when later cleanup is deferred");
        failCleanupBarrier = false;
        _ = NewRepairState(new ConversationStore(workbenchRoot), Path.Combine(workbenchRoot, "archive.db"));
        Require(new ConversationStore(workbenchRoot).Load().PendingDataCleanup?.Count == 0,
            "Workbench archive/image cleanup resumes after restart");

        var playerRoot = Path.Combine(root, "approved-player-bookkeeping");
        var rejectBookkeeping = false;
        var playerStore = new ConversationStore(playerRoot, () => { if (rejectBookkeeping) { rejectBookkeeping = false; throw new IOException("Injected artifact-label save failure"); } });
        playerStore.Save(new SavedState("local", "fixture", "", null, [new Conversation { CampaignId = workbenchCampaign.Id, SessionNumber = 0 }], [new Campaign { Id = workbenchCampaign.Id }], workbenchCampaign.Id));
        var playerState = NewRepairState(playerStore, Path.Combine(playerRoot, "archive.db"));
        var playerWorkbench = playerState.CreateWorkbench(playerState.Current!.Id, "Approval")!;
        var approval = playerState.RequestPlayerSheetUpdate(workbenchCampaign.Id, "old", "new");
        var pending = playerState.PendingPlayerUpdate!;
        var applied = false;
        rejectBookkeeping = true;
        Require(playerState.ApplyPlayerSheetUpdate(pending, () => applied = true) && applied &&
            (await approval).Contains("approved", StringComparison.OrdinalIgnoreCase) && playerState.PendingPlayerUpdate is null &&
            playerState.Current?.Id == playerWorkbench.Id,
            "durably applied Player Sheet approval resolves despite later Workbench label failure");

        var damagedRoot = Path.Combine(root, "damaged-snapshot");
        var damagedStore = new ConversationStore(damagedRoot);
        damagedStore.Save(new SavedState("local", "valid", "", null, [session], [campaign], campaign.Id));
        damagedStore.Save(new SavedState("local", "newer", "", null, [session], [campaign], campaign.Id));
        var primaryPath = Path.Combine(damagedStore.DataRoot, "state.json");
        File.WriteAllText(primaryPath, "{\"Conversations\":[null],\"Campaigns\":[{\"Name\":\"Broken\"}]}");
        Require(new ConversationStore(damagedRoot).Load().Model == "valid", "structurally invalid JSON primary recovers the valid previous snapshot");
        var archivePath = Path.Combine(damagedRoot, "archive.db");
        File.WriteAllText(archivePath, "not a SQLite database");
        var snapshotBytes = File.ReadAllBytes(primaryPath);
        try { _ = new ChronicleArchiveStore(archivePath); throw new InvalidOperationException("Expected damaged archive rejection"); }
        catch (InvalidDataException) { }
        Require(File.ReadAllBytes(primaryPath).SequenceEqual(snapshotBytes), "damaged archive fails clearly without changing campaign snapshots");

        await Task.CompletedTask;
    }
}
