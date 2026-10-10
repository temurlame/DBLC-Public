using DBLC;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

internal static partial class Checks
{
    private static async Task EfficiencyRemainingChecks(string temporary)
    {
        var surprise = new SurpriseMachineData
        {
            Enabled = true, EligibilityMinimum = 1, EligibilityMaximum = 1,
            EventProbabilityMinimumPercent = 100, EventProbabilityMaximumPercent = 100,
            InterruptionDurationMinimum = 2, InterruptionDurationMaximum = 2,
            CooldownMinimum = 1, CooldownMaximum = 1, CampaignTriggerChancePercent = 0,
            MagnitudeWeights = SurpriseDefaults.Magnitudes.ToDictionary(value => value, value => value == "MINOR" ? 1d : 0d),
            SourceWeights = SurpriseDefaults.Sources.ToDictionary(value => value, value => value == "STRUCTURE" ? 1d : 0d),
            ChangeWeights = SurpriseDefaults.Changes.ToDictionary(value => value, value => value == "BREAKS" || value == "DEMANDS" ? 1d : 0d),
            RelationWeights = SurpriseDefaults.Relations.ToDictionary(value => value, value => value == "UNRELATED" ? 1d : 0d)
        };
        var firstSurprise = SurpriseMachine.Advance(surprise);
        Require(firstSurprise is not null && firstSurprise.Contains("MAGNITUDE: MINOR", StringComparison.Ordinal)
            && firstSurprise.Contains("SOURCE: STRUCTURE", StringComparison.Ordinal)
            && firstSurprise.Contains("CHANGE: BREAKS", StringComparison.Ordinal)
            && !firstSurprise.Contains("CHANGE: DEMANDS", StringComparison.Ordinal)
            && surprise.InterruptionLeaseRemaining == 2, "forced surprise honors weights and excludes impossible structure demands");
        Require(SurpriseMachine.Advance(surprise) is null && surprise.InterruptionLeaseRemaining == 1
            && SurpriseMachine.Advance(surprise) is null && surprise.Phase == "COOLDOWN"
            && SurpriseMachine.Advance(surprise) is null && surprise.Phase == "QUIET"
            && SurpriseMachine.Advance(surprise) is not null,
            "surprise suppression and cooldown count subsequent completed beats before another eligible event");
        surprise.Enabled = false;
        var pausedBeats = surprise.BeatCount;
        Require(SurpriseMachine.Advance(surprise) is null && surprise.BeatCount == pausedBeats,
            "disabled surprise does not advance its cycle");

        var chooseKeep = typeof(AppState).GetMethod("CompactionKeepCount", BindingFlags.NonPublic | BindingFlags.Static)!;
        var even = Enumerable.Range(0, 20).Select(index => new ChatLine { Sequence = index + 1, Content = new string('a', 100) }).ToList();
        var uneven = Enumerable.Range(0, 20).Select(index => new ChatLine { Sequence = index + 1, Content = new string('a', index == 10 ? 20_000 : 100) }).ToList();
        Require((int)chooseKeep.Invoke(null, [even])! == 10 && (int)chooseKeep.Invoke(null, [uneven])! == 8,
            "compaction keeps the usual recent tail for even text but removes a large older pair before the tail");

        var reads = 0;
        using (var finalPass = new ModelStub((_, pass) => pass < 7 ? Calls(("read_player", "{}")) : Text("The GM answers from the gathered material.")))
        {
            var spoken = new System.Text.StringBuilder();
            await new LlamaClient().CompleteAsync(finalPass.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Look up the answer." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => { reads++; return "Small sheet"; }, () => "Workspace",
                _ => "search", _ => "read", text => { spoken.Append(text); }, _ => { }, (_, _, _) => { });
            var request = finalPass.Requests[7];
            Require(finalPass.Requests.Count == 8 && reads == 7 && request["tools"] is null
                && (string?)request["tool_choice"] == "none" && spoken.ToString().Contains("answers", StringComparison.Ordinal),
                "the eighth existing inference pass answers with prior tool results and no tools or ninth pass");
        }
        using (var ignoresToolChoice = new ModelStub((_, pass) => Calls(("read_player", "{}"))))
        {
            var badReads = 0;
            try
            {
                await new LlamaClient().CompleteAsync(ignoresToolChoice.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Look up the answer." }], "", "",
                    () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                    () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => { badReads++; return "Small sheet"; }, () => "Workspace",
                    _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { });
                Require(false, "a server ignoring tool_choice=none must not dispatch a later tool");
            }
            catch (InvalidOperationException ex)
            {
                Require(badReads == 7 && ex.Message.Contains("No additional tool was executed", StringComparison.Ordinal),
                    "a server's unexpected eighth-pass tool call is rejected before execution");
            }
        }

        var registered = 0;
        using (var manyReads = new ModelStub((_, pass) => pass == 0
            ? Calls(Enumerable.Repeat(("read_pages", "{\"book_id\":\"fixture\",\"from_page\":1,\"to_page\":1}"), 17).ToArray(), true)
            : Text("The GM can use the accepted reads.")))
        {
            await new LlamaClient().CompleteAsync(manyReads.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Read many large pages." }], "", "",
                () => "books", _ => "Fixture", _ => "catalogue", (_, _, _) => "Source: Fixture.pdf, PDF pages 1-1\n" + new string('R', 16_000),
                _ => "sections", (_, _) => "section", () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => "Player", () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { }, onSubstantiveRead: (_, _, _) => { registered++; return ""; });
            var results = ToolOutputs(manyReads.Requests[1]);
            Require(results.Count == 17 && results.Any(result => result.Contains("too much retrieved material", StringComparison.Ordinal))
                && results.All(result => System.Text.Json.JsonSerializer.Serialize(result).Length <= LibraryService.MaxReadResultCharacters)
                && registered == results.Count(result => result.StartsWith("Source: ", StringComparison.Ordinal)),
                "combined large read batches remain bounded and withheld reads never register a citation source");
        }
        using (var nearFull = new ModelStub((_, pass) => pass == 0
            ? Calls(("read_pages", "{\"book_id\":\"fixture\",\"from_page\":1,\"to_page\":1}"), false)
                + "data: {\"usage\":{\"prompt_tokens\":3500,\"completion_tokens\":20,\"total_tokens\":3520}}\n\ndata: [DONE]\n\n"
            : Text("Answer without the withheld page.")))
        {
            var nearFullRegistrations = 0;
            await new LlamaClient().CompleteAsync(nearFull.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Read a page." }], "", "",
                () => "books", _ => "Fixture", _ => "catalogue", (_, _, _) => "Source: Fixture.pdf, PDF pages 1-1\n" + new string('R', 5_000),
                _ => "sections", (_, _) => "section", () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => "Player", () => "Workspace",
                _ => "search", _ => "read", _ => { }, _ => { }, (_, _, _) => { },
                onSubstantiveRead: (_, _, _) => { nearFullRegistrations++; return ""; }, contextTokenLimit: 4_000);
            Require(ToolOutputs(nearFull.Requests[1]).Single().Contains("too much retrieved material", StringComparison.Ordinal)
                && nearFullRegistrations == 0, "near-full model context withholds a new read before citation registration");
        }

        var logPath = Path.Combine(temporary, "bounded-diagnostics.jsonl");
        File.WriteAllText(logPath, new string('x', 16 * 1024 * 1024));
        var diagnostics = Isolate(new GmRequestDiagnostics(), "_path", logPath);
        diagnostics.RecordStage("rotation fixture", TimeSpan.FromMilliseconds(1));
        Require(File.Exists(logPath + ".previous") && new FileInfo(logPath).Length < 1_000
            && new FileInfo(logPath + ".previous").Length == 16 * 1024 * 1024,
            "diagnostics rotate at their size bound while leaving campaign data outside the log path untouched");
    }
}
