using DBLC;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class MapVisionLiveProbe
{
    public static async Task Run(string temporary)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var props = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:8081/props"));
        var model = props.RootElement.GetProperty("model_alias").GetString() ?? throw new InvalidOperationException("No model alias.");
        Console.WriteLine("Selected model: " + model + "; vision: " + props.RootElement.GetProperty("modalities").GetProperty("vision").GetBoolean());
        var campaign = new Campaign { Name = "Isolated map vision probe" };
        var session = new Conversation { CampaignId = campaign.Id, SessionNumber = 0 };
        var store = new ConversationStore(Path.Combine(temporary, "vision-probe"));
        store.Save(new SavedState("http://127.0.0.1:8081/v1", model, "", null, [session], [campaign], campaign.Id));
        var mapStore = new CampaignMapAssetStore(Path.Combine(temporary, "campaigns"));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(temporary, "archive.db")),
            new ChronicleImageAssetStore(), new CampaignAgendaAuditStore(), new PlayerImageAssetStore(), mapStore);
        var map = state.ImportCampaignMap(campaign.Id, "Imported visual test", "probe.png", DistinctiveImage());
        var client = new LlamaClient();
        var lists = 0;
        var opens = 0;
        var attachedImages = 0;
        async Task<string> Ask(bool enabled)
        {
            var output = new StringBuilder();
            await client.CompleteAsync("http://127.0.0.1:8081/v1", model,
                [new ChatLine { Role = "user", Content = "Please use list_campaign_maps, then open_campaign_map to inspect the imported test map. What central shape and color do you actually see? If vision is unavailable, say that instead. Do not infer the answer from a title." }],
                "", "", () => "No books.", _ => null, _ => null, (_, _, _) => "No pages.", _ => "No companion.", (_, _) => "No sections.",
                () => "No NPCs.", _ => "No NPC.", (_, _, _, _) => "No change.", () => "No sheet.", () => "No workspace.", _ => "No archive.", _ => "No archive.",
                token => output.Append(token), _ => { }, (_, _, _) => { },
                onPresentation: presentation => { output.Clear(); foreach (var segment in presentation.Segments) output.Append(segment.Text); },
                listCampaignMaps: () => { lists++; return state.ListImportedMapsForGm(campaign.Id); },
                openCampaignMap: (id, available) =>
                {
                    opens++;
                    var result = state.OpenImportedMapForGm(campaign.Id, id, enabled && available);
                    if (result.Image is not null) attachedImages++;
                    return result;
                },
                mapVisionEnabled: enabled);
            return output.ToString();
        }
        var seen = await Ask(true);
        Console.WriteLine("Vision-enabled answer: " + seen);
        if (lists < 1 || opens < 1 || attachedImages < 1 || !seen.Contains("triangle", StringComparison.OrdinalIgnoreCase) || !seen.Contains("red", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The GM did not identify the test image from an imported map tool result.");
        var imagesBeforeDisabled = attachedImages;
        var opensBeforeDisabled = opens;
        var disabled = await Ask(false);
        Console.WriteLine("Vision-disabled answer: " + disabled);
        if (opens <= opensBeforeDisabled || attachedImages != imagesBeforeDisabled || (!disabled.Contains("vision", StringComparison.OrdinalIgnoreCase) && !disabled.Contains("cannot", StringComparison.OrdinalIgnoreCase) && !disabled.Contains("can't", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The GM did not acknowledge that the image was unavailable.");
    }

    private static byte[] DistinctiveImage()
    {
        const int size = 256;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var i = (y * size + x) * 4;
            var triangle = y is > 25 and < 225 && Math.Abs(x - 128) < (y - 25) * .48;
            pixels[i] = triangle ? (byte)20 : (byte)255;
            pixels[i + 1] = triangle ? (byte)20 : (byte)255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }
}
