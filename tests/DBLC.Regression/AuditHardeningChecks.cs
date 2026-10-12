using DBLC;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class Checks
{
    private static (AppState State, ConversationStore Store) AuditState(string temporary, string name, bool workbench = true)
    {
        var folder = Path.Combine(temporary, "audit-hardening", name);
        var campaign = new Campaign { Name = "Synthetic audit campaign" };
        var conversation = new Conversation { CampaignId = campaign.Id, IsWorkbench = workbench, Title = "Audit session" };
        var parent = new Conversation { CampaignId = campaign.Id, Title = "Parent session" };
        if (workbench) conversation.ParentSessionId = parent.Id;
        var store = new ConversationStore(Path.Combine(folder, "state"));
        store.Save(new SavedState("local", "fixture", "", null, workbench ? [parent, conversation] : [conversation], [campaign], campaign.Id, CharacterSpeakersEnabled: true));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(folder, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(folder, "campaigns")));
        state.Select(state.Conversations.Single(item => item.Id == conversation.Id));
        return (state, store);
    }

    private static async Task AuditHardeningChecks(string root, string temporary)
    {
        await SubmissionAdmissionChecks(root, temporary);
        await ApprovalNarrationChecks(temporary);
        await SharedWorkbenchCreationChecks(root, temporary);
        await WorkbenchExitPathChecks(root, temporary);
        await TurnCancellationChecks(temporary);
        await SpeakerCompletionChecks(temporary);
        await RecentClockToolContextChecks(temporary);
        await TransientGmContextChecks(temporary);
    }

    private static async Task TransientGmContextChecks(string temporary)
    {
        var (state, _) = AuditState(temporary, "transient-gm-context", workbench: false);
        state.Campaigns[0].Npcs.Add(new NpcCard { Name = "Cletus", PortrayalCue = "Broad drawl." });
        state.Campaigns[0].SuccessfulGmExchangesSinceTimeReminder = 6;
        using var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("read_npc", "{\"name\":\"Cletus\"}"))
            : Text("Cletus nods."));
        state.Endpoint = server.Endpoint;
        await state.SendAsync("I speak with Cletus.", new LlamaClient());
        Require(server.Paths.Count(path => path.EndsWith("/chat/completions", StringComparison.Ordinal)) == 2,
            "NPC portrayal and the time reminder do not add an inference pass");
        var initial = (JsonArray)server.Requests[0]["messages"]!;
        var continuation = (JsonArray)server.Requests[1]["messages"]!;
        var initialSystem = (string?)initial[0]?["content"] ?? "";
        var continuationSystem = (string?)continuation[0]?["content"] ?? "";
        var initialUser = initial.OfType<JsonObject>().Last(message => (string?)message["role"] == "user");
        var continuationUser = continuation.OfType<JsonObject>().Last(message => (string?)message["role"] == "user");
        var context = (string?)initialUser["content"] ?? "";
        Require(initialSystem == continuationSystem && !initialSystem.Contains("Broad drawl.", StringComparison.Ordinal)
            && !initialSystem.Contains("CAMPAIGN TIME REMINDER", StringComparison.Ordinal)
            && context.Contains("I speak with Cletus.", StringComparison.Ordinal)
            && context.Contains("Cletus:\nBroad drawl.", StringComparison.Ordinal)
            && context.Contains("CAMPAIGN TIME REMINDER", StringComparison.Ordinal)
            && context.Contains("CURRENT FICTIONAL CAMPAIGN TIME:", StringComparison.Ordinal)
            && (string?)continuationUser["content"] == context,
            "the actual GM turn and tool continuation retain the same portrayal, reminder, and authoritative time at the request tail");
        var initialTools = (JsonArray)server.Requests[0]["tools"]!;
        var continuationTools = (JsonArray)server.Requests[1]["tools"]!;
        var expectedTools = (JsonArray)typeof(LlamaClient).GetMethod("BuildTools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [false])!;
        var expectedNames = expectedTools.OfType<JsonObject>().Select(tool => (string?)tool["function"]?["name"]).ToHashSet(StringComparer.Ordinal);
        var initialNames = initialTools.OfType<JsonObject>().Select(tool => (string?)tool["function"]?["name"]).ToHashSet(StringComparer.Ordinal);
        Require(expectedNames.SetEquals(initialNames) && initialTools.ToJsonString() == continuationTools.ToJsonString()
            && continuation.OfType<JsonObject>().Any(message => (string?)message["role"] == "tool"),
            "the full direct tool set remains available on both inference passes");
    }

    private static async Task RecentClockToolContextChecks(string temporary)
    {
        var (state, store) = AuditState(temporary, "recent-clock-tool", workbench: false);
        var conversation = state.Current!;
        using (var first = new ModelStub((_, pass) => pass == 0
            ? Calls(("advance_time", "{\"amount\":20,\"unit\":\"minutes\"}"))
            : Text("Twenty minutes pass.")))
        {
            state.Endpoint = first.Endpoint;
            await state.SendAsync("We wait twenty minutes.", new LlamaClient());
            var initialMessages = (JsonArray)first.Requests[0]["messages"]!;
            Require(!((string?)initialMessages[0]?["content"] ?? "").Contains("CURRENT FICTIONAL CAMPAIGN TIME:\n", StringComparison.Ordinal)
                && ((string?)initialMessages[^1]?["content"] ?? "").Contains("CURRENT FICTIONAL CAMPAIGN TIME:\n", StringComparison.Ordinal),
                "the changing clock snapshot is sent at the request tail, outside the stable system prefix");
        }
        var stored = store.Load().Conversations.Single(item => item.Id == conversation.Id).RecentToolExchange;
        Require(stored is { Name: "advance_time" } && stored.Result.StartsWith("Time advanced:\n", StringComparison.Ordinal)
            && stored.ReplyId == conversation.Lines.Last(line => line.Role == "assistant").Id,
            "one successful clock tool exchange is retained with the completed GM reply across save/reload");
        using (var next = new ModelStub((_, _) => Text("The road is quiet.")))
        {
            state.Endpoint = next.Endpoint;
            await state.SendAsync("We continue.", new LlamaClient());
            var messages = (JsonArray)next.Requests[0]["messages"]!;
            Require(((string?)messages[^1]?["content"] ?? "").Contains(state.GetCampaignGmMoment(conversation.CampaignId), StringComparison.Ordinal),
                "the next request receives the current authoritative clock after the actual time-advance tool");
            var tool = messages.OfType<JsonObject>().Single(message => (string?)message["role"] == "tool");
            var toolIndex = messages.IndexOf(tool);
            Require((string?)tool["tool_call_id"] == stored!.CallId && (string?)tool["content"] == stored.Result
                && (string?)messages[toolIndex - 1]?["role"] == "assistant"
                && (string?)messages[toolIndex - 1]?["tool_calls"]?[0]?["function"]?["name"] == "advance_time"
                && (string?)messages[toolIndex + 1]?["role"] == "assistant",
                "the prior clock call and exact result appear once before their original completed response");
            Require(next.Requests[0]["tools"] is JsonArray,
                "the recent exchange preserves normal direct tool availability");
        }
        Require(conversation.RecentToolExchange is null && store.Load().Conversations.Single(item => item.Id == conversation.Id).RecentToolExchange is null,
            "a later turn without an eligible clock call clears the temporary example");
        Require(conversation.Lines.All(line => !line.Content.Contains("Time advanced:", StringComparison.Ordinal)),
            "the tool result is private model context and does not become Chronicle narration");

        var (invalidState, _) = AuditState(temporary, "rejected-clock-tool", workbench: false);
        using (var invalid = new ModelStub((_, pass) => pass == 0
            ? Calls(("advance_time", "{\"amount\":0,\"unit\":\"minutes\"}"))
            : Text("No time passed.")))
        {
            invalidState.Endpoint = invalid.Endpoint;
            await invalidState.SendAsync("Wait zero minutes.", new LlamaClient());
        }
        Require(invalidState.Current!.RecentToolExchange is null,
            "a rejected time operation does not become a successful tool example");
    }

    private static async Task SubmissionAdmissionChecks(string root, string temporary)
    {
        var (state, _) = AuditState(temporary, "admission");
        var source = state.Current!;
        var draft = "Keep this desktop draft.";
        var discovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? rejected = null;
        async Task DesktopPreflight()
        {
            var intended = state.Current!.Id;
            await discovery.Task;
            await state.SendAsync(draft, new LlamaClient(), expectedConversationId: intended,
                onAccepted: () => draft = "", onRejected: reason => rejected = reason);
        }
        var preflight = DesktopPreflight();
        var other = new Conversation { CampaignId = source.CampaignId, IsWorkbench = true };
        state.Conversations.Add(other); state.Select(other);
        discovery.SetResult(); await preflight;
        Require(draft.Length > 0 && rejected is not null && source.Lines.Count == 0 && other.Lines.Count == 0,
            "finding 1: session switch during discovery rejects explicitly without redirecting or clearing the draft");

        state.Select(source);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var review = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new ModelStub((_, pass) => pass == 0 ? Calls(("request_end_workbench", "{}")) : Text("Finished."));
        state.Endpoint = server.Endpoint;
        var mobile = state.SendAsync("Mobile wins admission.", new LlamaClient(), requestWorkbenchEnd: _ => { entered.SetResult(); return review.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await state.SendAsync(draft, new LlamaClient(), expectedConversationId: source.Id,
            onAccepted: () => draft = "", onRejected: reason => rejected = reason);
        Require(draft.Length > 0 && rejected!.Contains("active") && source.Lines.Count(line => line.Role == "user") == 1,
            "finding 1: concurrent mobile/desktop admission preserves the rejected desktop draft and records only one input");
        review.SetResult("Declined."); await mobile;
        var desktopSource = File.ReadAllText(Path.Combine(root, "src/DBLC/AppShell.razor"));
        var sendForm = desktopSource[desktopSource.IndexOf("private async Task SendPrompt(")..];
        Require(sendForm.IndexOf("var sourceConversation = State.Current") < sendForm.IndexOf("await RefreshModelId()")
            && sendForm.Contains("expectedConversationId: sourceConversation.Id") && sendForm.Contains("onAccepted: () => { accepted = true; if (fromComposer) { Draft = \"\""),
            "finding 1: actual desktop preflight captures the target before discovery and clears only on shared acceptance");

        var mobileSource = File.ReadAllText(Path.Combine(root, "src/DBLC/MobileShell.razor"));
        var mobileSubmit = mobileSource[mobileSource.IndexOf("private async Task SubmitCore(")..];
        Require(sendForm.Contains("await RefreshModelId()") && sendForm.Contains("State.SendAsync(prompt, Client, ServerModelId, ServerContextLimit")
            && mobileSubmit.Contains("await ModelInfo.GetModelIdAsync(State.Endpoint)")
            && mobileSubmit.Contains("State.SendAsync(text, Client, resolvedModelId,") ,
            "ordinary desktop and mobile GM turns resolve through the same model-info client and pass the discovered ID");

        var (routingState, _) = AuditState(temporary, "resolved-model-routing");
        routingState.Model = "The GM";
        using var routingServer = new ModelStub((_, _) => Text("The response uses the discovered model."));
        routingState.Endpoint = routingServer.Endpoint;
        const string discoveredModelId = "bonsai-runtime-model-id";
        await routingState.SendAsync("Continue the scene.", new LlamaClient(), discoveredModelId);
        Require((string?)routingServer.Requests.Single()["model"] == discoveredModelId
            && routingState.Model == "The GM",
            "resolved model override is sent to llama.cpp without replacing the saved display/model setting");
    }

    private static async Task WorkbenchExitPathChecks(string root, string temporary)
    {
        var desktop = File.ReadAllText(Path.Combine(root, "src/DBLC/AppShell.razor"));
        var mobile = File.ReadAllText(Path.Combine(root, "src/DBLC/MobileShell.razor"));
        var client = File.ReadAllText(Path.Combine(root, "src/DBLC/Services.cs"));
        const string internalReview = "[DBLC: The player requested Review & End. Review the Workbench and use request_end_workbench.]";
        Require(desktop.Contains("await SendPrompt(\"\", fromComposer: false, internalInstruction: \"[DBLC: The player requested Review & End.")
            && mobile.Contains("await SubmitCore(\"\", fromComposer: false, internalInstruction: \"[DBLC: The player requested Review & End.")
            && !desktop.Contains("ProposeWorkbenchRetentionAsync") && !mobile.Contains("ProposeWorkbenchRetentionAsync")
            && !client.Contains("ProposeWorkbenchRetentionAsync"),
            "Review & End on both clients uses an internal instruction in the normal submission path");
        Require(desktop.Contains("if (fromComposer) { Draft = \"\"; RemoveComposerImage(); }")
            && mobile.Contains("if (fromComposer) Draft = \"\";"),
            "button-triggered exit request preserves an unsent composer draft and image");
        Require(desktop.Contains("private void DiscardWorkbenchNow()") && mobile.Contains("private void DiscardMobileWorkbenchNow()"),
            "Discard & Exit retains its direct non-model path");

        {
            var (state, _) = AuditState(temporary, "internal-workbench-review");
            var workbench = state.Current!;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var decision = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var server = new ModelStub((_, pass) => pass == 0
                ? Calls(("request_end_workbench", "{}"))
                : Text("The Workbench remains open."));
            state.Endpoint = server.Endpoint;
            async Task<string> Review(WorkbenchExitRetention _)
            {
                entered.TrySetResult();
                return await decision.Task;
            }
            var turn = state.SendAsync("", new LlamaClient(), requestWorkbenchEnd: Review,
                expectedConversationId: workbench.Id, internalInstruction: internalReview);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var requestedMessages = server.Requests[0]["messages"]!.AsArray();
            Require(((string?)requestedMessages.Last(message => (string?)message?["role"] == "user")?["content"])?.StartsWith(internalReview + "\n\n[DBLC CURRENT CONTEXT", StringComparison.Ordinal) == true,
                "Review & End internal instruction reaches the ordinary GM request");
            Require(workbench.Lines.All(line => line.Role != "user")
                && workbench.Lines.All(line => !line.Content.Contains("Review & End", StringComparison.Ordinal)),
                "Review & End instruction is not persisted as player dialogue or Chronicle text");
            decision.SetResult("Review cancelled.");
            await turn;
            Require(server.Requests.Count == 2 && workbench.Lines.All(line => line.Role != "user"),
                "internal Review & End keeps the existing single tool continuation without a player line");
        }

        foreach (var approve in new[] { false, true })
        {
            var (state, _) = AuditState(temporary, "workbench-exit-" + approve);
            var workbench = state.Current!;
            workbench.WorkbenchRetainedArtifacts.Add("NPC portrait updated");
            using var server = new ModelStub((_, pass) => pass == 0
                ? Calls(("request_end_workbench", "{\"notes\":[\"Retain this ruling\"],\"chronicle_facts\":[\"The gate is sealed\"]}"))
                : Text("The GM acknowledges the decision."));
            state.Endpoint = server.Endpoint;
            async Task<string> Review(WorkbenchExitRetention retention)
            {
                Require(state.TryBeginWorkbenchExitReview(workbench.Id, out var reservation), "normal tool reserves shared review");
                Require(state.PublishWorkbenchExitReview(reservation, retention, out var decision) && decision is not null,
                    "normal tool publishes a review that awaits its continuation");
                return await decision!;
            }
            var turn = state.SendAsync("Please review this Workbench, propose anything worth retaining, and request to end it.",
                new LlamaClient(), requestWorkbenchEnd: Review, expectedConversationId: workbench.Id);
            for (var attempt = 0; attempt < 100 && state.PendingWorkbenchExitReview is null; attempt++)
                await Task.Delay(10);
            Require(state.PendingWorkbenchExitReview?.Retention.Notes.SequenceEqual(["Retain this ruling"]) == true,
                "normal Workbench tool produced the review without a second inference");
            var system = (string)server.Requests[0]["messages"]![0]!["content"]!;
            Require(system.Contains("NPC portrait updated") && system.Contains("Artifacts already retained from this Workbench"),
                "normal Workbench prompt includes small retained-artifact labels");
            Require(server.Requests[0]["tools"]!.ToJsonString().Contains("request_end_workbench"),
                "normal Workbench turn exposes the exit tool");
            state.ResolveWorkbenchExitReview(approve ? WorkbenchExitReviewAction.Approve : WorkbenchExitReviewAction.Cancel);
            await turn;
            Require(server.Requests.Count == 2, "review decision resumes one ordinary tool continuation with no retention request");
            Require(approve ? state.Current?.Id != workbench.Id : state.Current?.Id == workbench.Id,
                "approval closes after the GM turn; cancellation keeps the Workbench open");
        }
    }

    private static async Task ApprovalNarrationChecks(string temporary)
    {
        foreach (var tool in new[] { "propose_player_update", "create_bestiary", "propose_bestiary_update", "propose_gm_note" })
        foreach (var approve in new[] { false, true })
        {
            var (state, store) = AuditState(temporary, tool + approve);
            var conversation = state.Current!;
            if (tool == "propose_bestiary_update") state.Campaigns[0].Bestiary.Add(new BestiaryEntry { Name = "Test creature", StatBlock = "Old" });
            var args = tool switch
            {
                "propose_player_update" => "{\"markdown\":\"Updated sheet\"}",
                "propose_gm_note" => "{\"text\":\"Test retained note\"}",
                _ => "{\"name\":\"Test creature\",\"stat_block\":\"New\",\"notes\":\"Test notes\"}"
            };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Changed += () => { if (state.PendingPlayerUpdate is not null || state.PendingBestiaryProposal is not null || state.PendingGmNote is not null) entered.TrySetResult(); };
            using var server = new ModelStub((_, pass) => pass == 0
                ? Delta(new { content = "Before review. " }) + Calls((tool, args))
                : Calls(("present_response", "{\"segments\":[{\"speaker\":\"GM\",\"text\":\"After review.\"}]}")));
            state.Endpoint = server.Endpoint;
            var turn = state.SendAsync("Prepare the change.", new LlamaClient(), readPlayer: _ => "Original sheet");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var liveId = conversation.Lines[^1].PresentationSegments!.Single().Id;
            Require(conversation.Lines[^1].Content == "Before review. " && !turn.IsCompleted, $"finding 2: {tool}/{approve} preamble is live during approval");
            switch (tool)
            {
                case "propose_player_update":
                    if (approve) Require(state.ApplyPlayerSheetUpdate(state.PendingPlayerUpdate!, () => { }), "player approval is applied once");
                    else state.ResolvePlayerSheetUpdate("Rejected.");
                    break;
                case "propose_gm_note": if (approve) state.ApproveGmNoteProposal(); else state.CancelGmNoteProposal(); break;
                default: if (approve) state.ApproveBestiaryProposal(); else state.CancelBestiaryProposal(); break;
            }
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            var reply = conversation.Lines.Single(line => line.Role == "assistant");
            Require(string.Concat(reply.PresentationSegments!.Select(segment => segment.Text)) == "Before review. After review."
                && reply.PresentationSegments![0].Id == liveId, $"finding 2: {tool}/{approve} retains narration once and keeps its stable segment ID");
            var reload = store.Load().Conversations.Single(item => item.Id == conversation.Id).Lines.Single(line => line.Role == "assistant");
            Require(reload.Content == reply.Content && reload.PresentationSegments![0].Id == liveId,
                $"finding 2: {tool}/{approve} completed narration survives persistence and reload");
        }
    }

    private static async Task SharedWorkbenchCreationChecks(string root, string temporary)
    {
        foreach (var accepted in new[] { true, false })
        {
            var (state, _) = AuditState(temporary, "workbench-create" + accepted, workbench: false);
            var parent = state.Current!;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action initiatingObserver = () => { if (state.PendingWorkbenchCreation is not null) entered.TrySetResult(); };
            state.Changed += initiatingObserver;
            using var server = new ModelStub((_, pass) => pass == 0 ? Calls(("request_workbench", "{\"name\":\"Test workbench\"}")) : Text("Decision received."));
            state.Endpoint = server.Endpoint;
            var turn = state.SendAsync("Request a workbench.", new LlamaClient());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var request = state.PendingWorkbenchCreation!;
            state.Changed -= initiatingObserver; // The initiating UI is disposed/refreshed.
            var reconnected = state.PendingWorkbenchCreation;
            Require(ReferenceEquals(request, reconnected) && !turn.IsCompleted, "finding 3: reconnect observes the same shared pending creation request");
            Require(state.ResolveWorkbenchCreation(reconnected!.Id, accepted) && !state.ResolveWorkbenchCreation(request.Id, accepted),
                "finding 3: reconnected client resolves once; duplicate approval has no effect");
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            Require(state.PendingWorkbenchCreation is null && state.Conversations.Count(item => item.IsWorkbench) == (accepted ? 1 : 0),
                "finding 3: detached initiator does not strand creation or duplicate its artifact");
            Require(accepted ? state.Current!.IsWorkbench : state.Current!.Id == parent.Id,
                "finding 3: accepted creation switches after the turn; rejection keeps the parent session");
        }
        foreach (var shell in new[] { "AppShell.razor", "MobileShell.razor" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src/DBLC", shell));
            Require(source.Contains("State.PendingWorkbenchCreation") && !source.Contains("_gmWorkbenchRequestCompletion"),
                "finding 3: both real UI components render shared approval without owning its waiter");
        }
    }

    private static async Task TurnCancellationChecks(string temporary)
    {
        foreach (var tool in new[] { "propose_player_update", "create_bestiary", "propose_bestiary_update", "propose_gm_note", "request_workbench", "request_end_workbench" })
        {
            Console.WriteLine("Testing Stop during " + tool);
            var (state, store) = AuditState(temporary, "stop-" + tool, workbench: tool != "request_workbench");
            var conversation = state.Current!;
            if (tool == "propose_bestiary_update") state.Campaigns[0].Bestiary.Add(new BestiaryEntry { Name = "Test creature", StatBlock = "Old" });
            var args = tool switch
            {
                "propose_player_update" => "{\"markdown\":\"Changed\"}",
                "propose_gm_note" => "{\"text\":\"Never saved\"}",
                "request_workbench" => "{\"name\":\"Never created\"}",
                "request_end_workbench" => "{\"notes\":[],\"chronicle_facts\":[]}",
                _ => "{\"name\":\"Test creature\",\"stat_block\":\"Changed\"}"
            };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Changed += () => { if (state.PendingPlayerUpdate is not null || state.PendingBestiaryProposal is not null || state.PendingGmNote is not null || state.PendingWorkbenchCreation is not null || state.PendingWorkbenchExitReview is not null) entered.TrySetResult(); };
            using var server = new ModelStub((_, _) => Delta(new { content = "Keep this narration." }) + Calls((tool, args)));
            state.Endpoint = server.Endpoint;
            Task<string> ExitReview(WorkbenchExitRetention retention)
            {
                Require(state.TryBeginWorkbenchExitReview(conversation.Id, out var reservation), "exit reservation acquired");
                Require(state.PublishWorkbenchExitReview(reservation, retention, out var approval), "exit approval published");
                return approval!;
            }
            var turn = state.SendAsync("Prepare approval.", new LlamaClient(), readPlayer: _ => "Original", requestWorkbenchEnd: ExitReview);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stalePlayer = state.PendingPlayerUpdate;
            var staleBestiary = state.PendingBestiaryProposal;
            var staleNote = state.PendingGmNote;
            var staleCreation = state.PendingWorkbenchCreation;
            var staleExit = state.PendingWorkbenchExitReview;
            Require(state.StopCurrentResponse(), $"finding 4: Stop accepted during {tool}");
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            Require(!state.Busy && state.PendingPlayerUpdate is null && state.PendingBestiaryProposal is null && state.PendingGmNote is null
                && state.PendingWorkbenchCreation is null && state.PendingWorkbenchExitReview is null,
                $"finding 4: {tool} waiter and pending review cleared without another inference");
            Require(server.Requests.Count == 1 && conversation.Lines[^1].Content == "Keep this narration."
                && !conversation.Lines.Any(line => line.EntryType == ChatLine.TechnicalErrorEntryType), "Stop preserves existing narration without a spurious connection error");
            var applied = false;
            if (stalePlayer is not null) Require(!state.ApplyPlayerSheetUpdate(stalePlayer, () => applied = true), "stale Player review cannot save");
            if (staleBestiary is not null) { try { state.ApproveBestiaryProposal(staleBestiary); } catch (InvalidOperationException) { } }
            if (staleNote is not null) { try { state.ApproveGmNoteProposal(staleNote); } catch (InvalidOperationException) { } }
            if (staleCreation is not null) Require(!state.ResolveWorkbenchCreation(staleCreation.Id, true), "stale creation approval cannot create");
            if (staleExit is not null) Require(state.ResolveWorkbenchExitReview(WorkbenchExitReviewAction.Approve, staleExit) is null, "stale exit approval cannot close");
            Require(!applied && state.GetGmNotes(conversation.CampaignId).Count == 0 && state.Conversations.Contains(conversation)
                && state.Campaigns[0].Bestiary.All(entry => entry.StatBlock == "Old"), "cancelled approvals cause no durable mutation");
            Require(store.Load().Conversations.First(item => item.Id == conversation.Id).Lines[^1].Content == "Keep this narration.", "stopped narration survives reload");
        }

        var reply = new ChatLine(); var steering = new ResponseSteeringSession(Guid.NewGuid(), reply);
        var calls = 0;
        using (var server = new ModelStub((_, _) => Calls(("read_player", "{}"), ("edit_npc", "{\"name\":\"Test\",\"notes\":\"Changed\"}"))))
        {
            try
            {
                await new LlamaClient().CompleteAsync(server.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Test" }], "", "",
                    () => "", _ => "", _ => "", (_, _, _) => "", _ => "", (_, _) => "", () => "", _ => "", (_, _, _, _) => { calls++; return "saved"; },
                    () => { steering.RequestStop(); return "Read completed"; }, () => "", _ => "", _ => "", _ => { }, _ => { }, (_, _, _) => { }, steering: steering);
            }
            catch (OperationCanceledException) { }
            Require(calls == 0 && server.Requests.Count == 1, "finding 4: Stop after one completed batch operation prevents the next mutating tool");
        }
        var asyncStop = new ResponseSteeringSession(Guid.NewGuid(), new ChatLine());
        var asyncEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asyncWait = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var server = new ModelStub((_, _) => Calls(("request_end_workbench", "{}"))))
        {
            var turn = RunModel(server, new(), steering: asyncStop, onWorkbenchEnd: _ => { asyncEntered.SetResult(); return asyncWait.Task; });
            await asyncEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)); asyncStop.RequestStop();
            try { await turn.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            asyncWait.TrySetResult("Late completion");
            Require(server.Requests.Count == 1 && turn.IsCompleted, "finding 4: Stop releases an asynchronous tool wait; late completion cannot start continuation");
        }

        var (preparingState, _) = AuditState(temporary, "stop-during-exit-preparation");
        var preparationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>? preparingOperation = null;
        Guid cancelledReservation = Guid.Empty;
        using (var server = new ModelStub((_, _) => Calls(("request_end_workbench", "{}"))))
        {
            preparingState.Endpoint = server.Endpoint;
            var turn = preparingState.SendAsync("Prepare the exit.", new LlamaClient(), requestWorkbenchEnd: retention =>
            {
                preparingOperation = Prepare();
                return preparingOperation;
                async Task<string> Prepare()
                {
                    Require(preparingState.TryBeginWorkbenchExitReview(preparingState.Current!.Id, out cancelledReservation), "exit preparation reserved");
                    preparationEntered.SetResult();
                    await preparationFinished.Task;
                    Require(!preparingState.PublishWorkbenchExitReview(cancelledReservation, retention, out _),
                        "finding 4: delayed preparation cannot publish a review after Stop");
                    return "Stopped.";
                }
            });
            await preparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(preparingState.StopCurrentResponse(), "Stop during exit preparation succeeds");
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            preparationFinished.SetResult(); await preparingOperation!;
            Require(!preparingState.WorkbenchExitReviewBusy && preparingState.PendingWorkbenchExitReview is null,
                "finding 4: Stop clears the exit preparation reservation as well as approval waits");
        }

        var (staleState, _) = AuditState(temporary, "stale-review-next-proposal");
        var campaignId = staleState.Current!.CampaignId;
        var oldPlayerWait = staleState.RequestPlayerSheetUpdate(campaignId, "Old", "First");
        var oldPlayer = staleState.PendingPlayerUpdate!;
        staleState.ResolvePlayerSheetUpdate("Cancelled."); await oldPlayerWait;
        var newPlayerWait = staleState.RequestPlayerSheetUpdate(campaignId, "Old", "Second");
        Require(!staleState.ApplyPlayerSheetUpdate(oldPlayer, () => throw new Exception("Stale approval applied"))
            && !newPlayerWait.IsCompleted, "finding 4: stale Player approval cannot apply or resolve a later proposal");
        staleState.ResolvePlayerSheetUpdate("Cancelled."); await newPlayerWait;
        var oldNoteWait = staleState.RequestGmNoteProposal(campaignId, "First note");
        var oldNote = staleState.PendingGmNote!;
        staleState.CancelGmNoteProposal(); await oldNoteWait;
        var newNoteWait = staleState.RequestGmNoteProposal(campaignId, "Second note");
        try { staleState.ApproveGmNoteProposal(oldNote); throw new Exception("Stale note approved"); } catch (InvalidOperationException) { }
        Require(!newNoteWait.IsCompleted && staleState.GetGmNotes(campaignId).Count == 0,
            "finding 4: stale note approval cannot save a newer note");
        staleState.CancelGmNoteProposal(); await newNoteWait;
        var bestiary = staleState.GetBestiaryTools(campaignId);
        var oldBestiaryWait = bestiary.Create("First creature", "Old", "");
        var oldBestiary = staleState.PendingBestiaryProposal!;
        staleState.CancelBestiaryProposal(); await oldBestiaryWait;
        var newBestiaryWait = bestiary.Create("Second creature", "New", "");
        try { staleState.ApproveBestiaryProposal(oldBestiary); throw new Exception("Stale creature approved"); } catch (InvalidOperationException) { }
        Require(!newBestiaryWait.IsCompleted && staleState.Campaigns[0].Bestiary.Count == 0,
            "finding 4: stale Bestiary approval cannot save a newer creature");
        staleState.CancelBestiaryProposal(); await newBestiaryWait;
    }

    private static async Task SpeakerCompletionChecks(string temporary)
    {
        foreach (var present in new[] { false, true })
        {
            var (state, store) = AuditState(temporary, "speaker-" + present);
            var conversation = state.Current!;
            var npc = new NpcCard { Name = "Example NPC" }; state.Campaigns[0].Npcs.Add(npc);
            Guid? streamedId = null;
            state.Changed += () => { streamedId ??= conversation.Lines.LastOrDefault()?.PresentationSegments?.FirstOrDefault(segment => segment.Speaker == npc.Name)?.Id; };
            using var server = new ModelStub((_, _) => Delta(new { content = "The GM waits.\n[[speaker:Example NPC]][[smoke]]Keep the key." })
                + (present ? Calls(("present_response", "{\"segments\":[{\"speaker\":\"GM\",\"text\":\"She falls silent.\"}]}")) : "data: [DONE]\n\n"));
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Speak as the character.", new LlamaClient());
            var reply = conversation.Lines.Single(line => line.Role == "assistant");
            var segment = reply.PresentationSegments!.Single(item => item.Speaker == npc.Name);
            Require(segment.Text == "Keep the key." && segment.Id == streamedId && segment.SpeakerNpcId == npc.Id,
                "finding 5: streamed NPC identity, stable ID and prose survive final completion");
            Require(segment.NarrationEffectSpans!.Any(span => span.Effect == "smoke"), "finding 5: preserved speaker prefix retains its presentation effect");
            var saved = store.Load().Conversations.Single(item => item.Id == conversation.Id).Lines.Single(line => line.Role == "assistant");
            Require(saved.PresentationSegments!.Single(item => item.SpeakerNpcId == npc.Id).Id == streamedId
                && saved.ContextContent.Contains("Example NPC: Keep the key."), "finding 5: persistence/reload retains speaker-aware context");
            var archive = new ChronicleArchiveStore(Path.Combine(temporary, "speaker-archive-" + present + ".db"));
            archive.Archive(conversation.Id, [reply]);
            Require(archive.GetArchivedLines(conversation.Id).Single().PresentationSegments!.Single(item => item.SpeakerNpcId == npc.Id).Id == streamedId
                && archive.Read(conversation.Id, [reply.Sequence]).Contains("Example NPC: Keep the key."),
                "finding 5: archive retains speaker metadata and text-only GM retrieval preserves attribution");
            using var next = new ModelStub((_, _) => Text("Understood."));
            state.Endpoint = next.Endpoint;
            await state.SendAsync("What did she say?", new LlamaClient());
            var messages = next.Requests[0]["messages"]!.AsArray();
            var assistantContext = (string?)messages.Single(message => (string?)message?["role"] == "assistant")?["content"];
            Require(messages.Count(message => (string?)message?["role"] == "assistant") == 1
                && assistantContext!.Contains("[[speaker:GM]]The GM waits.", StringComparison.Ordinal)
                && assistantContext.Contains("[[speaker:Example NPC]]Keep the key.", StringComparison.Ordinal)
                && !assistantContext.Contains("Example NPC: Keep the key.", StringComparison.Ordinal),
                "speaker history: next request uses the instructed inline speaker markers in one logical assistant response");
        }

        var (toolState, _) = AuditState(temporary, "speaker-tool-continuation");
        var originalNpc = new NpcCard { Name = "Example NPC" }; toolState.Campaigns[0].Npcs.Add(originalNpc);
        using var continuation = new ModelStub((_, pass) =>
        {
            if (pass == 0) return Delta(new { content = "[[speaker:Example NPC]][[smoke]]Keep the key." }) + Calls(("read_player", "{}"));
            toolState.Campaigns[0].Npcs.Clear();
            toolState.Campaigns[0].Npcs.Add(new NpcCard { Name = "Example NPC" });
            return Calls(("present_response", "{\"segments\":[{\"speaker\":\"GM\",\"text\":\"She waits.\"}]}"));
        });
        toolState.Endpoint = continuation.Endpoint;
        await toolState.SendAsync("Read and continue.", new LlamaClient());
        var preserved = toolState.Current!.Lines.Single(line => line.Role == "assistant").PresentationSegments!.Single(item => item.Speaker == originalNpc.Name);
        Require(preserved.SpeakerNpcId == originalNpc.Id && preserved.Text == "Keep the key."
            && preserved.NarrationEffectSpans!.Any(span => span.Effect == "smoke"),
            "finding 5: final continuation preserves the streamed NPC link rather than relinking to a different same-named card");
        Require(continuation.Requests[1]["messages"]!.AsArray().Any(message => (string?)message?["role"] == "assistant"
            && ((string?)message?["content"])?.Contains("[[speaker:Example NPC]]Keep the key.") == true),
            "speaker history: in-flight tool continuation receives the instructed inline speaker marker");
    }
}
