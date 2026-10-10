using System.IO;
using System.Text;
using System.Text.Json;

namespace DBLC;

// Campaign JSON remains the source of truth. This format is portable between installations.
public static class GmNoteTransfer
{
    public const int MaxImportBytes = 24 * 1024 * 1024;
    public const int MaxNotes = 20_000;
    public const int MaxResults = 8;
    public const int MaxNoteCharacters = 1000;
    private const string Format = "dblc-gm-notes";

    public sealed record ImportRow(Guid Id, string Text);
    public sealed record ImportPlan(Guid CampaignId, string CampaignFingerprint, IReadOnlyList<ImportRow> Creates,
        IReadOnlyList<ImportRow> Updates, int Skipped, int Rejected, IReadOnlyList<string> RejectionReasons)
    {
        public int CreatedCount => Creates.Count;
        public int UpdatedCount => Updates.Count;
    }

    public static string ExportJson(IEnumerable<GmNote> notes) => JsonSerializer.Serialize(new
    {
        format = Format,
        version = 1,
        notes = notes.Select(note => new { id = note.Id, text = note.Text }).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true });

    public static string ExportMarkdown(IEnumerable<GmNote> notes)
    {
        var result = new StringBuilder("# GM Notes\n\n");
        foreach (var note in notes)
            result.Append("## ").AppendLine(Title(note.Text)).AppendLine().AppendLine(note.Text).AppendLine();
        return result.ToString();
    }

    public static string Title(string text)
    {
        var firstLine = text.Split(['\r', '\n'], 2)[0].Trim();
        if (firstLine.Length == 0) return "Untitled note";
        return firstLine.Length <= 80 ? firstLine : firstLine[..79] + "…";
    }

    public static string Fingerprint(IEnumerable<GmNote> notes)
    {
        var value = new StringBuilder();
        foreach (var note in notes.OrderBy(note => note.Id))
            value.Append(note.Id.ToString("N")).Append(':').Append(note.Text.Length).Append(':').Append(note.Text).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    public static ImportPlan Preview(string json, Guid campaignId, IReadOnlyList<GmNote> existing, bool allowUpdates)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxImportBytes) throw new InvalidDataException("The import file is too large.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format ||
            !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var formatVersion) || formatVersion != 1 ||
            !root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("This is not a supported DBLC GM Notes JSON export.");
        if (notes.GetArrayLength() > MaxNotes) throw new InvalidDataException("The import exceeds the 20,000-note limit.");

        var byId = existing.ToDictionary(note => note.Id);
        var byText = new HashSet<string>(existing.Select(note => note.Text.Trim()), StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<Guid>();
        var creates = new List<ImportRow>();
        var updates = new List<ImportRow>();
        var reasons = new List<string>();
        var skipped = 0;
        var rejected = 0;
        var index = 0;
        foreach (var item in notes.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(property => property.Name is not ("id" or "text")) ||
                !item.TryGetProperty("id", out var idProperty) ||
                idProperty.ValueKind != JsonValueKind.String || !Guid.TryParse(idProperty.GetString(), out var id) || id == Guid.Empty ||
                !item.TryGetProperty("text", out var textProperty) || textProperty.ValueKind != JsonValueKind.String ||
                textProperty.GetString() is not { } rawText || !IsValidStoredText(rawText))
            {
                rejected++; AddReason($"Row {index}: invalid ID or note text."); continue;
            }
            var text = rawText;
            if (!seenIds.Add(id)) { rejected++; AddReason($"Row {index}: duplicate ID in import file."); continue; }
            if (byId.TryGetValue(id, out var current))
            {
                if (string.Equals(current.Text, text, StringComparison.Ordinal)) { skipped++; continue; }
                if (!allowUpdates) { rejected++; AddReason($"Row {index}: ID conflicts with an existing note; enable updates to replace it."); continue; }
                if (byText.Contains(text.Trim()) && !string.Equals(current.Text.Trim(), text, StringComparison.OrdinalIgnoreCase))
                { skipped++; continue; }
                byText.Add(text.Trim());
                updates.Add(new ImportRow(id, text));
            }
            else if (!byText.Add(text.Trim())) skipped++;
            else creates.Add(new ImportRow(id, text));
        }
        return new ImportPlan(campaignId, Fingerprint(existing), creates, updates, skipped, rejected, reasons);

        void AddReason(string reason) { if (reasons.Count < 8) reasons.Add(reason); }
    }

    public static bool IsValidStoredText(string? text) => text is { Length: > 0 and <= MaxNoteCharacters } && !string.IsNullOrWhiteSpace(text);

    public static string Search(IReadOnlyList<GmNote> notes, string query)
    {
        var boundedQuery = query[..Math.Min(query.Length, 120)].Trim();
        var terms = boundedQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2).Take(8).ToArray();
        if (terms.Length == 0) return "Search needs one or more terms of at least two characters.";
        var matches = notes.Select(note =>
        {
            var title = Title(note.Text);
            var matchedTerms = terms.Count(term => note.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var score = matchedTerms * 10 + terms.Count(term => title.Contains(term, StringComparison.OrdinalIgnoreCase)) * 3 +
                (note.Text.Contains(boundedQuery, StringComparison.OrdinalIgnoreCase) ? 20 : 0);
            return (note, title, score);
        }).Where(result => result.score > 0).OrderByDescending(result => result.score)
            .ThenBy(result => result.title, StringComparer.OrdinalIgnoreCase).ThenBy(result => result.note.Id).Take(MaxResults).ToList();
        return matches.Count == 0 ? "No matching GM Notes in this campaign." :
            "GM NOTE SEARCH RESULTS (open a note by ID for full text)\n" +
            string.Join("\n", matches.Select(result =>
            {
                var position = terms.Select(term => result.note.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase)).Where(value => value >= 0).DefaultIfEmpty(0).Min();
                var start = Math.Max(0, position - 35);
                var excerpt = result.note.Text.Substring(start, Math.Min(140, result.note.Text.Length - start)).Replace('\r', ' ').Replace('\n', ' ');
                return $"- note_id: {result.note.Id:N} | {result.title} | {excerpt}";
            }));
    }
}
