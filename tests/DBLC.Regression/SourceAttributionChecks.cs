using DBLC;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static partial class Checks
{
    private static async Task SourceAttributionChecks(string temporary)
    {
        var source = new GmSourceAttribution();
        var campaign = Guid.NewGuid();
        var note = new GmNote { Text = "A hidden bargain" };
        var noteArgs = JsonNode.Parse($"{{\"note_id\":\"{note.Id:N}\"}}")!.AsObject();
        Require(GmSourceAttribution.FromRead("search_gm_notes", noteArgs, "result", campaign, Guid.NewGuid(), "Session 1", _ => null, _ => note) is null,
            "source attribution: search results are ineligible");
        var noteSource = GmSourceAttribution.FromRead("read_gm_note", noteArgs, $"GM NOTE {note.Id:N}\n{note.Text}", campaign, Guid.NewGuid(), "Session 1", _ => null, _ => note)!;
        var handle = Regex.Match(source.Register(noteSource), @"\[\[cite:([^\]]+)\]\]").Groups[1].Value;
        Require(source.Selected().Count == 0, "source attribution: reading alone does not cite");
        source.Select(Guid.NewGuid(), "missing-1");
        Require(source.Selected().Values.All(value => value.Count == 0), "source attribution: unknown handle is rejected");
        var reply = Guid.NewGuid();
        var parser = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(reply, value));
        var visible = string.Concat(parser.Append("A bargain.[[ci").Concat(parser.Append("te:" + handle + "]]"))
            .Concat(parser.Append(" End.")).Concat(parser.Flush()).Select(batch => batch.Text));
        Require(visible == "A bargain. End." && source.Selected()[reply].Single() == noteSource,
            "source attribution: split marker is hidden and selects the substantive note");
        source.Select(reply, handle + "," + handle);
        Require(source.Selected()[reply].Count == 1, "source attribution: duplicate handles collapse");
        var secondHandle = Regex.Match(source.Register(new GmSourceReference("library", "Rules", BookId: "C:/Rules.pdf", FromPage: 42)),
            @"\[\[cite:([^\]]+)\]\]").Groups[1].Value;
        source.Select(reply, secondHandle + "," + handle);
        Require(source.Selected()[reply].Select(item => item.Kind).SequenceEqual(["gm_note", "library"]),
            "source attribution: multiple selected sources keep first-selection order");
        var other = new GmSourceAttribution();
        other.Register(noteSource);
        other.Select(reply, handle);
        Require(other.Selected().Values.All(value => value.Count == 0), "source attribution: a prior turn handle cannot be replayed");
        var literal = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(reply, value));
        var code = string.Concat(literal.Append($"`[[cite:{handle}]]`\n```text\n[[cite:{handle}]]\n```").Concat(literal.Flush()).Select(batch => batch.Text));
        Require(code.Contains($"`[[cite:{handle}]]`") && code.Contains($"\n[[cite:{handle}]]\n") && source.Selected()[reply].Count == 2,
            "source attribution: inline and fenced code remain literal");
        var malformedReply = Guid.NewGuid();
        var malformed = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(malformedReply, value));
        var malformedVisible = string.Concat(malformed.Append("Before [[cite:not-valid?]] after [[cite:unfinished")
            .Concat(malformed.Flush()).Select(batch => batch.Text));
        Require(malformedVisible == "Before [[cite:not-valid?]] after [[cite:unfinished" && !source.Selected().ContainsKey(malformedReply),
            "source attribution: malformed and incomplete citation syntax remains literal without selecting a source");
        var mixedReply = Guid.NewGuid();
        var mixed = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(mixedReply, value));
        var mixedVisible = string.Concat(mixed.Append("Bad [[cite:not-valid?]]; stale [[cite:deadbeef-99999]]; valid [[ci")
            .Concat(mixed.Append("te:" + handle + "]]; tail [[cite:unfinished"))
            .Concat(mixed.Flush()).Select(batch => batch.Text));
        Require(mixedVisible == "Bad [[cite:not-valid?]]; stale ; valid ; tail [[cite:unfinished" &&
                source.Selected()[mixedReply].Single() == noteSource,
            "source attribution: mixed malformed, unknown, split valid, and unfinished markers retain only literal prose and eligible citation");
        var overlengthReply = Guid.NewGuid();
        var overlength = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(overlengthReply, value));
        var overlengthPrefix = "[[cite:" + new string('x', 100);
        var overlengthStart = string.Concat(overlength.Append(overlengthPrefix).Select(batch => batch.Text));
        Require(overlengthStart == overlengthPrefix,
            "source attribution: an overlength unfinished citation streams as literal text without an unbounded buffer");
        var nestedLiteral = overlengthStart + string.Concat(overlength.Append(" [[cite:" + handle[..4])
            .Concat(overlength.Append(handle[4..] + "]]")).Select(batch => batch.Text));
        Require(nestedLiteral == overlengthPrefix + " [[cite:" + handle + "]]" && !source.Selected().ContainsKey(overlengthReply),
            "source attribution: a nested eligible handle inside an overlength malformed citation stays literal and unselected");
        var separateVisible = string.Concat(overlength.Append(" separate [[cite:" + handle + "]]")
            .Concat(overlength.Flush()).Select(batch => batch.Text));
        Require(separateVisible == " separate " && source.Selected()[overlengthReply].Single() == noteSource,
            "source attribution: an independent valid marker after the malformed passage still selects its source");
        var sameChunkReply = Guid.NewGuid();
        var sameChunk = new GmNarrationMarkerParser("focus", true, onCitation: value => source.Select(sameChunkReply, value));
        var sameChunkInput = overlengthPrefix + " [[cite:" + handle + "]]";
        var sameChunkVisible = string.Concat(sameChunk.Append(sameChunkInput).Concat(sameChunk.Flush()).Select(batch => batch.Text));
        Require(sameChunkVisible == sameChunkInput && !source.Selected().ContainsKey(sameChunkReply),
            "source attribution: a closed overlength malformed candidate stays literal when delivered in one chunk");

        var (state, store) = AuditState(temporary, "source-attribution", workbench: false);
        state.AddGmNote(state.ActiveCampaignId, "basement bargain\nThe old price was iron.");
        var id = state.GetGmNotes(state.ActiveCampaignId).Single().Id;
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("search_gm_notes", "{\"query\":\"basement\"}"))
            : Text("I found a title, but have not read the note. [[cite:deadbeef-1]]")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Search only.", new LlamaClient());
            Require(state.Current!.Lines.Last().SourceReferences is null &&
                !server.Requests[1]["messages"]!.ToJsonString().Contains("Source handle:"),
                "source attribution: search-only note results cannot generate an eligible or cited source");
        }
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_gm_note", $"{{\"note_id\":\"{id:N}\"}}"))
            : Text("The bargain stands.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Read it.", new LlamaClient());
            Require(state.Current!.Lines.Last().SourceReferences is null &&
                server.Requests[1]["messages"]!.AsArray().Any(message => (string?)message?["role"] == "tool" && ((string?)message?["content"])!.Contains("Source handle:")),
                "source attribution: substantive read supplies a handle but uncited prose gets no chip");
            Require(!server.Requests[0]["messages"]!.ToJsonString().Contains("Source handle:") &&
                !server.Requests[0]["messages"]!.ToJsonString().Contains("[[cite:"),
                "source attribution: ordinary initial requests carry no citation instruction");
        }
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_gm_note", $"{{\"note_id\":\"{id:N}\"}}"))
            : Text("The price is iron. [[cite:" + Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("What is the price?", new LlamaClient());
            var answer = state.Current!.Lines.Last();
            Require(answer.Content == "The price is iron. " && answer.SourceReferences?.Single().NoteId == id,
                "source attribution: cited note appears once and marker never enters Chronicle prose");
            Require(store.Load().Conversations!.Single().Lines.Last().SourceReferences?.Single().NoteId == id,
                "source attribution: selected note reference persists through Session reload");
        }
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_gm_note", $"{{\"note_id\":\"{id:N}\"}}"))
            : Calls(("present_response", "{\"segments\":[{\"speaker\":\"GM\",\"text\":\"The price remains iron. [[cite:" +
                Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]\"}]}"))))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Present it.", new LlamaClient());
            Require(state.Current!.Lines.Last().Content.Contains("The price remains iron.") &&
                !state.Current!.Lines.Last().Content.Contains("[[cite:") && state.Current!.Lines.Last().SourceReferences?.Count == 1,
                "source attribution: present_response selects a source without persisting markers");
        }
        var cited = state.Current!.Lines.Last();
        using (var server = new ModelStub((request, pass) =>
        {
            if (pass == 0) return Calls(("read_gm_note", $"{{\"note_id\":\"{id:N}\"}}"));
            state.StopCurrentResponse();
            return Text("This cancelled answer must not cite. [[cite:" +
                Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]" );
        }))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Stop before the answer.", new LlamaClient());
            Require(state.Current!.Lines.Last().SourceReferences is null,
                "source attribution: Stop discards eligible and selected sources before persistence");
        }
        var archive = new ChronicleArchiveStore(Path.Combine(temporary, "source-reference-archive.db"));
        archive.Archive(state.Current.Id, [cited]);
        Require(archive.GetArchivedLines(state.Current.Id).Single().SourceReferences?.Single().NoteId == id,
            "source attribution: SQLite archive preserves typed source references");

        var bookArgs = JsonNode.Parse("{\"book_id\":\"C:/Rules.pdf\",\"from_page\":42,\"to_page\":43}")!.AsObject();
        var librarySource = GmSourceAttribution.FromRead("read_pages", bookArgs, "Source: Rules.pdf, PDF pages 42-43\nText", campaign, Guid.NewGuid(), "Session 1", _ => "Rules", _ => null);
        Require(librarySource is { Kind: "library", FromPage: 42, ToPage: 43, BookId: "C:/Rules.pdf" },
            "source attribution: PDF page reference retains the selected book and page range");
        var sectionArgs = JsonNode.Parse("{\"book_id\":\"C:/Rules.pdf\",\"section_ids\":[\"creation\"]}")!.AsObject();
        var sectionSource = GmSourceAttribution.FromRead("read_json_sections", sectionArgs,
            "Source: Rules.pdf; DBLC companion JSON: Rules.json\n{\"page_start\":42,\"page_end\":43}", campaign,
            Guid.NewGuid(), "Session 1", _ => "Rules", _ => null, returnedSections: [("creation", 42, 43)]);
        Require(sectionSource is { FromPage: 42, ToPage: 43 } && sectionSource.SectionIds!.Single() == "creation",
            "source attribution: JSON section retains its exact section ID and page locator");
        var libraryFolder = Path.Combine(temporary, "source-library");
        Directory.CreateDirectory(libraryFolder);
        var pdfPath = Path.Combine(libraryFolder, "Rules.pdf");
        WritePdf(pdfPath);
        state.SetLibraryFolder(libraryFolder);
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_pages", $"{{\"book_id\":{System.Text.Json.JsonSerializer.Serialize(pdfPath)},\"from_page\":1,\"to_page\":1}}"))
            : Text("The rules say so. [[cite:" + Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Read the rule.", new LlamaClient());
            var reference = state.Current!.Lines.Last().SourceReferences!.Single();
            Require(reference is { Kind: "library", FromPage: 1, ToPage: 1 } && reference.BookId == pdfPath,
                "source attribution: real PDF tool continuation resolves the selected page");
            File.Delete(pdfPath);
            Require(!state.CanOpenSourceReference(reference) && reference.Title == "Rules",
                "source attribution: moved Library PDF becomes unavailable with its label intact");
        }
        WritePdf(pdfPath);
        File.WriteAllText(Path.Combine(libraryFolder, "Rules.json"),
            "{\"schema_version\":\"1.0\",\"book\":{\"title\":\"Rules\",\"source_pdf\":\"Rules.pdf\"},\"sections\":[{\"id\":\"creation\",\"title\":\"Creation\",\"page_start\":1,\"page_end\":1,\"content\":\"A iron gate key.\"}]}");
        state.RefreshLibrary();
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_json_sections", $"{{\"book_id\":{System.Text.Json.JsonSerializer.Serialize(pdfPath)},\"section_ids\":[\"creation\"]}}"))
            : Text("The section describes a iron key. [[cite:" + Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Read the companion section.", new LlamaClient());
            Require(state.Current!.Lines.Last().SourceReferences?.Single() is { Kind: "library", FromPage: 1 } companionReference &&
                companionReference.SectionIds!.Single() == "creation",
                "source attribution: real JSON companion read preserves its selected section and PDF page");
        }
        var archiveConversation = Guid.NewGuid();
        var archiveStore = new ChronicleArchiveStore(Path.Combine(temporary, "source-attribution-history.db"));
        archiveStore.Archive(archiveConversation, [new ChatLine { Sequence = 7, Role = "user", Content = "The old key." },
            new ChatLine { Sequence = 8, Role = "assistant", Content = "It opened the gate." }]);
        var archivedText = archiveStore.Read(archiveConversation, [8]);
        var archiveSource = GmSourceAttribution.FromRead("read_chronicle", JsonNode.Parse("{\"line_ids\":[8]}")!.AsObject(), archivedText,
            campaign, archiveConversation, "Session 3", _ => null, _ => null, [7, 8]);
        Require(archiveSource?.ArchiveSequences?.SequenceEqual([7, 8]) == true && archiveSource.ConversationId == archiveConversation,
            "source attribution: Chronicle locator names only the exact returned archived lines");
        Require(GmSourceAttribution.FromRead("search_chronicle", JsonNode.Parse("{}")!.AsObject(), archivedText,
            campaign, archiveConversation, "Session 3", _ => null, _ => null) is null,
            "source attribution: Chronicle search results are ineligible");

        var historyRoot = Path.Combine(temporary, "source-history-integration");
        var historyCampaign = new Campaign { Name = "History source" };
        var historySession = new Conversation { CampaignId = historyCampaign.Id, Title = "Session three", SessionNumber = 3,
            Lines = [new ChatLine { Sequence = 20, Role = "user", Content = "The shed gate." },
                new ChatLine { Sequence = 21, Role = "assistant", Content = "Its key is iron." }], NextSequence = 22 };
        historyCampaign.SessionOrder.Add(historySession.Id);
        var historyStateStore = new ConversationStore(Path.Combine(historyRoot, "state"));
        historyStateStore.Save(new SavedState("local", "fixture", "", null, [historySession], [historyCampaign], historyCampaign.Id));
        var historyArchive = new ChronicleArchiveStore(Path.Combine(historyRoot, "archive.db"));
        historyArchive.Archive(historySession.Id, historySession.Lines);
        historySession.Lines.Clear();
        historyStateStore.Save(new SavedState("local", "fixture", "", null, [historySession], [historyCampaign], historyCampaign.Id));
        var historyState = new AppState(historyStateStore, new LibraryService(), historyArchive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(historyRoot, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(historyRoot, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(historyRoot, "campaigns")));
        historyState.Select(historyState.Conversations.Single());
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("search_chronicle", "{\"query\":\"iron\"}"))
            : Text("The search suggests a key, but I have not read the archive.")))
        {
            historyState.Endpoint = server.Endpoint;
            await historyState.SendAsync("Search history only.", new LlamaClient());
            Require(historyState.Current!.Lines.Last().SourceReferences is null &&
                !server.Requests[1]["messages"]!.ToJsonString().Contains("Source handle:"),
                "source attribution: archive search alone never creates an eligible reference");
        }
        using (var server = new ModelStub((request, pass) => pass == 0
            ? Calls(("read_chronicle", "{\"line_ids\":[21]}"))
            : Text("The key is iron. [[cite:" + Regex.Match(request["messages"]!.ToJsonString(), @"Source handle: ([a-f0-9]+-\d+)").Groups[1].Value + "]]")))
        {
            historyState.Endpoint = server.Endpoint;
            await historyState.SendAsync("Recall the key.", new LlamaClient());
            var reference = historyState.Current!.Lines.Last().SourceReferences!.Single();
            Require(reference.Kind == "chronicle" && reference.ConversationId == historySession.Id &&
                reference.ArchiveSequences!.SequenceEqual([20, 21, 22]),
                $"source attribution: actual archived Chronicle read creates an exact Session excerpt reference ({reference.ConversationId}; {string.Join(',', reference.ArchiveSequences ?? [])})");
            Require(historyState.GetCitedArchiveExcerpt(reference).Select(line => line.Sequence).SequenceEqual([20, 21, 22]) &&
                historyState.CanOpenSourceReference(reference),
                "source attribution: citation viewer resolves exactly the retrieved archived lines");
            var reloadedReference = historyStateStore.Load().Conversations!.Single().Lines.Last().SourceReferences!.Single();
            Require(reloadedReference.Kind == reference.Kind && reloadedReference.ConversationId == reference.ConversationId &&
                reloadedReference.ArchiveSequences!.SequenceEqual(reference.ArchiveSequences!),
                "source attribution: archived Chronicle reference survives active Session reload");
            Require(!historyState.CanOpenSourceReference(reference with { CampaignId = Guid.NewGuid() }) &&
                historyState.GetCitedArchiveExcerpt(reference with { CampaignId = Guid.NewGuid() }).Count == 0,
                "source attribution: archive citation cannot cross campaign boundary");
            var copiedCampaignId = Guid.NewGuid();
            historyState.DuplicateCampaign(historyCampaign.Id, copiedCampaignId);
            var copiedSession = historyState.Conversations.Single(item => item.CampaignId == copiedCampaignId);
            var copiedReference = copiedSession.Lines.Last().SourceReferences!.Single();
            Require(copiedReference.CampaignId == copiedCampaignId && copiedReference.ConversationId == copiedSession.Id &&
                historyState.CanOpenSourceReference(copiedReference) && historyState.GetCitedArchiveExcerpt(copiedReference).Count == 3,
                "source attribution: campaign duplication remaps active archive reference and copied excerpt");
            historyState.Select(historyState.Conversations.Single(item => item.Id == historySession.Id));
            historyArchive.DeleteConversation(historySession.Id);
            Require(!historyState.CanOpenSourceReference(reference) && reference.Title == "Session 3",
                "source attribution: missing original excerpt becomes unavailable without changing its label");
            historyState.Select(copiedSession);
            Require(historyState.CanOpenSourceReference(copiedReference),
                "source attribution: deleting original archive leaves duplicate citation resolvable");
        }

        var secondReference = new GmSourceReference("chronicle", "Session 3", historyCampaign.Id,
            ConversationId: historySession.Id, ArchiveSequences: [20, 21]);
        var duplicateSource = new ChatLine { Sequence = 40, Role = "assistant", Content = "Earlier key.", SourceReferences = [secondReference] };
        var duplicateArchive = new ChronicleArchiveStore(Path.Combine(temporary, "source-duplicate-archive.db"));
        duplicateArchive.Archive(historySession.Id, [duplicateSource]);
        var duplicatedCampaign = Guid.NewGuid();
        var duplicatedSession = Guid.NewGuid();
        duplicateArchive.Duplicate(historySession.Id, duplicatedSession, historyCampaign.Id, duplicatedCampaign,
            new Dictionary<Guid, Guid> { [historySession.Id] = duplicatedSession });
        Require(duplicateArchive.GetArchivedLines(duplicatedSession).Single().SourceReferences!.Single() is { CampaignId: var remappedCampaign, ConversationId: var remappedSession }
            && remappedCampaign == duplicatedCampaign && remappedSession == duplicatedSession,
            "source attribution: duplicated SQLite references remap campaign and Session identities");
    }
}
