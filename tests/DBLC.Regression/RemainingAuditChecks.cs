using DBLC;
using System.IO;
using System.Text.Json;

internal static partial class Checks
{
    private static async Task RemainingAuditChecks(string temporary)
    {
        await BoundedRetrievalChecks(temporary);
        CampaignNoteReferenceDuplicationChecks(temporary);
    }

    private static async Task BoundedRetrievalChecks(string temporary)
    {
        var (state, _) = AuditState(temporary, "bounded-retrieval", workbench: false);
        var campaignId = state.ActiveCampaignId;
        var padded = new GmNote { Text = new string(' ', 1001) + "Valid after trimming" };
        var preview = GmNoteTransfer.Preview(GmNoteTransfer.ExportJson([padded]), campaignId, [], false);
        Require(preview.Rejected == 1 && preview.CreatedCount == 0,
            "finding 6: import rejects a padded note whose stored text exceeds 1000 characters");
        var exact = new GmNote { Text = new string('A', GmNoteTransfer.MaxNoteCharacters) };
        var exactPreview = GmNoteTransfer.Preview(GmNoteTransfer.ExportJson([exact]), campaignId, [], false);
        Require(exactPreview.CreatedCount == 1 && exactPreview.Creates.Single().Text.Length == GmNoteTransfer.MaxNoteCharacters,
            "finding 6: the actual 1000-character import boundary remains valid");
        var forged = new GmNoteTransfer.ImportPlan(campaignId, GmNoteTransfer.Fingerprint(state.GetGmNotes(campaignId)),
            [new GmNoteTransfer.ImportRow(Guid.NewGuid(), padded.Text)], [], 0, 0, []);
        var rejectedAtApply = false;
        try { state.ApplyGmNoteImport(forged); } catch (InvalidOperationException) { rejectedAtApply = true; }
        Require(rejectedAtApply && state.GetGmNotes(campaignId).Count == 0,
            "finding 6: applying an oversized import plan cannot bypass the stored-text limit");

        var folder = Path.Combine(temporary, "bounded-library");
        Directory.CreateDirectory(folder);
        var pdf = Path.Combine(folder, "Reference.pdf");
        WritePdf(pdf);
        var library = new LibraryService();
        var tooManyPages = library.ReadPages(pdf, 1, LibraryService.MaxPagesPerRead + 1);
        Require(tooManyPages.Contains("narrower page range", StringComparison.Ordinal) && !tooManyPages.Contains("Fixture page", StringComparison.Ordinal),
            "finding 6: excessive PDF page request returns a clear rejection without partial source text");
        var longPdf = Path.Combine(folder, "Long.pdf");
        WritePdf(longPdf, new string('P', LibraryService.MaxReadResultCharacters + 100));
        var oversizedPage = library.ReadPages(longPdf, 1, 1);
        Require(oversizedPage.Contains("read limit", StringComparison.Ordinal) && oversizedPage.Length < LibraryService.MaxReadResultCharacters && !oversizedPage.Contains("PPPP", StringComparison.Ordinal),
            "finding 6: one oversized PDF page is rejected whole rather than truncated");

        var json = Path.Combine(folder, "Reference.json");
        var sections = Enumerable.Range(0, LibraryService.MaxSectionsPerRead + 1)
            .Select(index => new { id = $"section-{index}", title = $"Section {index}", page_start = 1, page_end = 1, content = $"CONTENT_{index}" }).ToArray();
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Reference", source_pdf = "Reference.pdf" }, sections }));
        var tooManySections = library.ReadJsonSections(json, pdf, 1, sections.Select(section => section.id));
        Require(tooManySections.Contains("narrower set", StringComparison.Ordinal) && !tooManySections.Contains("CONTENT_", StringComparison.Ordinal),
            "finding 6: excessive JSON section batch returns no partial content");
        var largeBatch = Enumerable.Range(0, LibraryService.MaxSectionsPerRead)
            .Select(index => new { id = $"large-{index}", title = $"Large {index}", page_start = 1, page_end = 1, content = new string('B', 5000) }).ToArray();
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Reference", source_pdf = "Reference.pdf" }, sections = largeBatch }));
        var oversizedBatch = library.ReadJsonSections(json, pdf, 1, largeBatch.Select(section => section.id));
        Require(oversizedBatch.Contains("read limit", StringComparison.Ordinal) && !oversizedBatch.Contains("BBBB", StringComparison.Ordinal),
            "finding 6: a legal section count with too much combined content returns no partial batch");
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Reference", source_pdf = "Reference.pdf" },
            sections = new[] { new { id = "long", title = "Long", page_start = 1, page_end = 1, content = new string('X', LibraryService.MaxReadResultCharacters + 100) } } }));
        var largeSection = library.ReadJsonSections(json, pdf, 1, ["long"]);
        Require(largeSection.Contains("read limit", StringComparison.Ordinal) && largeSection.Length < LibraryService.MaxReadResultCharacters && !largeSection.Contains("XXXX", StringComparison.Ordinal),
            "finding 6: oversized JSON section is rejected whole rather than truncated");

        using var server = new ModelStub((_, pass) => pass == 0 ? Calls(("read_player", "{}")) : Text("Done."));
        state.Endpoint = server.Endpoint;
        await state.SendAsync("Read the long sheet.", new LlamaClient(), readPlayer: _ => new string('R', LibraryService.MaxReadResultCharacters + 100));
        var toolResult = (string?)server.Requests[1]["messages"]!.AsArray().Last(message => (string?)message?["role"] == "tool")!["content"];
        Require(toolResult is { Length: < LibraryService.MaxReadResultCharacters } && JsonSerializer.Serialize(toolResult).Length <= LibraryService.MaxReadResultCharacters
            && toolResult.Contains("next_offset:", StringComparison.Ordinal) && toolResult.Contains("RRRR", StringComparison.Ordinal),
            "finding 6: an oversized sheet reaches the model only as a bounded, explicitly partial window");
        using var escapedServer = new ModelStub((_, pass) => pass == 0 ? Calls(("read_player", "{}")) : Text("Done."));
        state.Endpoint = escapedServer.Endpoint;
        await state.SendAsync("Read the escaped sheet.", new LlamaClient(), readPlayer: _ => new string('\n', LibraryService.MaxReadResultCharacters / 2 + 100));
        var escapedResult = (string?)escapedServer.Requests[1]["messages"]!.AsArray().Last(message => (string?)message?["role"] == "tool")!["content"];
        Require(escapedResult is { Length: < LibraryService.MaxReadResultCharacters } && JsonSerializer.Serialize(escapedResult).Length <= LibraryService.MaxReadResultCharacters
            && escapedResult.Contains("next_offset:", StringComparison.Ordinal),
            "finding 6: escaped document text remains navigable without exceeding the serialized budget");
    }

    private static void CampaignNoteReferenceDuplicationChecks(string temporary)
    {
        var (state, store) = AuditState(temporary, "copied-note-references", workbench: false);
        var source = state.Current!;
        var sourceCampaignId = source.CampaignId;
        var note = new GmNote { Text = "A copied campaign ruling" };
        state.Campaigns.Single(campaign => campaign.Id == sourceCampaignId).GmNotes.Add(note);
        var originalReference = new GmNoteReference(sourceCampaignId, note.Id, "A copied campaign ruling");
        source.Lines.Add(new ChatLine { Sequence = 1, Role = "assistant", Content = "The ruling applies.", GmNoteReferences = [originalReference] });
        var archive = new ChronicleArchiveStore(Path.Combine(temporary, "audit-hardening", "copied-note-references", "archive.db"));
        archive.Archive(source.Id, [new ChatLine { Sequence = 2, Role = "assistant", Content = "Earlier ruling.", GmNoteReferences = [originalReference] }]);
        var unrelated = new Campaign { Name = "Unrelated", GmNotes = [new GmNote { Id = note.Id, Text = "Different campaign's note" }] };
        state.Campaigns.Add(unrelated);
        var collisionRejected = false;
        try { state.DuplicateCampaign(sourceCampaignId, unrelated.Id); } catch (InvalidOperationException) { collisionRejected = true; }
        Require(collisionRejected && state.Campaigns.Count == 2, "finding 7: a duplicate cannot reuse an unrelated campaign ID");

        var destinationId = Guid.NewGuid();
        state.DuplicateCampaign(sourceCampaignId, destinationId);
        var copy = state.Conversations.Single(conversation => conversation.CampaignId == destinationId);
        var activeReference = copy.Lines.Single().GmNoteReferences!.Single();
        var archivedReference = archive.GetArchivedLines(copy.Id).Single().GmNoteReferences!.Single();
        Require(activeReference.CampaignId == destinationId && archivedReference.CampaignId == destinationId &&
            activeReference.NoteId == note.Id && archivedReference.NoteId == note.Id && activeReference.Title == originalReference.Title,
            "finding 7: campaign duplicate remaps active and archived note references while preserving ID and title");
        Require(state.CanOpenGmNoteReference(activeReference) && state.CanOpenGmNoteReference(archivedReference) &&
            !state.CanOpenGmNoteReference(originalReference),
            "finding 7: duplicate references open only against the copied campaign");
        state.DeleteCampaign(sourceCampaignId);
        Require(state.CanOpenGmNoteReference(activeReference) && state.CanOpenGmNoteReference(archivedReference) &&
            store.Load().Conversations!.Single(conversation => conversation.Id == copy.Id).Lines.Single().GmNoteReferences!.Single().CampaignId == destinationId &&
            archive.GetArchivedLines(copy.Id).Single().GmNoteReferences!.Single().CampaignId == destinationId,
            "finding 7: deleting the original campaign leaves copied active and archived references intact after reload");
    }
}
