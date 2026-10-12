using DBLC;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

internal static class LiveProbe
{
    public static async Task Run(string root, string temporary)
    {
        var summaries = new List<object>();
        var scenarios = new[] { "ordinary", "library", "presentation-effects", "character-speakers", "speaker-lifecycle", "speaker-lifecycle-late" };
        var onlyScenario = Environment.GetEnvironmentVariable("DBLC_LIVE_SCENARIO");
        foreach (var scenario in scenarios.Where(scenario => string.IsNullOrWhiteSpace(onlyScenario) || scenario.Equals(onlyScenario, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Live synthetic probe: " + scenario);
            var prompt = scenario switch
            {
                "ordinary" => "Synthetic performance probe: describe a quiet stone doorway in one short sentence. No source lookup is needed.",
                "library" => "Synthetic performance probe: consult the Fixture Rules book and tell me what new characters carry and how much HP one advancement adds. Use the relevant companion sections, then answer in at most two sentences.",
                "presentation-effects" => "Write a short ordinary GM response with six clearly readable short phrases. Place each optional hidden presentation marker exactly once, in this order, on its own line immediately before the phrase: [[focus]], [[shaky]], [[3d]], [[vaporwave]], [[smoke]], [[hyperspace]]. Do not mention the markers as prose. Keep the content harmless and easy to inspect.",
                "character-speakers" => "Give one brief response with exactly three clearly separate parts: narrator text, one short line spoken by Example NPC, then narrator text. Use [[speaker:GM]] before narrator parts and [[speaker:Example NPC]] before her line. Do not print HTML or mention the markers.",
                "speaker-lifecycle-late" => "Write 3 short GM narration sentences first. Near the end, emit the exact marker [[speaker: Example NPC]] and add 2 short consecutive sentences spoken by Example NPC. Use plain text; do not call present_response or any other tool. Do not print HTML or explain the test.",
                "speaker-lifecycle" => "Write one GM narration sentence. Then emit the exact marker [[speaker: Example NPC]] and write several short consecutive sentences spoken by Example NPC. Make it around 5 sentences total, with the speaker change after the first narration sentence, and do not add another speaker change. Use plain text; do not call present_response or any other tool. Do not print HTML or explain the test.",
                _ => throw new InvalidOperationException("Unknown live probe scenario.")
            };
            var log = Path.Combine(temporary, scenario + ".jsonl");
            var diagnostics = new GmRequestDiagnostics();
            typeof(GmRequestDiagnostics).GetField("_path", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(diagnostics, log);
            var reply = new ChatLine(); var steering = new ResponseSteeringSession(Guid.NewGuid(), reply, diagnostics);
            var streamedText = new StringBuilder();
            var effectsParser = new GmNarrationMarkerParser("focus", allowInlineEffects: true, allowCharacterSpeakers: true);
            var observedEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var observedSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GmPresentation? presentation = null;
            using var deadline = new CancellationTokenSource();
            var watchdog = Task.Run(async () => { try { await Task.Delay(TimeSpan.FromMinutes(4), deadline.Token); steering.RequestStop(); } catch (OperationCanceledException) { } });
            var timer = Stopwatch.StartNew(); var outcome = "completed";
            try
            {
                await new LlamaClient().CompleteAsync("http://127.0.0.1:8081/v1", Environment.GetEnvironmentVariable("DBLC_LIVE_MODEL") ?? "local-model", [new ChatLine { Role = "user", Content = prompt }], "", "",
                    () => "book_id: fixture | Fixture Rules | 3 PDF pages | valid DBLC companion available", _ => "Fixture Rules",
                    _ => "Fixture Rules: Character creation p1; Advancement p2; Weather p3.", (_, from, to) => $"Fixture Rules pages {from}-{to}: new characters carry a lantern and rope; one advancement adds 2 HP.",
                    _ => "Fixture Rules companion sections: creation | Character creation | pages 1-1; advancement | Advancement | pages 2-2; weather | Weather | pages 3-3.",
                    (_, ids) => string.Join("\n", ids.Select(id => id switch { "creation" => "Section creation, page 1: New characters carry a lantern and rope.", "advancement" => "Section advancement, page 2: One advancement adds 2 HP.", "weather" => "Section weather, page 3: Wind comes from the west.", _ => "Unknown section ID." })),
                    () => "No NPC cards", _ => "No such NPC", (_, _, _, _) => "Synthetic probe: no NPC persistence",
                    () => "Empty Player", () => "Empty Workspace", _ => "No archive matches", _ => "No archive lines",
                    token =>
                    {
                        streamedText.Append(token);
                        if (int.TryParse(Environment.GetEnvironmentVariable("DBLC_LIVE_DELAY_MS"), out var delay) && delay > 0) Thread.Sleep(Math.Min(delay, 100));
                        var batches = effectsParser.Append(token);
                        foreach (var batch in batches) { reply.Content += batch.Text; observedEffects.Add(batch.Effect); observedSpeakers.Add(batch.Speaker); }
                    }, _ => { }, (_, _, _) => { },
                    hasModelGmMark: true, onPresentation: p =>
                    {
                        presentation = p;
                        var finalText = string.Join("\n", p.Segments.Select(s => s.Text));
                        reply.Content = finalText;
                        foreach (var segment in p.Segments)
                        {
                            var parser = new GmNarrationMarkerParser("focus", allowInlineEffects: true, allowCharacterSpeakers: true, initialSpeaker: segment.Speaker);
                            foreach (var batch in parser.Append(segment.Text).Concat(parser.Flush())) { observedEffects.Add(batch.Effect); observedSpeakers.Add(batch.Speaker); }
                        }
                    }, steering: steering, diagnostics: diagnostics,
                    streamAnimationsEnabled: scenario is "presentation-effects" or "character-speakers" or "speaker-lifecycle" or "speaker-lifecycle-late", allowInlineStreamEffects: true,
                    allowCharacterSpeakers: scenario is "presentation-effects" or "character-speakers" or "speaker-lifecycle" or "speaker-lifecycle-late");
                if (scenario is "presentation-effects" or "speaker-lifecycle" or "speaker-lifecycle-late")
                {
                    foreach (var batch in effectsParser.Flush()) { observedEffects.Add(batch.Effect); observedSpeakers.Add(batch.Speaker); }
                }
                if (steering.StopRequested) outcome = "deadline_stopped";
            }
            catch (Exception ex) { outcome = ex.GetType().Name; }
            finally { deadline.Cancel(); await watchdog; steering.Close(); diagnostics.Complete(outcome); }
            var entries = File.ReadLines(log).Select(line => JsonNode.Parse(line)!["entry"]!.AsObject()).ToArray();
            var passes = entries.Where(e => (string?)e["event_name"] == "inference_finished").ToArray();
            var tools = entries.Where(e => (string?)e["event_name"] == "tool_finished").ToArray();
            var expectedEffects = GmNarrationMarkerParser.Effects;
            var summary = new { scenario, outcome, wall_ms = timer.Elapsed.TotalMilliseconds, inference_passes = passes.Length,
                request_wall_ms = passes.Sum(p => (double?)p["wall_ms"] ?? 0), prompt_tokens = passes.Sum(p => (int?)p["prompt_tokens"] ?? 0),
                generated_tokens = passes.Sum(p => (int?)p["generation_tokens"] ?? 0), tool_ms = tools.Sum(t => (double?)t["elapsed_ms"] ?? 0),
                tools = tools.Select(t => (string?)t["name"]).ToArray(), response_characters = reply.Content.Length, stream_characters = streamedText.Length,
                presentation_segments = presentation?.Segments.Count,
                observed_effects = scenario == "presentation-effects" ? expectedEffects.Where(observedEffects.Contains).ToArray() : [],
                all_effects_observed = scenario != "presentation-effects" || expectedEffects.All(observedEffects.Contains),
                observed_speakers = scenario is "character-speakers" or "speaker-lifecycle" or "speaker-lifecycle-late" ? observedSpeakers.ToArray() : [],
                all_requested_speakers_observed = scenario is not ("character-speakers" or "speaker-lifecycle" or "speaker-lifecycle-late") || observedSpeakers.Contains("GM") && observedSpeakers.Contains("Example NPC"),
                ordinary_narration_visible = scenario != "ordinary" || !string.IsNullOrWhiteSpace(reply.Content), stream_narration_characters = streamedText.Length,
                library_facts_present = scenario != "library" || reply.Content.Contains("lantern", StringComparison.OrdinalIgnoreCase) && reply.Content.Contains("rope", StringComparison.OrdinalIgnoreCase) && reply.Content.Contains('2') };
            summaries.Add(summary);
            Console.Error.WriteLine(JsonSerializer.Serialize(summary));
        }
        Console.WriteLine(JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
