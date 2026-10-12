using DBLC;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class Checks
{
    internal static async Task RunMapRemovalChecks(string root, string temporary)
    {
        var schema = (JsonArray)typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
        var names = schema.OfType<JsonObject>().Select(tool => (string?)tool["function"]?["name"]).ToHashSet(StringComparer.Ordinal);
        Require(!names.Any(name => name is not null && (name.Contains("sketch_map", StringComparison.OrdinalIgnoreCase) || name is "list_saved_maps" or "load_saved_map" or "read_saved_map"))
            && names.Contains("list_campaign_maps") && names.Contains("open_campaign_map"),
            "GM exposes imported-image map tools but no sketch-map tools");

        var data = Path.Combine(temporary, "map-removal");
        var campaign = new Campaign { Name = "Imported map fixture", SavedMaps = [JsonNode.Parse("""{"Id":"old-sketch","Name":"Legacy sketch","Sketch":{"Features":{"G7":"table"}}}""")!.AsObject()] };
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1" };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [conversation], [campaign], campaign.Id));
        var campaignsRoot = Path.Combine(data, "campaigns");
        var imageStore = Isolate(new ChronicleImageAssetStore(), "_root", campaignsRoot);
        var agendaAudit = Isolate(new CampaignAgendaAuditStore(), "_root", campaignsRoot);
        var playerImages = Isolate(new PlayerImageAssetStore(), "_root", campaignsRoot);
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")),
            imageStore, agendaAudit, playerImages, new CampaignMapAssetStore(campaignsRoot));
        var imported = state.ImportCampaignMap(campaign.Id, "Cellar", "cellar.png", TestMapPng());
        Require(state.GetImportedMaps(campaign.Id).Single().Id == imported.Id
            && state.GetMapDisplayEntries(campaign.Id).Single().Title == "Cellar"
            && state.GetOwnerMapDataUrl(campaign.Id, imported.Id, "thumbnail")?.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) == true,
            "imported image metadata, thumbnail and Maps catalogue remain operational");
        state.UpdateImportedMap(campaign.Id, imported.Id, "Old cellar");
        Require(state.GetImportedMap(campaign.Id, imported.Id)?.Title == "Old cellar"
            && state.GetCampaignMapImage(campaign.Id, imported.Id, "original") is not null,
            "rename retains the original imported image");

        var unavailable = state.OpenImportedMapForGm(campaign.Id, imported.Id.ToString(), false);
        var available = state.OpenImportedMapForGm(campaign.Id, imported.Id.ToString(), true);
        Require(unavailable.Image is null && unavailable.Text.Contains("Vision is unavailable", StringComparison.Ordinal)
            && available.Image is { MimeType: "image/jpeg" } image && image.DataUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal),
            "vision-disabled GM receives no image, while vision-enabled GM access supplies the actual bounded image");

        using (var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("open_campaign_map", JsonSerializer.Serialize(new { map_id = imported.Id.ToString() })))
            : Text("I have inspected the map.")))
        {
            bool? visionChecked = null;
            await RunModel(server, new StringBuilder(), listCampaignMaps: () => state.ListImportedMapsForGm(campaign.Id),
                openCampaignMap: (id, canSee) => { visionChecked = canSee; return state.OpenImportedMapForGm(campaign.Id, id, canSee); }, mapVisionEnabled: true);
            var toolResult = server.Requests[1]["messages"]!.AsArray().OfType<JsonObject>().Last(message => (string?)message["role"] == "tool");
            var blocks = toolResult["content"]!.AsArray();
            var imageBlock = blocks.OfType<JsonObject>().Single(block => (string?)block["type"] == "image_url");
            Require(visionChecked == true && server.Paths.Contains("/props", StringComparer.Ordinal)
                && ((string?)imageBlock["image_url"]?["url"])?.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) == true,
                "open_campaign_map continuation sends actual image data to a vision-capable GM request");
        }
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("open_campaign_map", JsonSerializer.Serialize(new { map_id = imported.Id.ToString() })))
            : Text("I cannot inspect the image here.")))
        {
            bool? visionChecked = null;
            await RunModel(server, new StringBuilder(), openCampaignMap: (id, canSee) => { visionChecked = canSee; return state.OpenImportedMapForGm(campaign.Id, id, canSee); }, mapVisionEnabled: false);
            var toolResult = server.Requests[1]["messages"]!.AsArray().OfType<JsonObject>().Last(message => (string?)message["role"] == "tool");
            Require(visionChecked == false && !server.Paths.Contains("/props", StringComparer.Ordinal)
                && toolResult["content"] is JsonValue,
                "vision-disabled open does not fetch server capability or attach an image");
        }

        var persisted = store.Load().Campaigns!.Single();
        Require(persisted.ImportedMaps.Single().Id == imported.Id
            && persisted.SavedMaps.Single()["Sketch"]?["Features"]?["G7"]?.GetValue<string>() == "table",
            "imported maps persist and old saved-sketch records remain intact as inert campaign data");
        var shell = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        var mobileHost = File.ReadAllText(Path.Combine(root, "src", "DBLC", "MobileWebHostManager.cs"));
        Require(!shell.Contains("CurrentSketchMap", StringComparison.Ordinal) && !shell.Contains("ShowSketchMapViewer", StringComparison.Ordinal)
            && shell.Contains("IMPORTED IMAGES", StringComparison.Ordinal) && shell.Contains("ImageViewer IsOpen=\"ShowImportedMapViewer\"", StringComparison.Ordinal)
            && mobileHost.Contains("/campaign-maps/{campaignId:guid}/{mapId:guid}/{kind}", StringComparison.Ordinal),
            "Maps UI retains the imported image viewer and mobile image route without sketch UI");
        Console.Error.WriteLine($"PASS: {_checks} focused map assertions.");
    }
}
