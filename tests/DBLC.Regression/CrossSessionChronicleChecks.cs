using DBLC;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static partial class Checks
{
    private static async Task CrossSessionChronicleChecks(string temporary)
    {
        var folder = Path.Combine(temporary, "cross-session-chronicle");
        var campaign = new Campaign { Name = "Cross-session test" };
        var previous = new Conversation { CampaignId = campaign.Id, SessionNumber = 0, Title = "First Session", IsEnded = true, Lines = [
            new ChatLine { Role = "user", Sequence = 1, Content = "We reached the church gate." },
            new ChatLine { Role = "assistant", Sequence = 2, Content = "The church door is sealed." },
            new ChatLine { Role = "assistant", Sequence = 3, Content = "The Mucous Lurker fell in the church cellar." }
        ] };
        var current = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, PreviousSessionNumber = 0, Title = "Second Session", Lines = [
            new ChatLine { Role = "user", Sequence = 1, Content = "What happened at the church?" }
        ] };
        var otherCampaign = new Campaign { Name = "Separate campaign" };
        var unrelated = new Conversation { CampaignId = otherCampaign.Id, SessionNumber = 0, Title = "Unrelated", Lines = [
            new ChatLine { Role = "user", Sequence = 1, Content = "The church is in another campaign." }
        ] };
        campaign.SessionOrder.AddRange([previous.Id, current.Id]);
        var store = new ConversationStore(Path.Combine(folder, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [previous, current, unrelated], [campaign, otherCampaign], campaign.Id));
        var archivePath = Path.Combine(folder, "archive.db");
        var archive = new ChronicleArchiveStore(archivePath);
        archive.Archive(previous.Id, [new ChatLine { Role = "assistant", Sequence = 3, Content = "The Mucous Lurker fell in the church cellar." }]);
        var state = new AppState(store, new LibraryService(), archive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(folder, "campaigns")));
        state.Select(state.Conversations.Single(item => item.Id == current.Id));

        var searchArgs = JsonSerializer.Serialize(new { query = "church" });
        var readArgs = JsonSerializer.Serialize(new { session_id = previous.Id.ToString("D"), line_ids = new[] { 1 } });
        using var server = new ModelStub((request, pass) => Response(request, pass));
        string Response(JsonObject request, int pass)
        {
            if (pass == 0) return Calls(("search_chronicle", searchArgs));
            if (pass == 1) return Calls(("read_chronicle", readArgs));
            var body = request["messages"]!.ToJsonString();
            var handle = Regex.Match(body, @"Source handle: ([0-9a-f]{8}-[1-9][0-9]*)").Groups[1].Value;
            return Text($"You were last outside the church. [[cite:{handle}]]");
        }
        state.Endpoint = server.Endpoint;
        await state.SendAsync("Look back at the church scene.", new LlamaClient());

        var tools = server.Requests[0]["tools"]!.ToJsonString();
        Require(tools.Contains("read_previous_session_ending") && tools.Contains("session_number") && tools.Contains("session_id"),
            "GM receives campaign-scoped Chronicle search, filtered search, focused read and direct previous-ending tools");
        var searchOutput = server.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
        Require(searchOutput.Contains($"chronicle_id: {previous.Id:N}:1") && searchOutput.Contains($"chronicle_id: {previous.Id:N}:3")
            && searchOutput.Contains($"chronicle_id: {current.Id:N}:1") && !searchOutput.Contains(unrelated.Id.ToString("N")),
            "campaign search finds archived and unarchived messages across Sessions with composite stable identities: " + searchOutput);
        Require(Regex.Matches(searchOutput, @"chronicle_id: [a-f0-9]{32}:\d+").Count <= 6
            && Regex.Matches(searchOutput, Regex.Escape($"chronicle_id: {previous.Id:N}:3")).Count == 1 && searchOutput.Length < 4000,
            "archive overlap does not duplicate a message and search output stays bounded");
        var readOutput = server.Requests[2]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
        Require(readOutput.Contains("We reached the church gate") && readOutput.Contains("The church door is sealed")
            && !readOutput.Contains("What happened at the church"),
            "focused read resolves the requested Session and returns its match plus a neighboring message");
        var cited = state.Current!.Lines.Last(line => line.Role == "assistant");
        Require(cited.SourceReferences?.Count == 1 && cited.SourceReferences[0].ConversationId == previous.Id
            && cited.SourceReferences[0].ArchiveSequences?.SequenceEqual([1L, 2L]) == true
            && state.CanOpenSourceReference(cited.SourceReferences[0])
            && state.GetCitedArchiveExcerpt(cited.SourceReferences[0]).Select(line => line.Sequence).SequenceEqual([1L, 2L]),
            "a substantive unarchived read creates a working Chronicle citation to its actual source Session");

        var filteredSearch = JsonSerializer.Serialize(new { query = "church", session_number = 0 });
        using var filterServer = new ModelStub((_, pass) => pass == 0 ? Calls(("search_chronicle", filteredSearch)) : Text("Found it."));
        state.Endpoint = filterServer.Endpoint;
        await state.SendAsync("Search the first Session only.", new LlamaClient());
        var filtered = filterServer.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
        Require(filtered.Contains(previous.Id.ToString("N")) && !filtered.Contains(current.Id.ToString("N")),
            "optional Session filter excludes matches from other Sessions");

        using var previousEndingServer = new ModelStub((_, pass) => pass == 0
            ? Calls(("read_previous_session_ending", "{}")) : Text("You were at the gate."));
        state.Endpoint = previousEndingServer.Endpoint;
        await state.SendAsync("What happened immediately before this Session?", new LlamaClient());
        var ending = previousEndingServer.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
        Require(ending.Contains("church gate") && ending.Contains("church door") && ending.Length < 4000,
            "direct previous-Session-ending read returns a small recent passage without keyword search");

        archive.Archive(previous.Id, Enumerable.Range(20, 8).Select(index => new ChatLine
        {
            Role = "assistant", Sequence = index, Content = "A ring was mentioned without identifying its metal."
        }).ToList());
        state.Conversations.Single(item => item.Id == previous.Id).Lines.Add(new ChatLine
        {
            Role = "assistant", Sequence = 50, Content = "The copper ring was placed on the anvil."
        });
        using (var rankingServer = new ModelStub((_, pass) => pass == 0
            ? Calls(("search_chronicle", "{\"query\":\"copper ring\"}")) : Text("Found the exact match.")))
        {
            state.Endpoint = rankingServer.Endpoint;
            await state.SendAsync("Find the distinctive ring passage.", new LlamaClient());
            var rankingResult = rankingServer.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
            Require(rankingResult.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Contains($"chronicle_id: {previous.Id:N}:50", StringComparison.Ordinal),
                "exact active Chronicle match ranks ahead of weaker archived matches");
        }
        state.Conversations.Single(item => item.Id == previous.Id).Lines.AddRange(Enumerable.Range(51, 8).Select(index => new ChatLine
        {
            Role = "assistant", Sequence = index, Content = "A silver item was mentioned without a clasp."
        }));
        archive.Archive(previous.Id, [new ChatLine { Role = "assistant", Sequence = 60, Content = "The silver clasp secured the cloak." }]);
        using (var reverseRankingServer = new ModelStub((_, pass) => pass == 0
            ? Calls(("search_chronicle", "{\"query\":\"silver clasp\"}")) : Text("Found the archived exact match.")))
        {
            state.Endpoint = reverseRankingServer.Endpoint;
            await state.SendAsync("Find the matching clasp passage.", new LlamaClient());
            var rankingResult = reverseRankingServer.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
            Require(rankingResult.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Contains($"chronicle_id: {previous.Id:N}:60", StringComparison.Ordinal),
                "exact archived Chronicle match ranks ahead of weaker active matches");
        }
        var focusedArchived = archive.GetArchivedLines(previous.Id, [3L, 60L]);
        Require(focusedArchived.Select(line => line.Sequence).SequenceEqual([3L, 60L])
            && focusedArchived[0].ContextContent.Contains("Mucous Lurker", StringComparison.Ordinal)
            && archive.GetArchivedTail(previous.Id, 2).Select(line => line.Sequence).SequenceEqual([27L, 60L]),
            "bounded archive sequence and tail queries preserve ordering across gaps and full line content");

        using var ordinaryServer = new ModelStub((_, _) => Text("No history needed."));
        state.Current!.OpeningSummary = "A saved opening recap.";
        state.Endpoint = ordinaryServer.Endpoint;
        await state.SendAsync("Continue normally.", new LlamaClient());
        Require(ordinaryServer.Requests.All(request => request["messages"]!.AsArray()
                .All(message => (string?)message?["role"] != "tool" && !(message?["content"]?.ToString().Contains("Previous Session 0 ending excerpt", StringComparison.Ordinal) ?? false))),
            "ordinary turns do not automatically retrieve or inject previous-Session Chronicle content");

        var recentActiveSequence = state.Current.Lines.Last(line => line.Role == "user").Sequence;
        archive.Archive(current.Id, [state.Current.Lines.Single(line => line.Sequence == 1)]);
        var emptyReadArgs = JsonSerializer.Serialize(new { session_id = current.Id.ToString("D"), line_ids = new[] { 1L, recentActiveSequence } });
        using var emptyReadServer = new ModelStub((_, pass) => pass == 0
            ? Calls(("read_chronicle", emptyReadArgs)) : Text("I can see the earlier Session discussion."));
        state.Endpoint = emptyReadServer.Endpoint;
        var priorUserCount = state.Current.Lines.Count(line => line.Role == "user");
        await state.SendAsync("", new LlamaClient());
        var emptyReadOutput = emptyReadServer.Requests[1]["messages"]!.AsArray()
            .Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
        Require(emptyReadOutput.Contains("What happened at the church?", StringComparison.Ordinal)
            && emptyReadOutput.Contains("Continue normally.", StringComparison.Ordinal)
            && state.Current.Lines.Count(line => line.Role == "user") == priorUserCount,
            "Empty Send reads both archived and active earlier current-Session messages without storing a blank player line");
    }
}
