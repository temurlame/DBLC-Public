using System.IO;

namespace DBLC;

public sealed class NpcPortraitStore
{
    private readonly string _root = AppDataPaths.File("campaigns");
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };
    private static string FrameFile(string portraitPath) => portraitPath + ".frame.json";

    private string NpcFolder(Guid campaignId, Guid npcId) => Path.Combine(_root, campaignId.ToString("N"), "npcs", npcId.ToString("N"));
    private string CampaignNpcFolder(Guid campaignId) => Path.Combine(_root, campaignId.ToString("N"), "npcs");
    private string BestiaryFolder(Guid campaignId, Guid entryId) => Path.Combine(_root, campaignId.ToString("N"), "bestiary", entryId.ToString("N"));
    private string CampaignBestiaryFolder(Guid campaignId) => Path.Combine(_root, campaignId.ToString("N"), "bestiary");

    public string Save(Guid campaignId, Guid npcId, string sourcePath)
        => SavePortrait(NpcFolder(campaignId, npcId), sourcePath);

    public string Save(Guid campaignId, Guid npcId, string fileName, Stream source)
        => SavePortrait(NpcFolder(campaignId, npcId), Path.GetExtension(fileName), source);

    public Task<string> SaveAsync(Guid campaignId, Guid npcId, string fileName, Stream source)
        => SavePortraitAsync(NpcFolder(campaignId, npcId), Path.GetExtension(fileName), source);

    public string ReplaceNpcPortrait(Guid campaignId, Guid npcId, string sourcePath, Action<string> commit)
    {
        var folder = NpcFolder(campaignId, npcId);
        return ReplaceWithRollback(folder, () => SavePortrait(folder, sourcePath), commit);
    }

    public async Task<string> ReplaceNpcPortraitAsync(Guid campaignId, Guid npcId, string fileName, Stream source, Action<string> commit)
    {
        var folder = NpcFolder(campaignId, npcId);
        var backup = BackupPortrait(folder);
        var retainBackup = false;
        try
        {
            var extension = await SavePortraitAsync(folder, Path.GetExtension(fileName), source);
            commit(extension);
            return extension;
        }
        catch
        {
            try { RestorePortrait(folder, backup); }
            catch { retainBackup = true; throw; }
            throw;
        }
        finally { if (!retainBackup) TryDeleteBackup(backup); }
    }

    private static string ReplaceWithRollback(string folder, Func<string> save, Action<string> commit)
    {
        var backup = BackupPortrait(folder);
        var retainBackup = false;
        try
        {
            var extension = save();
            commit(extension);
            return extension;
        }
        catch
        {
            try { RestorePortrait(folder, backup); }
            catch { retainBackup = true; throw; }
            throw;
        }
        finally { if (!retainBackup) TryDeleteBackup(backup); }
    }

    private static string BackupPortrait(string folder)
    {
        var backup = Path.Combine(folder, ".portrait-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "portrait.*"))
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
        }
        catch { TryDeleteBackup(backup); throw; }
        return backup;
    }

    private static void RestorePortrait(string folder, string backup)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "portrait.*")) File.Delete(file);
        foreach (var file in Directory.EnumerateFiles(backup)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
    }

    private static void TryDeleteBackup(string backup)
    {
        try { Directory.Delete(backup, recursive: true); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Portrait backup cleanup deferred: {0}", error.Message); }
    }

    public string SaveBestiaryPortrait(Guid campaignId, Guid entryId, string sourcePath)
        => SavePortrait(BestiaryFolder(campaignId, entryId), sourcePath);

    public string SaveBestiaryPortrait(Guid campaignId, Guid entryId, string fileName, Stream source)
        => SavePortrait(BestiaryFolder(campaignId, entryId), Path.GetExtension(fileName), source);

    public Task<string> SaveBestiaryPortraitAsync(Guid campaignId, Guid entryId, string fileName, Stream source)
        => SavePortraitAsync(BestiaryFolder(campaignId, entryId), Path.GetExtension(fileName), source);

    private string SavePortrait(string folder, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("Choose a PNG, JPG, JPEG, or WebP image.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected portrait could not be found.", sourcePath);

        using var source = File.OpenRead(sourcePath);
        return SavePortrait(folder, extension, source);
    }

    private string SavePortrait(string folder, string extension, Stream source)
    {
        extension = extension.ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("Choose a PNG, JPG, JPEG, or WebP image.");

        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "portrait" + extension);
        var temporary = Path.Combine(folder, "portrait.uploading");
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                source.CopyTo(output);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        foreach (var oldPortrait in Directory.EnumerateFiles(folder, "portrait.*").Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)))
            File.Delete(oldPortrait);
        if (File.Exists(FrameFile(destination))) File.Delete(FrameFile(destination));
        return extension;
    }

    private async Task<string> SavePortraitAsync(string folder, string extension, Stream source)
    {
        extension = extension.ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("Choose a PNG, JPG, JPEG, or WebP image.");

        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "portrait" + extension);
        var temporary = Path.Combine(folder, "portrait.uploading");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await source.CopyToAsync(output);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        foreach (var oldPortrait in Directory.EnumerateFiles(folder, "portrait.*").Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)))
            File.Delete(oldPortrait);
        if (File.Exists(FrameFile(destination))) File.Delete(FrameFile(destination));
        return extension;
    }
    public NpcPortraitFrame ReadFrame(Guid campaignId, Guid npcId, string? extension)
        => ReadFramePath(PortraitPath(NpcFolder(campaignId, npcId), extension));

    public NpcPortraitFrame ReadBestiaryFrame(Guid campaignId, Guid entryId, string? extension)
        => ReadFramePath(PortraitPath(BestiaryFolder(campaignId, entryId), extension));

    private NpcPortraitFrame ReadFramePath(string? portrait)
    {
        if (portrait is null || !File.Exists(portrait)) return new NpcPortraitFrame();
        try
        {
            var frame = System.Text.Json.JsonSerializer.Deserialize<NpcPortraitFrame>(File.ReadAllText(FrameFile(portrait))) ?? new NpcPortraitFrame();
            var zoom = frame.Zoom <= 1.150001 ? 1 : frame.Zoom;
            return frame with { X = Math.Clamp(frame.X, -400, 400), Y = Math.Clamp(frame.Y, -400, 400), Zoom = Math.Clamp(zoom, 1, 8) };
        }
        catch { return new NpcPortraitFrame(); }
    }

    public void SaveFrame(Guid campaignId, Guid npcId, string? extension, NpcPortraitFrame frame)
        => SaveFramePath(PortraitPath(NpcFolder(campaignId, npcId), extension), frame);

    public void SaveBestiaryFrame(Guid campaignId, Guid entryId, string? extension, NpcPortraitFrame frame)
        => SaveFramePath(PortraitPath(BestiaryFolder(campaignId, entryId), extension), frame);

    private void SaveFramePath(string? portrait, NpcPortraitFrame frame)
    {
        if (portrait is null || !File.Exists(portrait)) return;
        var safe = new NpcPortraitFrame(Math.Clamp(frame.X, -400, 400), Math.Clamp(frame.Y, -400, 400), Math.Clamp(frame.Zoom, 1, 8));
        AtomicTextFile.Write(FrameFile(portrait), System.Text.Json.JsonSerializer.Serialize(safe));
    }

    public string? FindImagePath(Guid campaignId, Guid npcId, string? extension)
    {
        var path = PortraitPath(NpcFolder(campaignId, npcId), extension);
        return path is not null && File.Exists(path) ? path : null;
    }

    public string? BrowserImageUrl(Guid campaignId, Guid npcId, string? extension)
    {
        var path = FindImagePath(campaignId, npcId, extension);
        return path is null ? null : $"/chronicle-portraits/{campaignId:D}/npc/{npcId:D}?v={File.GetLastWriteTimeUtc(path).Ticks}";
    }

    public string? ReadDataUrl(Guid campaignId, Guid npcId, string? extension)
        => ReadDataUrlPath(PortraitPath(NpcFolder(campaignId, npcId), extension));

    public string? ReadBestiaryDataUrl(Guid campaignId, Guid entryId, string? extension)
        => ReadDataUrlPath(PortraitPath(BestiaryFolder(campaignId, entryId), extension));

    private static string? ReadDataUrlPath(string? path)
    {
        if (path is null) return null;
        if (!File.Exists(path)) return null;
        var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream" };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private static string? PortraitPath(string folder, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        if (!SupportedExtensions.Contains(normalized)) return null;
        return Path.Combine(folder, "portrait" + normalized.ToLowerInvariant());
    }

    public void DeletePortrait(Guid campaignId, Guid npcId)
    {
        var folder = NpcFolder(campaignId, npcId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    public void DeleteBestiaryPortrait(Guid campaignId, Guid entryId)
    {
        var folder = BestiaryFolder(campaignId, entryId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    public void DuplicateCampaign(Guid sourceCampaignId, Guid destinationCampaignId)
    {
        var source = CampaignNpcFolder(sourceCampaignId);
        if (Directory.Exists(source))
        {
            var destination = CampaignNpcFolder(destinationCampaignId);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyDirectory(source, destination);
        }
        var bestiarySource = CampaignBestiaryFolder(sourceCampaignId);
        if (Directory.Exists(bestiarySource)) CopyDirectory(bestiarySource, CampaignBestiaryFolder(destinationCampaignId));
    }

    public void DeleteCampaign(Guid campaignId)
    {
        var folder = CampaignNpcFolder(campaignId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        var bestiary = CampaignBestiaryFolder(campaignId);
        if (Directory.Exists(bestiary)) Directory.Delete(bestiary, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

public sealed record NpcPortraitFrame(double X = 0, double Y = 0, double Zoom = 1);
