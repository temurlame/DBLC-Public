using DBLC;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var temporary = Path.Combine(Path.GetTempPath(), "dblc-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var results = new List<object>();
try
{
    if (args.Contains("--live")) { await LiveProbe.Run(root, temporary); return; }
    if (args.Contains("--live-map")) { await MapVisionLiveProbe.Run(temporary); return; }
    if (args.Contains("--targeted-edits")) { await Checks.RunTargetedEditChecks(temporary); return; }
    if (args.Contains("--skills")) { await Checks.RunSkillChecks(root, temporary); return; }
    if (args.Contains("--maps")) { await Checks.RunMapRemovalChecks(root, temporary); return; }
    if (args.Contains("--large-retrieval"))
    {
        await (Task)typeof(Checks).GetMethod("LargeRetrievalChecks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [temporary])!;
        return;
    }
    if (args.Contains("--rules-index")) { Checks.RulesIndexChecks(temporary); Console.WriteLine("Rules index checks passed."); return; }
    const string markdown = "## The room\n\nA **bold and *uneasy*** voice. Ordinary * and _ and `code`.\n\n- One\n- Two\n  - Nested\n\n```text\n<untrusted> & test\n```\n";
    Measure("chronicle_100_markdown_renders", 5, () => { for (var i = 0; i < 100; i++) ChronicleMarkdownRenderer.Render(markdown); });
    Measure("glitch_20_renders", 5, () => { for (var i = 0; i < 20; i++) ChronicleMarkdownRenderer.RenderGlitched(markdown, "cursed", 42, "fixture"); });
    var rendered = Enumerable.Range(0, 100).Select(_ => new ChronicleProbe()).ToArray();
    Measure("chronicle_100_unchanged_components", 100, () => { foreach (var component in rendered) component.Update(markdown); });
    Measure("glitch_20_unchanged_components", 100, () => { foreach (var component in rendered.Take(20)) component.Update(markdown, "GLITCH_CURSED", 42); });
    var conversation = new Conversation { Lines = Enumerable.Range(0, 200).Select(i => new ChatLine { Sequence = i + 1, Role = i % 2 == 0 ? "user" : "assistant", Content = string.Concat(Enumerable.Repeat(markdown, 5)) }).ToList() };
    var state = new SavedState("http://127.0.0.1:1/v1", "fixture", "", null, [conversation], [new Campaign { Name = "Fixture", SavedMaps = [JsonNode.Parse("""{"Id":"legacy-fixture","Sketch":{"Features":{"G7":"table"}}}""")!.AsObject()] }]);
    var store = new ConversationStore(temporary);
    Measure("save_200_message_campaign", 30, () => store.Save(state));
    Measure("load_200_message_campaign", 30, () => store.Load());
    var manyNotes = Enumerable.Range(0, 20_000).Select(index => new GmNote
    {
        Text = $"Campaign note {index:D5}\n" + string.Concat(Enumerable.Repeat("Ordinary reference material for this campaign. ", 10))
            + (index == 19_999 ? "Example NPC's basement bargain." : "")
    }).ToList();
    Measure("gm_note_search_20000", 10, () => GmNoteTransfer.Search(manyNotes, "Example NPC basement"));
    var toolsMethod = typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!;
    var toolSchemas = (JsonArray)toolsMethod.Invoke(null, [false])!;
    results.Add(new { name = "tool_schema_size", tools = toolSchemas.Count, serialized_characters = toolSchemas.ToJsonString().Length,
        largest = toolSchemas.OfType<JsonObject>().Select(tool => new { tool = (string?)tool["function"]?["name"], characters = tool.ToJsonString().Length })
            .OrderByDescending(item => item.characters).Take(8).ToArray() });
    Measure("build_tool_definitions", 100, () => toolsMethod.Invoke(null, [false]));
    var messagesMethod = typeof(LlamaClient).GetMethod("BuildMessages", BindingFlags.NonPublic | BindingFlags.Static)!;
    var messageArgs = messagesMethod.GetParameters().Select(p => p.Name == "history" ? (object)conversation.Lines : p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(bool) ? false : (object)"").ToArray();
    Measure("assemble_200_message_context", 100, () => messagesMethod.Invoke(null, messageArgs));
    var longChronicle = new Conversation { Lines = Enumerable.Range(0, 1_000).Select(index => new ChatLine
    {
        Sequence = index + 1, Role = index % 2 == 0 ? "user" : "assistant", Content = markdown,
        PresentationSegments = index % 2 == 0 ? null : [new GmPresentationSegment { Speaker = "GM", Text = markdown }]
    }).ToList() };
    Measure("mobile_snapshot_1000_lines", 30, () => MobileChronicleSnapshot.Capture(longChronicle));
    await Checks.Run(root, temporary);
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(temporary, true); }

void Measure(string name, int iterations, Action operation)
{
    for (var i = 0; i < 3; i++) operation();
    var allocations = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++) operation();
    timer.Stop();
    results.Add(new { name, iterations, mean_ms = timer.Elapsed.TotalMilliseconds / iterations, allocated_bytes_per_operation = (GC.GetAllocatedBytesForCurrentThread() - allocations) / iterations });
}
