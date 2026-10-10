using System.IO;
using System.Text;

namespace DBLC;

/// <summary>Loads local, player-editable presentation text for generic GM waiting states.</summary>
public sealed class GmActivityCatalog
{
    private const string DefaultGenericActivity = "GM is thinking…";
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "GM_ACTIVITY.md");

    public string PickActivity()
    {
        try
        {
            if (!File.Exists(_path)) return DefaultGenericActivity;
            var (faces, sayings) = Parse(File.ReadAllText(_path, Encoding.UTF8));
            if (sayings.Count == 0) return DefaultGenericActivity;

            // Choose the two pools independently so every face can pair with every saying.
            var saying = sayings[Random.Shared.Next(sayings.Count)];
            return faces.Count == 0 ? saying : $"{faces[Random.Shared.Next(faces.Count)]} {saying}";
        }
        catch
        {
            return DefaultGenericActivity;
        }
    }

    private static (List<string> Faces, List<string> Sayings) Parse(string markdown)
    {
        var faces = new List<string>();
        var sayings = new List<string>();
        string? section = null;
        var insideComment = false;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = StripHtmlComments(rawLine.TrimEnd('\r'), ref insideComment).Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith('>') ||
                line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(';')) continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                section = heading.Equals("Faces", StringComparison.OrdinalIgnoreCase) ? "Faces" :
                    heading.Equals("Sayings", StringComparison.OrdinalIgnoreCase) ? "Sayings" : null;
                continue;
            }
            if (line.StartsWith('#')) continue;
            if (section is null || IsInstruction(line)) continue;

            var entry = StripListMarker(line).Trim();
            var maximumLength = section == "Faces" ? 80 : 160;
            if (entry.Length == 0 || entry.Length > maximumLength || entry.Any(char.IsControl)) continue;

            var destination = section == "Faces" ? faces : sayings;
            if (!destination.Contains(entry, StringComparer.Ordinal)) destination.Add(entry);
        }

        return (faces, sayings);
    }

    private static string StripHtmlComments(string line, ref bool insideComment)
    {
        var output = new StringBuilder(line.Length);
        var cursor = 0;
        while (cursor < line.Length)
        {
            if (insideComment)
            {
                var close = line.IndexOf("-->", cursor, StringComparison.Ordinal);
                if (close < 0) return output.ToString();
                insideComment = false;
                cursor = close + 3;
                continue;
            }

            var open = line.IndexOf("<!--", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                output.Append(line.AsSpan(cursor));
                break;
            }
            output.Append(line.AsSpan(cursor, open - cursor));
            insideComment = true;
            cursor = open + 4;
        }
        return output.ToString();
    }

    private static string StripListMarker(string line)
    {
        if (line.Length >= 2 && line[1] == ' ' && line[0] is '-' or '*' or '+') return line[2..];
        var index = 0;
        while (index < line.Length && char.IsDigit(line[index])) index++;
        if (index > 0 && index + 1 < line.Length && line[index] is '.' or ')' && line[index + 1] == ' ')
            return line[(index + 2)..];
        return line;
    }

    private static bool IsInstruction(string line) =>
        line.StartsWith("note:", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("instructions:", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("example:", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("how to ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("edit ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("add ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("use ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("do not ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("these ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("this file ", StringComparison.OrdinalIgnoreCase);
}
