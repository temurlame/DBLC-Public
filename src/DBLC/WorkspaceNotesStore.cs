using System.IO;
using System.Text;

namespace DBLC;

public sealed class WorkspaceNotesStore
{
    private readonly string _folder = AppDataPaths.Root;
    private string LegacyFilePath => Path.Combine(_folder, "workspace.md");
    private string CampaignFilePath(Guid campaignId) => Path.Combine(_folder, "campaigns", campaignId.ToString("N"), "workspace.md");

    public string Load(Guid campaignId, bool useLegacyFallback = false)
    {
        var path = CampaignFilePath(campaignId);
        var text = ReadIfPresent(path);
        return text ?? (useLegacyFallback ? ReadIfPresent(LegacyFilePath) : null) ?? "";
    }

    private static string? ReadIfPresent(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(Guid campaignId, string notes)
    {
        var path = CampaignFilePath(campaignId);
        AtomicTextFile.Write(path, notes);
    }

    public void DuplicateCampaign(Guid sourceId, Guid destinationId, bool useLegacyFallback = false)
    {
        var source = CampaignFilePath(sourceId);
        if (!File.Exists(source) && useLegacyFallback && File.Exists(LegacyFilePath)) source = LegacyFilePath;
        if (!File.Exists(source)) return;
        var destination = CampaignFilePath(destinationId);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    public void DeleteCampaign(Guid campaignId, bool deleteLegacyFallback = false)
    {
        var path = CampaignFilePath(campaignId);
        if (File.Exists(path)) File.Delete(path);
        if (deleteLegacyFallback && File.Exists(LegacyFilePath)) File.Delete(LegacyFilePath);
    }
}
