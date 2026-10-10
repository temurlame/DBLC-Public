using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DBLC;

public sealed class ImportedCampaignMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Map";
    public string Extension { get; set; } = ".png";
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>Originals and small derivatives live outside campaign JSON. Paths are derived only from IDs.</summary>
public sealed class CampaignMapAssetStore
{
    public const int MaxImageBytes = 30 * 1024 * 1024;
    private readonly string _root;
    public CampaignMapAssetStore() : this(AppDataPaths.File("campaigns")) { }
    public CampaignMapAssetStore(string root) => _root = root;
    private string Folder(Guid campaignId) => Path.Combine(_root, campaignId.ToString("N"), "imported-maps");
    private static string Mime(string extension) => extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
    private string PathFor(Guid campaignId, ImportedCampaignMap map, string kind) => Path.Combine(Folder(campaignId), map.Id.ToString("N") + (kind == "original" ? map.Extension : kind == "thumbnail" ? ".thumb.jpg" : ".vision.jpg"));

    public ImportedCampaignMap Import(Guid campaignId, string title, string originalName, byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxImageBytes) throw new InvalidDataException("Choose a map image smaller than 30 MB.");
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new InvalidDataException("Choose a PNG, JPEG, or WebP map.");
        if (extension == ".jpeg") extension = ".jpg";
        var png = bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var jpg = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        var webp = bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (!(png && extension == ".png") && !(jpg && extension == ".jpg") && !(webp && extension == ".webp"))
            throw new InvalidDataException("The file content does not match its image type.");
        using var stream = new MemoryStream(bytes, writable: false);
        BitmapFrame frame;
        try { frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]; }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        { throw new InvalidDataException("Windows could not decode this image. For WebP, install the Windows WebP image extension or convert it to PNG.", ex); }
        if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || (long)frame.PixelWidth * frame.PixelHeight > 64_000_000)
            throw new InvalidDataException("The image dimensions are unsupported.");
        title = title.Trim();
        if (title.Length is 0 or > 100 || title.Any(char.IsControl)) throw new InvalidDataException("Give the map a title of 1–100 printable characters.");
        var map = new ImportedCampaignMap { Title = title, Extension = extension, Width = frame.PixelWidth, Height = frame.PixelHeight };
        var folder = Folder(campaignId);
        Directory.CreateDirectory(folder);
        var originals = PathFor(campaignId, map, "original");
        var thumb = PathFor(campaignId, map, "thumbnail");
        var vision = PathFor(campaignId, map, "vision");
        var suffix = "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(originals + suffix, bytes);
            File.WriteAllBytes(thumb + suffix, ResizeJpeg(frame, 320));
            File.WriteAllBytes(vision + suffix, ResizeJpeg(frame, 2048));
            File.Move(originals + suffix, originals);
            File.Move(thumb + suffix, thumb);
            File.Move(vision + suffix, vision);
            return map;
        }
        catch
        {
            foreach (var path in new[] { originals, thumb, vision }) { if (File.Exists(path + suffix)) File.Delete(path + suffix); if (File.Exists(path)) File.Delete(path); }
            throw;
        }
    }

    private static byte[] ResizeJpeg(BitmapFrame frame, int maxSide)
    {
        var factor = Math.Min(1.0, (double)maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource source = factor < 1 ? new TransformedBitmap(frame, new ScaleTransform(factor, factor)) : frame;
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public (string Path, string Mime)? Locate(Guid campaignId, ImportedCampaignMap map, string kind)
    {
        if (map.Id == Guid.Empty || map.Extension is not (".png" or ".jpg" or ".webp") || kind is not ("original" or "thumbnail" or "vision")) return null;
        var path = PathFor(campaignId, map, kind);
        return File.Exists(path) ? (path, kind == "original" ? Mime(map.Extension) : "image/jpeg") : null;
    }
    public string? DataUrl(Guid campaignId, ImportedCampaignMap map, string kind)
    {
        var located = Locate(campaignId, map, kind);
        return located is null ? null : $"data:{located.Value.Mime};base64,{Convert.ToBase64String(File.ReadAllBytes(located.Value.Path))}";
    }
    public void Delete(Guid campaignId, ImportedCampaignMap map)
    {
        foreach (var kind in new[] { "original", "thumbnail", "vision" })
        {
            var located = Locate(campaignId, map, kind);
            if (located is not null) File.Delete(located.Value.Path);
        }
    }
    public void CopyCampaign(Guid source, Guid destination)
    {
        var from = Folder(source);
        if (!Directory.Exists(from)) return;
        var to = Folder(destination);
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
    }
    public void DeleteCampaign(Guid campaignId)
    {
        var folder = Folder(campaignId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
