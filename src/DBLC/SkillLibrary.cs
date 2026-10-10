using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace DBLC;

/// <summary>Reads optional Agent Skills from ordinary SKILL.md files in DBLC's user data folder.</summary>
public static class SkillLibrary
{
    public const int MaxInstructionCharacters = 24_000;
    private static readonly Regex ValidName = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    public static string Root => AppDataPaths.File("skills");

    public static void InitializeDefaults() => InitializeDefaults(Root, Path.Combine(AppContext.BaseDirectory, "default-skills"));

    internal static void InitializeDefaults(string root, string defaultsRoot)
    {
        if (Directory.Exists(root)) return;
        Directory.CreateDirectory(root);
        if (!Directory.Exists(defaultsRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(defaultsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var source = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(source)) continue;
            var targetDirectory = Path.Combine(root, Path.GetFileName(directory));
            Directory.CreateDirectory(targetDirectory);
            File.Copy(source, Path.Combine(targetDirectory, "SKILL.md"));
        }
    }

    public static string List() => List(Root);

    internal static string List(string root)
    {
        if (!Directory.Exists(root)) return $"No skills folder exists yet. Default skills are initialized on DBLC startup. Folder: {root}";
        var found = new List<SkillMetadata>();
        var invalid = new List<string>();
        foreach (var directory in Directories(root))
        {
            var file = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(file)) continue;
            try { found.Add(Parse(file, Path.GetFileName(directory)).Metadata); }
            catch (InvalidDataException error) { invalid.Add($"{Path.GetFileName(directory)}: {error.Message}"); }
        }
        var lines = new List<string> { $"Skills folder: {root}" };
        lines.AddRange(found.OrderBy(skill => skill.Name, StringComparer.Ordinal).Select(skill => $"- {skill.Name}: {skill.Description}"));
        if (invalid.Count > 0) lines.Add("Invalid SKILL.md files skipped: " + string.Join("; ", invalid.Take(8)));
        if (found.Count == 0 && invalid.Count == 0) lines.Add("No skills are available. Add a folder containing SKILL.md; its YAML frontmatter needs name and description.");
        return string.Join('\n', lines);
    }

    public static string Read(string name) => Read(Root, name);

    internal static string Read(string root, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 64 || !ValidName.IsMatch(name)) return "Provide the exact skill name from list_skills.";
        if (!Directory.Exists(root)) return "No skills folder exists. Call list_skills to see its location.";
        var matches = Directories(root).Where(directory => string.Equals(Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return "No unique skill with that name is available. Call list_skills to see valid names.";
        var file = Path.Combine(matches[0], "SKILL.md");
        if (!File.Exists(file)) return "That skill folder has no SKILL.md file.";
        try
        {
            var parsed = Parse(file, Path.GetFileName(matches[0]));
            return $"Loaded skill: {parsed.Metadata.Name}\n{parsed.Instructions}";
        }
        catch (InvalidDataException error) { return $"Skill could not be loaded: {error.Message}"; }
        catch (IOException error) { return $"Skill could not be read: {error.Message}"; }
        catch (UnauthorizedAccessException) { return "Skill could not be read because access to its file was denied."; }
    }

    private static IEnumerable<string> Directories(string root) => Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
        .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);

    private static (SkillMetadata Metadata, string Instructions) Parse(string path, string folderName)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxInstructionCharacters * 4L + 8_192) throw new InvalidDataException($"file exceeds the {MaxInstructionCharacters:N0}-character instruction limit");
        var text = File.ReadAllText(path, Encoding.UTF8);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length > 0) lines[0] = lines[0].TrimStart('\uFEFF');
        if (lines.Length < 4 || lines[0] != "---") throw new InvalidDataException("YAML frontmatter must begin with ---");
        var end = Array.FindIndex(lines, 1, line => line == "---");
        if (end < 0) throw new InvalidDataException("YAML frontmatter is missing its closing ---");
        string? name = null;
        string? description = null;
        for (var index = 1; index < end; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]) || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon < 1) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key is not ("name" or "description")) continue;
            if (key == "name") { name = Scalar(value); continue; }
            if (value is "|" or "|-" or ">" or ">-" or "|+" or ">+")
            {
                var block = new List<string>();
                while (index + 1 < end && (string.IsNullOrWhiteSpace(lines[index + 1]) || char.IsWhiteSpace(lines[index + 1][0])))
                    block.Add(lines[++index].Trim());
                description = value.StartsWith('>') ? string.Join(' ', block.Where(part => part.Length > 0)) : string.Join('\n', block);
            }
            else description = Scalar(value);
        }
        if (name is null || name.Length is 0 or > 64 || !ValidName.IsMatch(name)) throw new InvalidDataException("name must use lowercase letters, numbers, and single hyphens, up to 64 characters");
        if (!string.Equals(name, folderName, StringComparison.Ordinal)) throw new InvalidDataException("name must match its skill folder name");
        if (string.IsNullOrWhiteSpace(description) || description.Length > 1024) throw new InvalidDataException("description must contain 1–1024 characters");
        var instructions = string.Join('\n', lines.Skip(end + 1)).Trim();
        if (instructions.Length == 0) throw new InvalidDataException("instruction body is empty");
        if (instructions.Length > MaxInstructionCharacters) throw new InvalidDataException($"instruction body exceeds {MaxInstructionCharacters:N0} characters; no text was truncated");
        return (new SkillMetadata(name, description), instructions);
    }

    private static string Scalar(string value)
    {
        if (value.StartsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(value) ?? ""; }
            catch (JsonException) { throw new InvalidDataException("quoted YAML metadata is malformed"); }
        }
        if (value.StartsWith('\''))
        {
            if (value.Length < 2 || !value.EndsWith('\'')) throw new InvalidDataException("quoted YAML metadata is malformed");
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        return value;
    }

    private sealed record SkillMetadata(string Name, string Description);
}
