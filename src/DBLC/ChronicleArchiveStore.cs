using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DBLC;

public sealed record ChronicleArchiveMatch(Guid ConversationId, long Sequence, string Role, string CreatedAt, string Excerpt, double Rank, string Content);

public sealed class ChronicleArchiveStore
{
    private readonly string _databasePath;

    public ChronicleArchiveStore() : this(AppDataPaths.File("chronicle-archive.db")) { }
    public ChronicleArchiveStore(string databasePath)
    {
        _databasePath = databasePath;
        Initialize();
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        SqliteConnection connection;
        try
        {
            connection = Open();
            try
            {
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The Chronicle archive failed its integrity check. Its files were not changed.");
            }
            catch { connection.Dispose(); throw; }
        }
        catch (SqliteException error)
        {
            throw new InvalidDataException("The Chronicle archive could not be opened. Campaign snapshots were not changed.", error);
        }
        using var ownedConnection = connection;
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS archived_lines (
                conversation_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                role TEXT NOT NULL,
                created_at TEXT NOT NULL,
                content TEXT NOT NULL,
                roll_sides INTEGER,
                gm_mark TEXT,
                presentation_json TEXT,
                image_attachment_key TEXT,
                entry_type TEXT,
                event_id TEXT,
                gm_note_refs_json TEXT,
                source_refs_json TEXT,
                completed_at TEXT,
                PRIMARY KEY (conversation_id, sequence)
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS archived_lines_fts USING fts5(
                conversation_id UNINDEXED,
                sequence UNINDEXED,
                role UNINDEXED,
                created_at UNINDEXED,
                content,
                tokenize='unicode61'
            );
            """;
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(archived_lines);";
        using var reader = columns.ExecuteReader();
        var hasRollSides = false;
        var hasGmMark = false;
        var hasPresentation = false;
        var hasImageAttachment = false;
        var hasEntryType = false;
        var hasEventId = false;
        var hasGmNoteRefs = false;
        var hasSourceRefs = false;
        var hasCompletedAt = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "roll_sides") hasRollSides = true;
            if (reader.GetString(1) == "gm_mark") hasGmMark = true;
            if (reader.GetString(1) == "presentation_json") hasPresentation = true;
            if (reader.GetString(1) == "image_attachment_key") hasImageAttachment = true;
            if (reader.GetString(1) == "entry_type") hasEntryType = true;
            if (reader.GetString(1) == "event_id") hasEventId = true;
            if (reader.GetString(1) == "gm_note_refs_json") hasGmNoteRefs = true;
            if (reader.GetString(1) == "source_refs_json") hasSourceRefs = true;
            if (reader.GetString(1) == "completed_at") hasCompletedAt = true;
        }
        reader.Close();
        if (!hasGmNoteRefs)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN gm_note_refs_json TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasEventId)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN event_id TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasRollSides)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN roll_sides INTEGER;";
            migrate.ExecuteNonQuery();
        }
        if (!hasGmMark)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN gm_mark TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasPresentation)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN presentation_json TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasImageAttachment)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN image_attachment_key TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasEntryType)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN entry_type TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasSourceRefs)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN source_refs_json TEXT;";
            migrate.ExecuteNonQuery();
        }
        if (!hasCompletedAt)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE archived_lines ADD COLUMN completed_at TEXT;";
            migrate.ExecuteNonQuery();
        }
    }

    public void Archive(Guid conversationId, IEnumerable<ChatLine> lines)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var line in lines.Where(line => line.Role is "user" or "assistant"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO archived_lines(conversation_id,sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at) VALUES($conversation,$sequence,$role,$created,$content,$rollSides,$gmMark,$presentation,$image,$entryType,$eventId,$gmNoteRefs,$sourceRefs,$completedAt);";
            command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
            command.Parameters.AddWithValue("$sequence", line.Sequence);
            command.Parameters.AddWithValue("$eventId", line.Id.ToString("N"));
            command.Parameters.AddWithValue("$role", line.Role);
            command.Parameters.AddWithValue("$created", line.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$content", line.Content);
            command.Parameters.AddWithValue("$rollSides", line.RollSides is { } sides ? sides : DBNull.Value);
            command.Parameters.AddWithValue("$gmMark", line.GmMark is { } mark ? mark : DBNull.Value);
            command.Parameters.AddWithValue("$presentation", line.PresentationSegments is { Count: > 0 } segments ? JsonSerializer.Serialize(segments) : DBNull.Value);
            command.Parameters.AddWithValue("$image", line.ImageAttachmentKey is { } image ? image : DBNull.Value);
            command.Parameters.AddWithValue("$entryType", line.EntryType is { } entryType ? entryType : DBNull.Value);
            command.Parameters.AddWithValue("$gmNoteRefs", line.GmNoteReferences is { Count: > 0 } references ? JsonSerializer.Serialize(references) : DBNull.Value);
            command.Parameters.AddWithValue("$sourceRefs", line.SourceReferences is { Count: > 0 } sources ? JsonSerializer.Serialize(sources) : DBNull.Value);
            command.Parameters.AddWithValue("$completedAt", line.CompletedAt is { } completedAt ? completedAt.ToString("O") : DBNull.Value);
            var inserted = command.ExecuteNonQuery();
            if (inserted == 0) continue;

            using var index = connection.CreateCommand();
            index.Transaction = transaction;
            index.CommandText = "INSERT INTO archived_lines_fts(conversation_id,sequence,role,created_at,content) VALUES($conversation,$sequence,$role,$created,$content);";
            index.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
            index.Parameters.AddWithValue("$sequence", line.Sequence);
            index.Parameters.AddWithValue("$role", line.Role);
            index.Parameters.AddWithValue("$created", line.CreatedAt.ToString("O"));
            index.Parameters.AddWithValue("$content", line.Content);
            index.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Duplicate(Guid sourceConversationId, Guid destinationConversationId, Guid? sourceCampaignId = null, Guid? destinationCampaignId = null,
        IReadOnlyDictionary<Guid, Guid>? conversationIds = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var originals = connection.CreateCommand())
        {
            originals.Transaction = transaction;
            originals.CommandText = "INSERT OR IGNORE INTO archived_lines(conversation_id,sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at) SELECT $destination,sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at FROM archived_lines WHERE conversation_id=$source;";
            originals.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
            originals.Parameters.AddWithValue("$source", sourceConversationId.ToString("N"));
            originals.ExecuteNonQuery();
        }
        if (sourceCampaignId is { } from && destinationCampaignId is { } to && from != to)
        {
            var rewrites = new List<(long Sequence, string References)>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT sequence,gm_note_refs_json FROM archived_lines WHERE conversation_id=$destination AND gm_note_refs_json IS NOT NULL;";
                select.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    List<GmNoteReference>? references;
                    try { references = JsonSerializer.Deserialize<List<GmNoteReference>>(reader.GetString(1)); }
                    catch (JsonException) { continue; }
                    if (references is null || !references.Any(reference => reference.CampaignId == from)) continue;
                    rewrites.Add((reader.GetInt64(0), JsonSerializer.Serialize(references.Select(reference =>
                        reference.CampaignId == from ? reference with { CampaignId = to } : reference))));
                }
            }
            foreach (var (sequence, references) in rewrites)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE archived_lines SET gm_note_refs_json=$references WHERE conversation_id=$destination AND sequence=$sequence;";
                update.Parameters.AddWithValue("$references", references);
                update.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
                update.Parameters.AddWithValue("$sequence", sequence);
                update.ExecuteNonQuery();
            }
            var sourceRewrites = new List<(long Sequence, string References)>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT sequence,source_refs_json FROM archived_lines WHERE conversation_id=$destination AND source_refs_json IS NOT NULL;";
                select.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    List<GmSourceReference>? references;
                    try { references = JsonSerializer.Deserialize<List<GmSourceReference>>(reader.GetString(1)); }
                    catch (JsonException) { continue; }
                    if (references is null) continue;
                    sourceRewrites.Add((reader.GetInt64(0), JsonSerializer.Serialize(references.Select(reference =>
                        reference.CampaignId == from ? reference with
                        {
                            CampaignId = to,
                            ConversationId = reference.ConversationId is { } oldId && conversationIds?.TryGetValue(oldId, out var newId) == true
                                ? newId : reference.ConversationId == sourceConversationId ? destinationConversationId : reference.ConversationId
                        } : reference))));
                }
            }
            foreach (var (sequence, references) in sourceRewrites)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE archived_lines SET source_refs_json=$references WHERE conversation_id=$destination AND sequence=$sequence;";
                update.Parameters.AddWithValue("$references", references);
                update.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
                update.Parameters.AddWithValue("$sequence", sequence);
                update.ExecuteNonQuery();
            }
        }
        using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText = "INSERT INTO archived_lines_fts(conversation_id,sequence,role,created_at,content) SELECT $destination,sequence,role,created_at,content FROM archived_lines_fts WHERE conversation_id=$source;";
            index.Parameters.AddWithValue("$destination", destinationConversationId.ToString("N"));
            index.Parameters.AddWithValue("$source", sourceConversationId.ToString("N"));
            index.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void DeleteConversation(Guid conversationId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "archived_lines_fts", "archived_lines" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE conversation_id=$conversation;";
            command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<ChatLine> GetArchivedLines(Guid conversationId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at FROM archived_lines WHERE conversation_id=$conversation ORDER BY sequence;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        using var reader = command.ExecuteReader();
        return DecodeLines(reader, conversationId);
    }

    public List<ChatLine> GetArchivedLines(Guid conversationId, IReadOnlyCollection<long> sequences)
    {
        var ids = sequences.Where(sequence => sequence > 0).Distinct().Take(36).ToArray();
        if (ids.Length == 0) return [];
        using var connection = Open();
        using var command = connection.CreateCommand();
        var parameters = ids.Select((_, index) => "$s" + index).ToArray();
        command.CommandText = $"SELECT sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at FROM archived_lines WHERE conversation_id=$conversation AND sequence IN ({string.Join(',', parameters)}) ORDER BY sequence;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue(parameters[index], ids[index]);
        using var reader = command.ExecuteReader();
        return DecodeLines(reader, conversationId);
    }

    public List<ChatLine> GetArchivedTail(Guid conversationId, int count)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,role,created_at,content,roll_sides,gm_mark,presentation_json,image_attachment_key,entry_type,event_id,gm_note_refs_json,source_refs_json,completed_at FROM archived_lines WHERE conversation_id=$conversation ORDER BY sequence DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        command.Parameters.AddWithValue("$limit", Math.Clamp(count, 1, 36));
        using var reader = command.ExecuteReader();
        var lines = DecodeLines(reader, conversationId);
        lines.Reverse();
        return lines;
    }

    private static List<ChatLine> DecodeLines(SqliteDataReader reader, Guid conversationId)
    {
        var lines = new List<ChatLine>();
        while (reader.Read())
        {
            List<GmPresentationSegment>? segments = null;
            if (!reader.IsDBNull(6))
                try { segments = JsonSerializer.Deserialize<List<GmPresentationSegment>>(reader.GetString(6)); } catch { }
            List<GmNoteReference>? gmNoteRefs = null;
            if (!reader.IsDBNull(10))
                try { gmNoteRefs = JsonSerializer.Deserialize<List<GmNoteReference>>(reader.GetString(10)); } catch { }
            List<GmSourceReference>? sourceRefs = null;
            if (!reader.IsDBNull(11))
                try { sourceRefs = JsonSerializer.Deserialize<List<GmSourceReference>>(reader.GetString(11)); } catch { }
            lines.Add(new ChatLine { GmNoteReferences = gmNoteRefs, SourceReferences = sourceRefs, Id = !reader.IsDBNull(9) && Guid.TryParse(reader.GetString(9), out var eventId) ? eventId : LegacyEventId(conversationId, reader.GetInt64(0)), Sequence = reader.GetInt64(0), Role = reader.GetString(1), CreatedAt = DateTimeOffset.Parse(reader.GetString(2)), CompletedAt = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)), Content = reader.GetString(3), RollSides = reader.IsDBNull(4) ? null : reader.GetInt32(4), GmMark = reader.IsDBNull(5) ? null : reader.GetString(5), PresentationSegments = segments, ImageAttachmentKey = reader.IsDBNull(7) ? null : reader.GetString(7), EntryType = reader.IsDBNull(8) ? null : reader.GetString(8) });
        }
        return lines;
    }

    private static Guid LegacyEventId(Guid conversationId, long sequence) => new(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{conversationId:N}:{sequence}")).AsSpan(0, 16));

    public bool ContainsEvent(Guid conversationId, Guid eventId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM archived_lines WHERE conversation_id=$conversation AND event_id=$event LIMIT 1;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        command.Parameters.AddWithValue("$event", eventId.ToString("N"));
        return command.ExecuteScalar() is not null;
    }

    public bool ContainsSequences(Guid conversationId, IReadOnlyCollection<long> sequences)
    {
        if (sequences.Count == 0 || sequences.Count > 36) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        var parameters = sequences.Distinct().Select((sequence, index) => (sequence, name: "$s" + index)).ToArray();
        command.CommandText = $"SELECT COUNT(*) FROM archived_lines WHERE conversation_id=$conversation AND sequence IN ({string.Join(',', parameters.Select(item => item.name))});";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        foreach (var (sequence, name) in parameters) command.Parameters.AddWithValue(name, sequence);
        return (long)(command.ExecuteScalar() ?? 0L) == parameters.Length;
    }

    public bool ContainsImageReference(Guid conversationId, string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM archived_lines WHERE conversation_id=$conversation AND image_attachment_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is not null;
    }

    public string Search(Guid conversationId, string query)
    {
        var terms = Regex.Matches(query ?? "", "[\\p{L}\\p{N}]+").Select(match => "\"" + match.Value.Replace("\"", "\"\"") + "\"").Take(16).ToArray();
        if (terms.Length == 0) return "Give search_chronicle a few words, a name, or a short phrase to look for.";
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT archived_lines_fts.sequence, archived_lines_fts.role, archived_lines_fts.created_at, snippet(archived_lines_fts, 4, '⟦', '⟧', '…', 16), archived_lines.roll_sides, archived_lines.entry_type
            FROM archived_lines_fts
            LEFT JOIN archived_lines ON archived_lines.conversation_id=archived_lines_fts.conversation_id AND archived_lines.sequence=CAST(archived_lines_fts.sequence AS INTEGER)
            WHERE archived_lines_fts MATCH $query AND archived_lines_fts.conversation_id = $conversation
            ORDER BY bm25(archived_lines_fts)
            LIMIT 8;
            """;
        command.Parameters.AddWithValue("$query", string.Join(" OR ", terms));
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        using var reader = command.ExecuteReader();
        var output = new StringBuilder();
        while (reader.Read())
        {
            var label = !reader.IsDBNull(5) && reader.GetString(5) == "campaign_record" ? "CAMPAIGN_RECORD" : reader.GetString(1).ToUpperInvariant();
            output.Append("- archive_line_id: ").Append(reader.GetInt64(0)).Append(" | ").Append(label).Append(" | ")
                .Append(reader.GetString(2)).Append(" | excerpt: ").Append(reader.GetString(3));
            if (!reader.IsDBNull(4)) output.Append(" | die: d").Append(reader.GetInt32(4));
            output.AppendLine();
        }
        return output.Length == 0 ? "No archived chronicle lines matched that query." : output.ToString();
    }

    public IReadOnlyList<ChronicleArchiveMatch> Search(IEnumerable<Guid> conversationIds, string query, int limit = 24)
    {
        var ids = conversationIds.Distinct().Take(5000).ToArray();
        var terms = Regex.Matches(query ?? "", "[\\p{L}\\p{N}]+", RegexOptions.IgnoreCase)
            .Select(match => "\"" + match.Value.Replace("\"", "\"\"") + "\"").Take(16).ToArray();
        if (ids.Length == 0 || terms.Length == 0) return [];
        using var connection = Open();
        using var command = connection.CreateCommand();
        var sessionParams = ids.Select((_, index) => "$c" + index).ToArray();
        command.CommandText = $"""
            SELECT f.conversation_id, CAST(f.sequence AS INTEGER), f.role, f.created_at,
                   snippet(archived_lines_fts, 4, '⟦', '⟧', '…', 16), bm25(archived_lines_fts), f.content
            FROM archived_lines_fts f
            WHERE archived_lines_fts MATCH $query AND f.conversation_id IN ({string.Join(',', sessionParams)})
            ORDER BY CASE WHEN instr(lower(f.content), lower($phrase)) > 0 THEN 0 ELSE 1 END, bm25(archived_lines_fts)
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", string.Join(" OR ", terms));
        command.Parameters.AddWithValue("$phrase", query?.Trim() ?? "");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 48));
        for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue(sessionParams[index], ids[index].ToString("N"));
        using var reader = command.ExecuteReader();
        var results = new List<ChronicleArchiveMatch>();
        while (reader.Read())
            if (Guid.TryParseExact(reader.GetString(0), "N", out var conversationId))
                results.Add(new(conversationId, reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetString(6)));
        return results;
    }

    public string Read(Guid conversationId, IEnumerable<long> requestedIds, Action<IReadOnlyList<long>>? onReturnedSequences = null)
    {
        var sequences = requestedIds.Distinct().Take(12).SelectMany(id => new[] { id - 1, id, id + 1 }).Where(id => id > 0).Distinct().Order().ToArray();
        if (sequences.Length == 0) return "Provide one or more archive_line_id values returned by search_chronicle.";
        using var connection = Open();
        using var command = connection.CreateCommand();
        var parameterNames = sequences.Select((_, index) => "$s" + index).ToArray();
        command.CommandText = $"SELECT sequence, role, created_at, content, roll_sides, entry_type, presentation_json FROM archived_lines WHERE conversation_id=$conversation AND sequence IN ({string.Join(',', parameterNames)}) ORDER BY sequence;";
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("N"));
        for (var index = 0; index < sequences.Length; index++) command.Parameters.AddWithValue(parameterNames[index], sequences[index]);
        using var reader = command.ExecuteReader();
        var output = new StringBuilder();
        var returnedSequences = new List<long>();
        while (reader.Read())
        {
            returnedSequences.Add(reader.GetInt64(0));
            var label = !reader.IsDBNull(5) && reader.GetString(5) == "campaign_record" ? "CAMPAIGN RECORD" : reader.GetString(1).ToUpperInvariant();
            output.Append("--- Archive line ").Append(reader.GetInt64(0)).Append(" · ").Append(label).Append(" · ")
                .Append(reader.GetString(2));
            if (!reader.IsDBNull(4)) output.Append(" · d").Append(reader.GetInt32(4)).Append(" roll");
            var content = reader.GetString(3);
            if (reader.GetString(1) == "assistant" && !reader.IsDBNull(6))
            {
                try
                {
                    var segments = JsonSerializer.Deserialize<List<GmPresentationSegment>>(reader.GetString(6));
                    content = new ChatLine { Content = content, PresentationSegments = segments }.ContextContent;
                }
                catch (JsonException) { /* Legacy/malformed metadata keeps the original archived text readable. */ }
            }
            output.AppendLine(" ---").AppendLine(content).AppendLine();
        }
        onReturnedSequences?.Invoke(returnedSequences);
        return output.Length == 0 ? "Those archive line IDs were not found in this conversation." : output.ToString();
    }
}
