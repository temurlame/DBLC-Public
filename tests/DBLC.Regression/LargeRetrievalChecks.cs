using DBLC;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static partial class Checks
{
    private static async Task LargeRetrievalChecks(string temporary)
    {
        var player = string.Concat(Enumerable.Repeat("# A quoted \"name\" and an escaped \\ path 🗝️\n", 600)) + "Late player marker: the silver key.\n";
        var workspace = string.Concat(Enumerable.Repeat("Workspace line with <markup> & symbols.\n", 600)) + "Late workspace marker: the sealed ledger.\n";
        var proposalOpened = false;
        using (var documents = new ModelStub((request, pass) => pass switch
        {
            0 => Calls([("read_player", "{}"), ("read_workspace", "{}")], true),
            1 => Calls(ToolOutputs(request).Select((output, index) => (index == 0 ? "read_player" : "read_workspace",
                JsonSerializer.Serialize(new { offset = int.Parse(Regex.Match(output, @"next_offset: (\d+)").Groups[1].Value) }))).ToArray(), true),
            2 => Calls(("propose_player_update", "{\"markdown\":\"# Replacement\"}")),
            _ => Text("The complete-sheet update was not opened.")
        }))
        {
            await new LlamaClient().CompleteAsync(documents.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Inspect the documents." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => player, () => workspace,
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { },
                onPlayerSheetUpdate: _ => { proposalOpened = true; return Task.FromResult("opened"); });
            var first = ToolOutputs(documents.Requests[1]);
            var second = ToolOutputs(documents.Requests[2]).TakeLast(2).ToList();
            var playerNext = int.Parse(Regex.Match(first[0], @"next_offset: (\d+)").Groups[1].Value);
            var workspaceNext = int.Parse(Regex.Match(first[1], @"next_offset: (\d+)").Groups[1].Value);
            Require(first.Count == 2 && second.Count == 2
                && first[0].EndsWith(player[..playerNext], StringComparison.Ordinal)
                && second[0].Contains(player[playerNext..Math.Min(player.Length, playerNext + 100)], StringComparison.Ordinal)
                && first[1].EndsWith(workspace[..workspaceNext], StringComparison.Ordinal)
                && second[1].Contains(workspace[workspaceNext..Math.Min(workspace.Length, workspaceNext + 100)], StringComparison.Ordinal),
                "large Player and Workspace reads expose exact escaped text with repeatable continuation positions");
            Require(first.Concat(second).All(result => JsonSerializer.Serialize(result).Length <= LibraryService.MaxReadResultCharacters)
                && !proposalOpened && ToolOutputs(documents.Requests[3]).Last().Contains("partial Player Sheet", StringComparison.Ordinal),
                "bounded document results respect the serialized budget and partial Player reads cannot open replacement approval");
        }

        using (var findDocuments = new ModelStub((request, pass) => pass switch
        {
            0 => Calls([("read_player", "{\"query\":\"silver key\"}"), ("read_workspace", "{\"query\":\"sealed ledger\"}")], true),
            1 => Calls(ToolOutputs(request).Select((output, index) => (index == 0 ? "read_player" : "read_workspace",
                JsonSerializer.Serialize(new { offset = int.Parse(Regex.Match(output, @"match_offset: (\d+)").Groups[1].Value) }))).ToArray(), true),
            _ => Text("I found the late records.")
        }))
        {
            await new LlamaClient().CompleteAsync(findDocuments.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Find the late records." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => player, () => workspace,
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { });
            var matches = ToolOutputs(findDocuments.Requests[1]);
            var found = ToolOutputs(findDocuments.Requests[2]).TakeLast(2).ToList();
            Require(matches.Count == 2 && matches.All(match => match.Contains("match_offset:", StringComparison.Ordinal)
                && JsonSerializer.Serialize(match).Length <= LibraryService.MaxReadResultCharacters)
                && found[0].Contains("silver key", StringComparison.Ordinal) && found[1].Contains("sealed ledger", StringComparison.Ordinal),
                "literal finds jump to late Player and Workspace text without sequentially reading the whole document");
        }
        var repeatedMatches = string.Join(" ", Enumerable.Range(0, 45).Select(index => $"needle-{index:D2}"));
        var searchedProposalOpened = false;
        using (var manyMatches = new ModelStub((request, pass) => pass switch
        {
            0 => Calls(("read_player", "{\"query\":\"needle\"}")),
            1 => Calls(("read_player", JsonSerializer.Serialize(new { query = "needle", offset = int.Parse(Regex.Match(ToolOutputs(request).Last(), @"next_offset: (\d+)").Groups[1].Value) }))),
            2 => Calls(("propose_player_update", "{\"markdown\":\"# Replacement\"}")),
            _ => Text("Search did not authorize replacement.")
        }))
        {
            await new LlamaClient().CompleteAsync(manyMatches.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Find each occurrence." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => repeatedMatches, () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { },
                onPlayerSheetUpdate: _ => { searchedProposalOpened = true; return Task.FromResult("opened"); });
            var firstSearch = ToolOutputs(manyMatches.Requests[1]).Single();
            var secondSearch = ToolOutputs(manyMatches.Requests[2]).Last();
            Require(Regex.Matches(firstSearch, @"match_offset: ").Count == 20
                && Regex.Matches(secondSearch, @"match_offset: ").Count == 20
                && !searchedProposalOpened && ToolOutputs(manyMatches.Requests[3]).Last().Contains("partial Player Sheet", StringComparison.Ordinal),
                "literal find has bounded continuation and search-only Player access cannot authorize replacement");
        }

        var bookLines = "BOOKS\n" + string.Join("\n", Enumerable.Range(0, 1000).Select(index => $"- book_id: book-{index:D3} | title: {(index == 999 ? "Late Handbook" : "Handbook")} {index:D3}"));
        var bookmarkLines = "BOOKMARKS\n" + string.Join("\n", Enumerable.Range(0, 320).Select(index => $"- {(index == 319 ? "Late Appendix" : "Chapter")} {index:D3} — PDF page {index + 1}"));
        var sectionLines = "SECTIONS\n" + string.Join("\n", Enumerable.Range(0, 320).Select(index => $"- section_id: section-{index:D3} | title: {(index == 319 ? "Late Rule" : "Rule")} | PDF pages {index + 1}"));
        var npcLines = "KNOWN NPCs\n" + string.Join("\n", Enumerable.Range(0, 2600).Select(index => $"- {(index == 2599 ? "Late NPC" : "NPC")} {index:D4}"));
        var bestiaryLines = "CAMPAIGN BESTIARY\n" + string.Join("\n", Enumerable.Range(0, 2600).Select(index => $"- {(index == 2599 ? "Late Beast" : "Beast")} {index:D4} | Slain: 0"));
        var importedMapLines = "IMPORTED CAMPAIGN MAPS\n" + string.Join("\n", Enumerable.Range(0, 1200).Select(index => $"- map_id: imported-{index:D4} | {(index == 1199 ? "Late Image" : "Image")} {index:D4}"));
        var catalogueCalls = new[] { "list_books", "get_catalogue", "get_json_catalogue", "list_npcs", "list_bestiary", "read_npc", "read_bestiary", "list_campaign_maps" };
        var catalogueQueries = new[] { "Late Handbook", "Late Appendix", "Late Rule", "Late NPC", "Late Beast", "Late Image" };
        var citationAttempts = 0;
        using (var catalogues = new ModelStub((_, pass) => pass switch
        {
            0 => Calls(catalogueCalls.Select(name => (name, name is "get_catalogue" or "get_json_catalogue" ? "{\"book_id\":\"book-999\"}"
                : name is "read_npc" or "read_bestiary" ? "{\"name\":\"Late NPC\"}" : "{}")).ToArray(), true),
            1 => Calls(catalogueCalls.Where(name => name is not ("read_npc" or "read_bestiary")).Select((name, index) => (name, name is "get_catalogue" or "get_json_catalogue"
                ? JsonSerializer.Serialize(new { book_id = "book-999", query = catalogueQueries[index] })
                : JsonSerializer.Serialize(new { query = catalogueQueries[index] }))).Append(("list_books", "{\"query\":\"Handbook 000\"}")).ToArray(), true),
            _ => Text("The late entries were found.")
        }))
        {
            await new LlamaClient().CompleteAsync(catalogues.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Find late catalogue entries." }], "", "",
                () => bookLines, _ => "title", _ => bookmarkLines, (_, _, _) => "pages", _ => sectionLines, (_, _) => "section",
                () => npcLines, _ => new string('N', 25_000), (_, _, _, _) => "saved", () => "Player", () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { },
                bestiaryTools: new CampaignBestiaryTools
                {
                    List = () => bestiaryLines, Read = _ => new string('B', 25_000), Create = (_, _, _) => Task.FromResult("created"),
                    ProposeUpdate = (_, _, _) => Task.FromResult("updated"), UpdateSlain = (_, _, _) => "updated"
                },
                listCampaignMaps: () => importedMapLines,
                onSubstantiveRead: (_, _, _) => { citationAttempts++; return ""; });
            var first = ToolOutputs(catalogues.Requests[1]);
            var filtered = ToolOutputs(catalogues.Requests[2]).TakeLast(7).ToList();
            Require(first.Count == 8 && first[0].Contains("next_offset:", StringComparison.Ordinal)
                && first[3].Contains("next_offset:", StringComparison.Ordinal)
                && first[4].Contains("next_offset:", StringComparison.Ordinal)
                && first[5].Contains("next_offset:", StringComparison.Ordinal)
                && first[6].Contains("next_offset:", StringComparison.Ordinal)
                && first[7].Contains("next_offset:", StringComparison.Ordinal)
                && first.All(result => JsonSerializer.Serialize(result).Length <= LibraryService.MaxReadResultCharacters),
                "large catalogues and individual NPC/Bestiary cards all return bounded navigable windows");
            Require(filtered.Count == 7 && filtered.Take(6).Select((result, index) => result.Contains(catalogueQueries[index], StringComparison.Ordinal)).All(found => found)
                && filtered[0].Contains("book-999", StringComparison.Ordinal)
                && filtered[1].Contains("PDF page 320", StringComparison.Ordinal)
                && filtered[2].Contains("section-319", StringComparison.Ordinal)
                && filtered[5].Contains("imported-1199", StringComparison.Ordinal)
                && filtered[6].Contains("book-000", StringComparison.Ordinal)
                && citationAttempts == 0,
                "literal filters find late stable locators without making metadata catalogues citation eligible");
        }

        var folder = Path.Combine(temporary, "large-retrieval");
        var campaign = new Campaign { Name = "Bounded collection fixture" };
        var session = new Conversation { CampaignId = campaign.Id, SessionNumber = 0, Title = "Large retrieval" };
        campaign.SessionOrder.Add(session.Id);
        for (var index = 0; index < 320; index++)
        {
            campaign.Agenda.Add(new AgendaEvent { Title = index == 319 ? "Late appointment" : $"Earlier event {index:D3}", DueMinute = (index + 1L) * 1440, Status = index == 319 ? "upcoming" : "resolved", Visibility = index == 319 ? "gm" : "player" });
            campaign.OpenThreads.Add(new OpenThread { Text = index == 319 ? "Late private bargain\n\nThe second paragraph names a hidden promise." : $"Earlier thread {index:D3}", Visibility = index == 319 ? "gm" : "player" });
        }
        var store = new ConversationStore(Path.Combine(folder, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [session], [campaign], campaign.Id));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(folder, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(folder, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(folder, "campaigns")));
        state.Select(state.Conversations.Single(item => item.Id == session.Id));
        using (var unsafeProposal = new ModelStub((_, pass) => pass == 0
            ? Calls(("propose_player_update", "{\"markdown\":\"# Destructive replacement\"}")) : Text("No update was opened.")))
        {
            state.Endpoint = unsafeProposal.Endpoint;
            await state.SendAsync("Update my large sheet.", new LlamaClient(), readPlayer: _ => player);
            Require(state.PendingPlayerUpdate is null && ToolOutputs(unsafeProposal.Requests[1]).Last().Contains("too large for a complete model read", StringComparison.Ordinal),
                "an oversized Player Sheet cannot be replaced by bypassing partial reads or the approval boundary");
        }
        var agenda = state.GetAgendaTools(campaign.Id);
        var allEvents = agenda.Read(new JsonObject());
        var lateEvent = agenda.Read(Obj("{\"query\":\"Late appointment\",\"status\":\"upcoming\",\"from_day\":320}"));
        Require(allEvents.Contains("next_offset:", StringComparison.Ordinal) && JsonSerializer.Serialize(allEvents).Length <= LibraryService.MaxReadResultCharacters
            && lateEvent.Contains("Late appointment", StringComparison.Ordinal) && lateEvent.Contains("event_id:", StringComparison.Ordinal),
            "large resolved Agenda history remains bounded while late filtered events retain stable IDs");
        Require(state.GetPlayerAgenda(campaign.Id).All(item => item.Visibility == "player")
            && state.GetPlayerOpenThreads(campaign.Id).All(item => item.Visibility == "player"),
            "GM-only late Agenda and thread records remain excluded from player-facing collections");

        var libraryFolder = Path.Combine(folder, "library");
        Directory.CreateDirectory(libraryFolder);
        var pdf = Path.Combine(libraryFolder, "Large.pdf");
        WritePdf(pdf, new string('P', 25_000));
        var json = Path.Combine(libraryFolder, "Large.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Large", source_pdf = "Large.pdf" },
            sections = new[] { new { id = "large-rule", title = "Large rule", page_start = 1, page_end = 1, content = new string('S', 25_000) } } }));
        var library = new LibraryService();
        var twoPages = Path.Combine(libraryFolder, "TwoPages.pdf");
        WriteTwoPagePdf(twoPages, "First page rule", "Second page ruling");
        var completeRange = library.ReadPages(twoPages, 1, 2);
        Require(completeRange.Contains("First page rule", StringComparison.Ordinal)
            && completeRange.Contains("Second page ruling", StringComparison.Ordinal),
            "an ordinary two-page PDF read remains complete when it fits the existing budget");
        var oversizedRange = Path.Combine(libraryFolder, "OversizedRange.pdf");
        WriteTwoPagePdf(oversizedRange, new string('X', 11_000), new string('Y', 11_000));
        var narrowRequest = library.ReadPages(oversizedRange, 1, 2, allowLargeSinglePage: true);
        Require(narrowRequest.Contains("Choose a narrower page range", StringComparison.Ordinal)
            && !narrowRequest.Contains("XXXX", StringComparison.Ordinal)
            && library.ReadPages(oversizedRange, 2, 2, allowLargeSinglePage: true).Contains("YYYY", StringComparison.Ordinal),
            "an over-budget multi-page request names a usable narrower path without leaking a partial range");
        var sourceReads = 0;
        using (var sources = new ModelStub((_, pass) => pass == 0
            ? Calls([("read_pages", "{\"book_id\":\"large\",\"from_page\":1,\"to_page\":1}"),
                ("read_json_sections", "{\"book_id\":\"large\",\"section_ids\":[\"large-rule\"]}")], true)
            : Text("I read both source windows.")))
        {
            await new LlamaClient().CompleteAsync(sources.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Read the sources." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, from, to) => library.ReadPages(pdf, from, to, allowLargeSinglePage: true),
                _ => "sections", (_, ids) => library.ReadJsonSections(json, pdf, 1, ids, allowLargeSingleSection: true),
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => "Player", () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { },
                onSubstantiveRead: (tool, args, output) =>
                {
                    var source = GmSourceAttribution.FromRead(tool, args, output, campaign.Id, session.Id, "Session 0",
                        _ => "Large", _ => null, returnedSections: [("large-rule", 1, 1)]);
                    if (source is not null) sourceReads++;
                    return "";
                });
            var windows = ToolOutputs(sources.Requests[1]);
            Require(windows.Count == 2 && windows.All(window => window.Contains("next_offset:", StringComparison.Ordinal)
                && JsonSerializer.Serialize(window).Length <= LibraryService.MaxReadResultCharacters)
                && windows[0].Contains("PPPP", StringComparison.Ordinal) && windows[1].Contains("SSSS", StringComparison.Ordinal)
                && sourceReads == 2,
                "oversized single PDF pages and JSON leaf sections are navigable substantive reads within citation and result budgets");
        }
        using (var threads = new ModelStub((_, pass) => pass switch
        {
            0 => Calls(("read_threads", "{}")),
            1 => Calls(("read_threads", "{\"query\":\"hidden promise\",\"visibility\":\"gm\"}")),
            _ => Text("The private thread is available.")
        }))
        {
            await new LlamaClient().CompleteAsync(threads.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Find the private thread." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => "Player", () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { }, openThreadTools: state.GetOpenThreadTools(campaign.Id));
            var all = ToolOutputs(threads.Requests[1]).Single();
            var filtered = ToolOutputs(threads.Requests[2]).Last();
            Require(all.Contains("next_offset:", StringComparison.Ordinal) && JsonSerializer.Serialize(all).Length <= LibraryService.MaxReadResultCharacters
                && filtered.Contains("Late private bargain", StringComparison.Ordinal) && filtered.Contains("thread_id:", StringComparison.Ordinal)
                && filtered.Contains("visibility: gm", StringComparison.Ordinal),
                "large Open Thread ledger is bounded and a private late record remains retrievable by ID");
        }
    }

    private static List<string> ToolOutputs(JsonObject request) => request["messages"]!.AsArray()
        .Where(message => (string?)message?["role"] == "tool")
        .Select(message => message!["content"]!.ToString()).ToList();

    private static void WriteTwoPagePdf(string path, string first, string second)
    {
        static string Stream(string value)
        {
            var text = $"BT /F1 12 Tf 30 100 Td ({value}) Tj ET";
            return $"<< /Length {text.Length} >>\nstream\n{text}\nendstream";
        }
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            Stream(first), Stream(second)
        ];
        var output = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Length);
            output.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Length;
        output.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append($"{offset:0000000000} 00000 n \n");
        output.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, output.ToString(), Encoding.ASCII);
    }
}
