using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DBLC;

// Identity and display text are snapshotted separately. Locators are typed so a
// missing or moved source can never be silently resolved to a different source.
public sealed record GmSourceReference(
    string Kind, string Title, Guid? CampaignId = null, Guid? NoteId = null,
    string? BookId = null, int? FromPage = null, int? ToPage = null,
    List<string>? SectionIds = null, Guid? ConversationId = null,
    List<long>? ArchiveSequences = null);

public sealed class GmSourceAttribution
{
    private readonly Dictionary<string, GmSourceReference> _eligible = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<string>> _selected = [];
    private readonly string _turnPrefix = Guid.NewGuid().ToString("N")[..8];
    private int _nextHandle = 1;

    public string Register(GmSourceReference source)
    {
        var handle = $"{_turnPrefix}-{_nextHandle++}";
        _eligible.Add(handle, source);
        return $"\nSource handle: {handle}. If this source informs your answer, append [[cite:{handle}]] to that answer.";
    }

    public void Select(Guid replyId, string handles)
    {
        foreach (var part in handles.Split(','))
        {
            var handle = part.Trim();
            if (!_eligible.ContainsKey(handle)) continue;
            if (!_selected.TryGetValue(replyId, out var selected)) _selected[replyId] = selected = [];
            if (selected.Contains(handle)) continue;
            selected.Add(handle);
        }
    }

    public IReadOnlyDictionary<Guid, List<GmSourceReference>> Selected() => _selected.ToDictionary(
        entry => entry.Key, entry => entry.Value.Select(handle => _eligible[handle]).ToList());

    // Called only after the complete, bounded tool result is known to have been
    // sent to the model. Search and catalogue tools never enter this method.
    public static GmSourceReference? FromRead(string tool, JsonObject args, string output, Guid campaignId,
        Guid archiveConversationId, string archiveTitle, Func<string?, string?> bookTitle,
        Func<string, GmNote?> findNote, IReadOnlyList<long>? returnedArchiveSequences = null,
        IReadOnlyList<(string Id, int Start, int End)>? returnedSections = null)
    {
        if (tool == "read_gm_note")
        {
            var idText = (string?)args["note_id"];
            if (!Guid.TryParse(idText, out var id) || findNote(idText ?? "") is not { } note ||
                !output.StartsWith("GM NOTE ", StringComparison.Ordinal)) return null;
            return new("gm_note", GmNoteTransfer.Title(note.Text), campaignId, id);
        }
        if (tool is "read_pages" or "read_json_sections")
        {
            var bookId = (string?)args["book_id"];
            if (string.IsNullOrWhiteSpace(bookId) || bookTitle(bookId) is not { } title ||
                !output.StartsWith("Source: ", StringComparison.Ordinal)) return null;
            if (tool == "read_pages")
            {
                var from = (int?)args["from_page"] ?? 0;
                var to = (int?)args["to_page"] ?? 0;
                if (from < 1 || to < from) return null;
                var reported = Regex.Match(output, @"PDF pages (\d+)-(\d+)");
                if (!reported.Success) return null;
                return new("library", title, BookId: bookId, FromPage: int.Parse(reported.Groups[1].Value), ToPage: int.Parse(reported.Groups[2].Value));
            }
            var ids = (args["section_ids"] as JsonArray ?? []).Select(value => (string?)value)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0 || returnedSections is null || returnedSections.Count != ids.Count ||
                ids.Any(id => !returnedSections.Any(section => section.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))) return null;
            return new("library", title, BookId: bookId,
                FromPage: returnedSections.Min(section => section.Start),
                ToPage: returnedSections.Max(section => section.End), SectionIds: ids);
        }
        if (tool == "read_chronicle")
        {
            var sequences = returnedArchiveSequences?.Distinct().ToList() ?? [];
            return sequences.Count == 0 ? null : new("chronicle", archiveTitle, campaignId,
                ConversationId: archiveConversationId, ArchiveSequences: sequences);
        }
        return null;
    }
}
