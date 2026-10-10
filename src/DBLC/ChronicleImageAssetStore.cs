using System.IO;

namespace DBLC;

/// <summary>Stores original composer images outside Chronicle text and session JSON.</summary>
public sealed class ChronicleImageAssetStore
{
    public const int MaxImageBytes = 12 * 1024 * 1024;
    private readonly string _root = AppDataPaths.File("campaigns");
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private string SessionFolder(Guid campaignId, Guid sessionId) => Path.Combine(_root, campaignId.ToString("N"), "sessions", sessionId.ToString("N"), "chronicle-images");

    public string Save(Guid campaignId, Guid sessionId, string originalName, byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxImageBytes) throw new InvalidDataException("Choose an image smaller than 12 MB.");
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        var isPng = bytes.AsSpan().StartsWith(PngSignature);
        var isJpeg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        if (!(isPng && extension == ".png") && !(isJpeg && extension is ".jpg" or ".jpeg"))
            throw new InvalidDataException("Choose a valid PNG or JPEG image.");

        var folder = SessionFolder(campaignId, sessionId);
        Directory.CreateDirectory(folder);
        var key = Guid.NewGuid().ToString("N") + extension;
        var finalPath = Path.Combine(folder, key);
        var temporaryPath = finalPath + ".tmp";
        File.WriteAllBytes(temporaryPath, bytes);
        File.Move(temporaryPath, finalPath);
        return key;
    }

    public string? ReadDataUrl(Guid campaignId, Guid sessionId, string? key)
    {
        if (!TryGetPath(campaignId, sessionId, key, out var path) || !File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        var mime = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    public void DeleteSession(Guid campaignId, Guid sessionId)
    {
        var folder = SessionFolder(campaignId, sessionId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    public void DeleteAsset(Guid campaignId, Guid sessionId, string key)
    {
        if (TryGetPath(campaignId, sessionId, key, out var path) && File.Exists(path)) File.Delete(path);
    }

    public void CopySession(Guid sourceCampaignId, Guid sourceSessionId, Guid targetCampaignId, Guid targetSessionId)
    {
        var source = SessionFolder(sourceCampaignId, sourceSessionId);
        if (!Directory.Exists(source)) return;
        var destination = SessionFolder(targetCampaignId, targetSessionId);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    public void DeleteCampaign(Guid campaignId)
    {
        var folder = Path.Combine(_root, campaignId.ToString("N"), "sessions");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private bool TryGetPath(Guid campaignId, Guid sessionId, string? key, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(key) || Path.GetFileName(key) != key || key.Contains("..", StringComparison.Ordinal)) return false;
        var extension = Path.GetExtension(key);
        if (extension is not (".png" or ".jpg" or ".jpeg")) return false;
        path = Path.Combine(SessionFolder(campaignId, sessionId), key);
        return true;
    }
}
