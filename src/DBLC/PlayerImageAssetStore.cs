using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DBLC;

public sealed record PlayerImageAsset(string MarkdownPath, string Alt, string DataUrl, PlayerImageFrame Frame);
public sealed record PlayerImageFrame(double X = 0, double Y = 0, double Zoom = 1);

public sealed class PlayerImageAssetStore
{
    private readonly string _root = AppDataPaths.File("campaigns");
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };
    private static string AssetFolder(Guid campaignId) => Path.Combine(campaignId.ToString("N"), "player-assets", "assets");

    private string Assets(Guid campaignId) => Path.Combine(_root, AssetFolder(campaignId));

    public PlayerImageAsset Save(Guid campaignId, string originalName, byte[] originalBytes)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("Choose a PNG, JPG, JPEG, or WebP image.");
        var folder = Assets(campaignId);
        Directory.CreateDirectory(folder);
        var stem = Regex.Replace(Path.GetFileNameWithoutExtension(originalName).ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-', '_');
        if (string.IsNullOrWhiteSpace(stem)) stem = "image";
        var candidate = stem + extension;
        var suffix = 2;
        while (File.Exists(Path.Combine(folder, candidate)) || File.Exists(Path.Combine(folder, candidate + ".frame.json")))
            candidate = $"{stem}-{suffix++}{extension}";
        var path = Path.Combine(folder, candidate);
        File.WriteAllBytes(path, originalBytes);
        var markdownPath = "assets/" + candidate;
        var alt = Regex.Replace(Path.GetFileNameWithoutExtension(candidate), "[-_]+", " ").Trim();
        alt = alt.Length == 0 ? "Image" : char.ToUpperInvariant(alt[0]) + alt[1..];
        return new PlayerImageAsset(markdownPath, alt, ToDataUrl(extension, originalBytes), ReadFramePath(path));
    }

    public PlayerImageAsset? Find(Guid campaignId, string markdownPath)
    {
        if (!TryGetAssetPath(campaignId, markdownPath, out var path) || !File.Exists(path)) return null;
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        return new PlayerImageAsset(markdownPath.Replace('\\', '/'), name, ToDataUrl(extension, File.ReadAllBytes(path)), ReadFramePath(path));
    }

    // Browser portraits use a small authenticated URL rather than embedding the image in every render.
    public string? FindImagePath(Guid campaignId, string markdownPath)
        => TryGetAssetPath(campaignId, markdownPath, out var path) && File.Exists(path) ? path : null;

    public PlayerImageAsset? FindForBrowser(Guid campaignId, string markdownPath)
    {
        if (FindImagePath(campaignId, markdownPath) is not { } path) return null;
        var url = $"/chronicle-portraits/{campaignId:D}/player/{Uri.EscapeDataString(Path.GetFileName(path))}?v={File.GetLastWriteTimeUtc(path).Ticks}";
        return new PlayerImageAsset(markdownPath, "", url, ReadFramePath(path));
    }

    public IReadOnlyDictionary<string, PlayerImageAsset> LoadReferenced(Guid campaignId, string markdown)
    {
        var result = new Dictionary<string, PlayerImageAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(markdown, @"!\[(?<alt>[^\]]*)\]\((?<path>assets/[A-Za-z0-9._-]+)\)"))
        {
            var asset = Find(campaignId, match.Groups["path"].Value);
            if (asset is not null) result[asset.MarkdownPath] = asset with { Alt = match.Groups["alt"].Value };
        }
        return result;
    }

    public string PreserveExistingImageReferences(Guid campaignId, string originalMarkdown, string proposedMarkdown)
    {
        var referenced = Regex.Matches(proposedMarkdown, @"!\[[^\]]*\]\((?<path>assets/[A-Za-z0-9._-]+)\)")
            .Cast<Match>().Select(match => match.Groups["path"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<(int Line, string Markdown, string Path)>();
        foreach (Match match in Regex.Matches(originalMarkdown, @"!\[[^\]]*\]\((?<path>assets/[A-Za-z0-9._-]+)\)"))
        {
            var path = match.Groups["path"].Value;
            if (referenced.Contains(path) || !TryGetAssetPath(campaignId, path, out var file) || !File.Exists(file)) continue;
            var line = originalMarkdown[..match.Index].Count(character => character == '\n');
            missing.Add((line, match.Value, path));
            referenced.Add(path);
        }
        if (missing.Count == 0) return proposedMarkdown;

        var lines = proposedMarkdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        foreach (var image in missing.OrderByDescending(item => item.Line))
            lines.Insert(Math.Clamp(image.Line, 0, lines.Count), image.Markdown);
        return string.Join('\n', lines);
    }

    public void SaveFrame(Guid campaignId, string markdownPath, PlayerImageFrame frame)
    {
        if (!TryGetAssetPath(campaignId, markdownPath, out var path) || !File.Exists(path)) return;
        var safe = new PlayerImageFrame(Math.Clamp(frame.X, -400, 400), Math.Clamp(frame.Y, -400, 400), Math.Clamp(frame.Zoom, 1, 8));
        AtomicTextFile.Write(path + ".frame.json", JsonSerializer.Serialize(safe));
    }

    public void DeleteUnreferenced(Guid campaignId, string previousMarkdown, string currentMarkdown)
    {
        var current = Regex.Matches(currentMarkdown, @"!\[[^\]]*\]\((?<path>assets/[A-Za-z0-9._-]+)\)")
            .Cast<Match>().Select(match => match.Groups["path"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var match in Regex.Matches(previousMarkdown, @"!\[[^\]]*\]\((?<path>assets/[A-Za-z0-9._-]+)\)").Cast<Match>())
        {
            var path = match.Groups["path"].Value;
            if (current.Contains(path) || !TryGetAssetPath(campaignId, path, out var file)) continue;
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + ".frame.json")) File.Delete(file + ".frame.json");
        }
    }

    private PlayerImageFrame ReadFramePath(string imagePath)
    {
        try
        {
            var frame = JsonSerializer.Deserialize<PlayerImageFrame>(File.ReadAllText(imagePath + ".frame.json")) ?? new PlayerImageFrame();
            var zoom = frame.Zoom <= 1.150001 ? 1 : frame.Zoom;
            return frame with { X = Math.Clamp(frame.X, -400, 400), Y = Math.Clamp(frame.Y, -400, 400), Zoom = Math.Clamp(zoom, 1, 8) };
        }
        catch { return new PlayerImageFrame(); }
    }

    private bool TryGetAssetPath(Guid campaignId, string markdownPath, out string path)
    {
        path = "";
        var normalized = markdownPath.Replace('\\', '/');
        if (!normalized.StartsWith("assets/", StringComparison.Ordinal) || normalized.Contains("..", StringComparison.Ordinal)) return false;
        var file = normalized["assets/".Length..];
        if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file || !SupportedExtensions.Contains(Path.GetExtension(file))) return false;
        path = Path.Combine(Assets(campaignId), file);
        return true;
    }

    private static string ToDataUrl(string extension, byte[] bytes)
    {
        var mime = extension.ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream" };
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
