using DBLC;
using System.IO;
using System.Net.Http;
using System.Text.Json;

internal static partial class Checks
{
    private static async Task SessionEndSummaryChecks(string root, string temporary)
    {
        var generated = await LlamaClient.TryGenerateSessionSummaryForEndAsync(() => Task.FromResult("A useful recap."));
        Require(generated == "A useful recap.", "End Session retains a successfully generated recap");
        Require(await LlamaClient.TryGenerateSessionSummaryForEndAsync(() => Task.FromResult("  \r\n")) is null,
            "an empty recap is treated as unavailable for End Session");
        Require(await LlamaClient.TryGenerateSessionSummaryForEndAsync(() => throw new HttpRequestException("model unavailable")) is null,
            "a recap inference failure is treated as unavailable for End Session");

        static (ConversationStore Store, AppState State, Conversation Session) Create(string path, Action? beforeWrite = null)
        {
            var store = new ConversationStore(path, beforeWrite);
            var campaign = new Campaign { Name = "Summary lifecycle" };
            var session = new Conversation { CampaignId = campaign.Id, SessionNumber = 0, Title = "Session 0", Lines = [new ChatLine { Role = "user", Content = "A test turn." }] };
            campaign.SessionOrder.Add(session.Id);
            store.Save(new SavedState("local", "fixture", "", null, [session], [campaign], campaign.Id));
            return (store, NewRepairState(store, Path.Combine(path, "archive.db")), session);
        }

        var success = Create(Path.Combine(temporary, "end-summary-success"));
        success.State.SaveSessionSummary(success.Session, generated!);
        var successNext = success.State.EndSessionAndStartNext(success.Session);
        Require(success.Session.IsEnded && success.Session.SessionSummary == "A useful recap."
            && successNext.OpeningSummary == "A useful recap.", "successful End Session recap is saved before closure and carried into the next Session");

        var retained = Create(Path.Combine(temporary, "end-summary-retained"));
        retained.State.SaveSessionSummary(retained.Session, "Previously saved recap.");
        var unavailable = await LlamaClient.TryGenerateSessionSummaryForEndAsync(() => throw new HttpRequestException("model unavailable"));
        if (!string.IsNullOrWhiteSpace(unavailable)) retained.State.SaveSessionSummary(retained.Session, unavailable);
        var retainedNext = retained.State.EndSessionAndStartNext(retained.Session);
        Require(retainedNext.OpeningSummary == "Previously saved recap.", "failed recap generation leaves the previously saved summary for the next Session");

        var empty = Create(Path.Combine(temporary, "end-summary-empty"));
        var noNewRecap = await LlamaClient.TryGenerateSessionSummaryForEndAsync(() => Task.FromResult(" \t "));
        if (!string.IsNullOrWhiteSpace(noNewRecap)) empty.State.SaveSessionSummary(empty.Session, noNewRecap);
        var emptyNext = empty.State.EndSessionAndStartNext(empty.Session);
        Require(empty.Session.IsEnded && string.IsNullOrEmpty(emptyNext.OpeningSummary), "empty recap does not block ending and the next Session opens without a summary");

        var (continuity, continuityStore) = AuditState(temporary, "session-ending-context", workbench: false);
        var previous = continuity.Current!;
        previous.Lines.AddRange([
            new ChatLine { Role = "user", Sequence = 1, Content = "I am ready to enter the church." },
            new ChatLine { Role = "assistant", Sequence = 2, Content = "You are outside the church gate. The door is still sealed." }
        ]);
        previous.NextSequence = 3;
        var next = continuity.EndSessionAndStartNext(previous);
        var background = continuity.GetContextHistory(next).Single().Content;
        Require(background.Contains("outside the church gate") && background.Contains("ready to enter")
            && background.IndexOf("ready to enter", StringComparison.Ordinal) < background.IndexOf("outside the church gate", StringComparison.Ordinal),
            "missing recap supplies the previous Session ending in chronological order");
        Require(next.Lines.Count == 0 && next.OpeningSummary == "" && continuity.GetArchivedLines(next.Id).Count == 0,
            "ending excerpt is request background, not a fabricated recap, Chronicle turn or archive entry");
        var reloaded = NewRepairState(continuityStore, Path.Combine(temporary, "continuity-reload-archive.db"));
        Require(reloaded.GetContextHistory(reloaded.Conversations.Single(item => item.Id == next.Id)).Single().Content == background,
            "existing saved Session linkage reconstructs ending context after reload without new persisted state");
        using (var server = new ModelStub((_, _) => Text("You remain outside the church.")))
        {
            continuity.Endpoint = server.Endpoint;
            await continuity.SendAsync("Where are we?", new LlamaClient());
            Require(server.Requests[0]["messages"]!.ToJsonString().Contains("outside the church gate"),
                "actual initial model request receives previous ending before answering location question");
            Require(server.Requests.Count(request => request["stream"]?.ToString() == "true") == 1,
                "ending context requires no additional inference pass");
        }
        next.OpeningSummary = "A saved recap.";
        Require(!continuity.GetContextHistory(next).Any(line => line.Content == background),
            "Sessions with a recap keep the existing context path without extra ending text");
        next.OpeningSummary = "";
        next.IsWorkbench = true;
        Require(!continuity.GetContextHistory(next).Any(line => line.Content == background),
            "Workbench context is unchanged");
        next.IsWorkbench = false;
        var originalCampaignId = previous.CampaignId;
        previous.CampaignId = Guid.NewGuid();
        Require(!continuity.GetContextHistory(next).Any(line => line.Content == background),
            "ending excerpts cannot cross campaign boundaries");
        previous.CampaignId = originalCampaignId;
        previous.Lines.Add(new ChatLine { Role = "assistant", Sequence = 3, Content = new string('x', 10000) + "Latest location." });
        var bounded = continuity.GetContextHistory(next)[0].Content;
        Require(bounded.Length < 4400 && bounded.EndsWith("Latest location.", StringComparison.Ordinal),
            "large endings have a small fixed budget and retain the newest text");
        previous.Lines.Add(new ChatLine { Role = "assistant", Sequence = 4, EntryType = ChatLine.TechnicalErrorEntryType, Content = "Connection failed." });
        Require(!continuity.GetContextHistory(next)[0].Content.Contains("Connection failed."),
            "technical errors are not supplied as prior scene evidence");
        continuity.Conversations.Remove(previous);
        Require(!continuity.GetContextHistory(next).Any(line => line.Content.StartsWith("Previous Session", StringComparison.Ordinal)),
            "deleted prior Session leaves no invented ending context");

        var failSave = false;
        var persistence = Create(Path.Combine(temporary, "end-summary-save-failure"), () =>
        {
            if (failSave) throw new IOException("Injected summary persistence failure");
        });
        failSave = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(generated)) persistence.State.SaveSessionSummary(persistence.Session, generated);
            persistence.State.EndSessionAndStartNext(persistence.Session);
            throw new InvalidOperationException("Expected recap persistence failure");
        }
        catch (IOException ex) when (ex.Message.Contains("Injected", StringComparison.Ordinal)) { }
        Require(!persistence.Session.IsEnded && persistence.State.Current?.Id == persistence.Session.Id,
            "failed recap persistence stops before End Session and leaves the active Session selected");

        var failEndSave = false;
        var endPersistence = Create(Path.Combine(temporary, "end-session-save-failure"), () =>
        {
            if (failEndSave) throw new IOException("Injected Session-end persistence failure");
        });
        var beforeEnd = JsonSerializer.Serialize(new { endPersistence.State.Conversations, endPersistence.State.Campaigns, Current = endPersistence.State.Current?.Id });
        failEndSave = true;
        try { endPersistence.State.EndSessionAndStartNext(endPersistence.Session); throw new InvalidOperationException("Expected Session-end persistence failure"); }
        catch (IOException ex) when (ex.Message.Contains("Injected", StringComparison.Ordinal)) { }
        Require(JsonSerializer.Serialize(new { endPersistence.State.Conversations, endPersistence.State.Campaigns, Current = endPersistence.State.Current?.Id }) == beforeEnd
            && !endPersistence.Session.IsEnded && endPersistence.State.Current?.Id == endPersistence.Session.Id,
            "failed Session-end persistence rolls back the new Session, ordering and in-memory closure state");

        var desktop = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        var mobile = File.ReadAllText(Path.Combine(root, "src", "DBLC", "MobileShell.razor"));
        foreach (var source in new[] { desktop, mobile })
        {
            Require(source.Contains("LlamaClient.TryGenerateSessionSummaryForEndAsync", StringComparison.Ordinal)
                && source.Contains("if (!string.IsNullOrWhiteSpace(summary)) State.SaveSessionSummary(session, summary);", StringComparison.Ordinal)
                && source.IndexOf("State.SaveSessionSummary(session, summary)", StringComparison.Ordinal) < source.IndexOf("State.EndSessionAndStartNext(session)", StringComparison.Ordinal),
                "desktop and mobile save only available recap text before attempting Session closure");
            Require(source.Contains("else if (!endAfterSummary) throw new InvalidOperationException(\"The model returned an empty session summary.\")", StringComparison.Ordinal)
                && source.Contains("Could not summarize session:", StringComparison.Ordinal)
                || source.Contains("Could not \" + (endAfterSummary ? \"end\" : \"summarize\")", StringComparison.Ordinal),
                "standalone Summarize Session continues to report generation failure");
        }
    }
}
