using DBLC;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

internal static class MinistralMessageCompatibilityChecks
{
    private const string Ministral = "Ministral-Compatible-Model";

    public static void Run()
    {
        var buildMessages = typeof(LlamaClient).GetMethod("BuildMessages", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildMessages was not found.");
        var normalize = typeof(LlamaClient).GetMethod("NormalizeMistralMessages", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeMistralMessages was not found.");

        var workbenchHistory = new List<ChatLine>
        {
            new() { Role = "user", Content = "Parent Session context snapshot (background only): The party is in a chapel." },
            new() { Role = "user", Content = "Let's discuss a ruling in this Workbench." }
        };
        var workbench = Build(buildMessages, workbenchHistory, isWorkbench: true);
        normalize.Invoke(null, [workbench, Ministral]);
        Assert(Roles(workbench).SequenceEqual(["system", "user"]), "fresh Workbench background and player turn normalize into a legal Mistral sequence");
        var workbenchUser = (string)workbench[1]!["content"]!;
        Assert(workbenchUser.Contains("Parent Session context snapshot", StringComparison.Ordinal)
            && workbenchUser.Contains("Let's discuss a ruling in this Workbench.", StringComparison.Ordinal)
            && workbenchUser.IndexOf("Parent Session context snapshot", StringComparison.Ordinal) < workbenchUser.IndexOf("Let's discuss a ruling", StringComparison.Ordinal),
            "Workbench snapshot and current request remain distinct and ordered in the normalized prompt");
        Assert(workbenchHistory[0].Content.Contains("background only", StringComparison.Ordinal)
            && workbenchHistory[1].Content == "Let's discuss a ruling in this Workbench.",
            "normalization does not rewrite Workbench history objects");

        var repeated = JsonNode.Parse("""
        [
          {"role":"system","content":"System priority."},
          {"role":"user","content":"Earlier context."},
          {"role":"user","content":"Current request."},
          {"role":"assistant","content":"First assistant fragment."},
          {"role":"assistant","content":"Second assistant fragment."}
        ]
        """)!.AsArray();
        normalize.Invoke(null, [repeated, Ministral]);
        Assert(Roles(repeated).SequenceEqual(["system", "user", "assistant"]), "consecutive user and assistant messages are coalesced for Mistral");
        Assert(((string)repeated[1]!["content"]!).IndexOf("Earlier context.", StringComparison.Ordinal)
                < ((string)repeated[1]!["content"]!).IndexOf("Current request.", StringComparison.Ordinal)
            && ((string)repeated[2]!["content"]!).Contains("Following original assistant message", StringComparison.Ordinal),
            "coalescing preserves message order and explicit boundaries");

        var hierarchy = JsonNode.Parse("""
        [
          {"role":"system","content":"Highest priority rule."},
          {"role":"developer","content":"Developer guidance."},
          {"role":"user","content":"Player request."},
          {"role":"assistant","content":"Reply."}
        ]
        """)!.AsArray();
        normalize.Invoke(null, [hierarchy, Ministral]);
        var hierarchyPrompt = (string)hierarchy[0]!["content"]!;
        Assert(Roles(hierarchy).SequenceEqual(["system", "user", "assistant"])
            && hierarchyPrompt.StartsWith("Highest priority rule.", StringComparison.Ordinal)
            && hierarchyPrompt.Contains("Developer instruction — lower priority than system instructions", StringComparison.Ordinal)
            && hierarchyPrompt.Contains("Developer guidance.", StringComparison.Ordinal),
            "system and developer instructions become supported template input without losing priority");

        var toolExchange = JsonNode.Parse("""
        [
          {"role":"system","content":"System."},
          {"role":"user","content":"Check the rules."},
          {"role":"assistant","content":null,"tool_calls":[{"id":"call_rules","type":"function","function":{"name":"read_pages","arguments":"{\"from_page\":2}"}}]},
          {"role":"tool","tool_call_id":"call_rules","content":"Page text."},
          {"role":"assistant","content":"The page says..."},
          {"role":"user","content":"Thanks."}
        ]
        """)!.AsArray();
        var callSnapshot = toolExchange[2]!.DeepClone().ToJsonString();
        var resultSnapshot = toolExchange[3]!.DeepClone().ToJsonString();
        normalize.Invoke(null, [toolExchange, Ministral]);
        Assert(Roles(toolExchange).SequenceEqual(["system", "user", "assistant", "tool", "assistant", "user"])
            && toolExchange[2]!.ToJsonString() == callSnapshot && toolExchange[3]!.ToJsonString() == resultSnapshot
            && (string?)toolExchange[3]!["tool_call_id"] == "call_rules",
            "native tool call, result, arguments, and call ID remain associated and unchanged");

        var tableHistory = new List<ChatLine>
        {
            new() { Role = "user", Content = "The bell rings." },
            new() { Role = "assistant", Content = "Rain darkens the road." },
            new() { Role = "user", Content = "I look toward the tower." }
        };
        var table = Build(buildMessages, tableHistory, isWorkbench: false);
        var tableSnapshot = table.ToJsonString();
        normalize.Invoke(null, [table, Ministral]);
        Assert(Roles(table).SequenceEqual(["system", "user", "assistant", "user"])
            && !((string)table[0]!["content"]!).Contains("WORKBENCH AWARENESS", StringComparison.Ordinal)
            && table.ToJsonString() == tableSnapshot,
            "ordinary Table conversation with correct alternation remains unchanged");

        var otherModel = JsonNode.Parse("""[{"role":"system","content":"s"},{"role":"user","content":"u1"},{"role":"user","content":"u2"}]""")!.AsArray();
        var otherSnapshot = otherModel.ToJsonString();
        normalize.Invoke(null, [otherModel, "Gemma-Compatible-Model"]);
        Assert(otherModel.ToJsonString() == otherSnapshot, "message normalization is limited to Mistral-family models");
    }

    public static async Task RunLiveAsync()
    {
        var history = new List<ChatLine>
        {
            new() { Role = "user", Content = "Parent Session context snapshot (background only): The party is discussing a house rule." },
            new() { Role = "user", Content = "In this temporary Workbench, reply with exactly: Workbench request received." }
        };
        var text = new StringBuilder();
        GmPresentation? presentation = null;
        await new LlamaClient().CompleteAsync(
            "http://127.0.0.1:8081/v1", Ministral, history, "", "",
            listBooks: () => "No books are available.",
            getBookTitle: _ => null,
            getCatalogue: _ => "No catalogue is available.",
            readPages: (_, _, _) => "No pages are available.",
            getJsonCatalogue: _ => "No JSON catalogue is available.",
            readJsonSections: (_, _) => "No JSON sections are available.",
            listNpcs: () => "No NPCs are available.",
            readNpc: _ => "No NPC card is available.",
            editNpc: (_, _, _, _) => "NPC editing is unavailable.",
            readPlayer: () => "The Player Sheet is empty.",
            readWorkspace: () => "Workspace Notes are empty.",
            searchArchive: _ => "No archive result.",
            readArchive: _ => "No archive lines.",
            onToken: token => text.Append(token),
            onStatus: _ => { },
            onUsage: (_, _, _) => { },
            gmProfile: AppDefaults.DefaultGmProfile,
            onPresentation: result => presentation = result,
            isWorkbench: true);

        var response = presentation is null
            ? text.ToString()
            : string.Join("\n", presentation.Segments.Select(segment => segment.Text));
        if (string.IsNullOrWhiteSpace(response)) throw new InvalidOperationException("Live Mistral Workbench request returned no user-facing response.");
        Console.WriteLine($"Live Ministral Workbench request succeeded; reply characters: {response.Length}.");
    }

    private static JsonArray Build(MethodInfo method, IReadOnlyList<ChatLine> history, bool isWorkbench)
    {
        var args = method.GetParameters().Select(parameter => parameter.Name switch
        {
            "history" => (object)history,
            "archiveSummary" => "",
            "isWorkbench" => isWorkbench,
            _ when parameter.HasDefaultValue => parameter.DefaultValue,
            _ => throw new InvalidOperationException($"No test value for BuildMessages parameter {parameter.Name}.")
        }).ToArray();
        return (JsonArray)(method.Invoke(null, args) ?? throw new InvalidOperationException("BuildMessages returned null."));
    }

    private static string[] Roles(JsonArray messages) => messages.OfType<JsonObject>().Select(message => (string?)message["role"] ?? "").ToArray();

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Ministral message compatibility check failed: " + description);
    }
}
