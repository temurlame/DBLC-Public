using System.IO;
using System.Text;
using System.Text.Json;

namespace DBLC;

public sealed class PlayerSheetStore
{
    private readonly string _folder = AppDataPaths.Root;
    private string LegacyFile => Path.Combine(_folder, "player-sheet.md");
    private string CampaignFile(Guid campaignId) => Path.Combine(_folder, "campaigns", campaignId.ToString("N"), "player-sheet.md");
    private string PreviousCampaignFile(Guid campaignId) => Path.Combine(_folder, "campaigns", campaignId.ToString("N"), "player-sheet.previous.md");
    private string CampaignAssets(Guid campaignId) => Path.Combine(_folder, "campaigns", campaignId.ToString("N"), "player-assets");

    public string Load(Guid campaignId, bool useLegacyFallback = false)
    {
        var path = CampaignFile(campaignId);
        var text = ReadIfPresent(path);
        return text ?? (useLegacyFallback ? ReadIfPresent(LegacyFile) : null) ?? "";
    }

    private static string? ReadIfPresent(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(Guid campaignId, string text)
    {
        var path = CampaignFile(campaignId);
        AtomicTextFile.Write(path, text);
    }

    public bool HasPrevious(Guid campaignId) => File.Exists(PreviousCampaignFile(campaignId));

    public void ClearPrevious(Guid campaignId)
    {
        var backup = PreviousCampaignFile(campaignId);
        TryDelete(backup);
    }

    public void ReplaceWithBackup(Guid campaignId, string current, string replacement)
    {
        var path = CampaignFile(campaignId);
        var backup = PreviousCampaignFile(campaignId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backupTemp = backup + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var replacementTemp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(backupTemp, current, new UTF8Encoding(false));
            File.WriteAllText(replacementTemp, replacement, new UTF8Encoding(false));
            File.Move(backupTemp, backup, true);
            File.Move(replacementTemp, path, true);
        }
        finally
        {
            TryDelete(backupTemp);
            TryDelete(replacementTemp);
        }
    }

    public string RestorePrevious(Guid campaignId)
    {
        var backup = PreviousCampaignFile(campaignId);
        if (!File.Exists(backup)) throw new FileNotFoundException("There is no previous Player sheet to restore.");
        var previous = File.ReadAllText(backup);
        Save(campaignId, previous);
        TryDelete(backup);
        return previous;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    public void DuplicateCampaign(Guid sourceId, Guid destinationId, bool useLegacyFallback = false)
    {
        var source = CampaignFile(sourceId);
        if (!File.Exists(source) && useLegacyFallback && File.Exists(LegacyFile)) source = LegacyFile;
        if (File.Exists(source))
        {
            var destination = CampaignFile(destinationId);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
        CopyDirectory(CampaignAssets(sourceId), CampaignAssets(destinationId));
    }

    public void DeleteCampaign(Guid campaignId, bool deleteLegacyFallback = false)
    {
        var path = CampaignFile(campaignId);
        if (File.Exists(path)) File.Delete(path);
        var previous = PreviousCampaignFile(campaignId);
        if (File.Exists(previous)) File.Delete(previous);
        var assets = CampaignAssets(campaignId);
        if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);
        if (deleteLegacyFallback && File.Exists(LegacyFile)) File.Delete(LegacyFile);
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

public sealed class ViewOrderStore
{
    public static readonly string[] DefaultOrder = ["Player", "Table", "Workspace"];
    private readonly string _file = AppDataPaths.File("view-order.json");
    public List<string> Load()
    {
        try { if (File.Exists(_file)) return Normalize(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file)) ?? []); } catch { }
        return DefaultOrder.ToList();
    }
    public void Save(IEnumerable<string> order)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(Normalize(order), new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
    private static List<string> Normalize(IEnumerable<string> order)
    {
        var valid = new HashSet<string>(DefaultOrder, StringComparer.OrdinalIgnoreCase);
        var result = order.Where(x => valid.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.AddRange(DefaultOrder.Where(x => !result.Contains(x, StringComparer.OrdinalIgnoreCase)));
        return result;
    }
}
