using DBLC;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;

internal static partial class Checks
{
    private static int _checks;
    internal static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        _checks++;
    }

    private static byte[] TestMapPng()
    {
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 70; pixels[i + 2] = 180; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static byte[] TestMapJpeg()
    {
        using var input = new MemoryStream(TestMapPng());
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    internal static async Task Run(string root, string temporary)
    {
        MinistralMessageCompatibilityChecks.Run();
        PrivacyLockChecks(temporary);
        await PersistenceRepairChecks(temporary);
        await FinalPersistenceChecks(temporary);
        await AuditHardeningChecks(root, temporary);
        await SourceAttributionChecks(temporary);
        await RemainingAuditChecks(temporary);
        await CalendarPageChecks(root, temporary);
        await WorkbenchCompactionChecks(temporary);
        await ChronicleTimestampChecks(root, temporary);
        await SessionEndSummaryChecks(root, temporary);
        await CrossSessionChronicleChecks(temporary);
        await RetrievalStoppingChecks(temporary);
        await LargeRetrievalChecks(temporary);
        await EfficiencyRemainingChecks(temporary);
        var snapshotConversation = new Conversation { Lines = [new ChatLine { Content = "before", CompletedAt = DateTimeOffset.Now, PresentationSegments = [new GmPresentationSegment { Speaker = "Example NPC", Text = "before", NarrationEffectSpans = [new GmNarrationEffectSpan(0, 6, "smoke")] }] }] };
        var snapshot = MobileChronicleSnapshot.Capture(snapshotConversation);
        foreach (var frozenLine in snapshot.Lines)
        {
            snapshotConversation.Lines.Add(new ChatLine { Content = "new message" });
            snapshotConversation.Lines[0].Content = "after";
            snapshotConversation.Lines[0].PresentationSegments![0].Text = "after";
            snapshotConversation.Lines[0].PresentationSegments!.Add(new GmPresentationSegment { Text = "new speaker" });
            Require(frozenLine.Content == "before" && frozenLine.PresentationSegments![0].Text == "before", "mobile render snapshot retains text while the source Chronicle changes");
            Require(frozenLine.PresentationSegments!.Count == 1, "mobile render snapshot retains speaker boundaries while streaming adds segments");
            Require(frozenLine.Id == snapshotConversation.Lines[0].Id && frozenLine.CompletedAt == snapshotConversation.Lines[0].CompletedAt && frozenLine.PresentationSegments[0].Id == snapshotConversation.Lines[0].PresentationSegments![0].Id, "mobile snapshot retains stable line/speaker identities and the display completion timestamp");
        }
        var updatedSnapshot = MobileChronicleSnapshot.Capture(snapshotConversation);
        Require(snapshot.Lines.Count == 1 && updatedSnapshot.Lines.Count == 2 && updatedSnapshot.Lines[0].Content == "after", "next mobile snapshot includes the latest appended and updated content");
        Require(MobileChronicleSnapshot.Capture(null).Lines.Count == 0, "mobile render snapshot supports an absent Session");
        var tick = ((char)96).ToString();
        var mobileShell = File.ReadAllText(Path.Combine(root, "src", "DBLC", "MobileShell.razor"));        var desktopShell = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        Require(desktopShell.Contains("Chronicle Text Size", StringComparison.Ordinal) && desktopShell.IndexOf("Chronicle Text Size", StringComparison.Ordinal) < desktopShell.IndexOf("NAVIGATION ORDER", StringComparison.Ordinal), "Chronicle text size setting appears above Navigation Order");
        Require(desktopShell.Contains("style=\"--chronicle-text-scale: @State.ChronicleTextScaleCss\"", StringComparison.Ordinal) && mobileShell.Contains("style=\"--chronicle-text-scale: @State.ChronicleTextScaleCss\"", StringComparison.Ordinal), "desktop and mobile Chronicle share the persisted prose-size variable");
        var appCss = File.ReadAllText(Path.Combine(root, "src", "DBLC", "wwwroot", "css", "app.css"));
        Require(appCss.Contains(".messages .message-content{font-size:calc(13px * var(--chronicle-text-scale,1))", StringComparison.Ordinal) && appCss.Contains("calc(17px * var(--chronicle-text-scale,1))", StringComparison.Ordinal), "desktop prose and Markdown headings scale through one variable");
        Require(!appCss.Contains(".message-meta{font-size:calc", StringComparison.Ordinal) && !appCss.Contains(".speaker-avatar{width:calc", StringComparison.Ordinal), "Chronicle labels retain their existing size while portraits use the shared scale");
        Require(desktopShell.Contains("AnimationsEnabled=\"@State.GmStreamAnimationsEnabled\"", StringComparison.Ordinal) && mobileShell.Contains("AnimationsEnabled=\"@State.GmStreamAnimationsEnabled\"", StringComparison.Ordinal), "desktop and mobile use the shared Chronicle stream-effects renderer");
        Require(mobileShell.Contains("ActiveSpace == \"Table\" && State.PendingPlayerUpdate", StringComparison.Ordinal), "Player Sheet approval is rendered above the mobile Table");
        Require(mobileShell.Contains("ActiveSpace == \"Table\" && State.PendingGmNote", StringComparison.Ordinal) && mobileShell.Contains("ActiveSpace == \"Table\" && State.PendingBestiaryProposal", StringComparison.Ordinal), "GM Note and Bestiary approvals are rendered above the mobile Table");
        Require(mobileShell.Contains("MobilePlayerReviewUpdated", StringComparison.Ordinal) && mobileShell.Contains("role=\"tablist\"", StringComparison.Ordinal), "mobile artifact review uses a phone-sized version switcher");
        var mobileJs = File.ReadAllText(Path.Combine(root, "src", "DBLC", "wwwroot", "app.js"));
        Require(mobileJs.Contains("--mobile-viewport-top", StringComparison.Ordinal) && mobileJs.Contains("composerResized(document.querySelector('.mobile-messages'))", StringComparison.Ordinal), "mobile visual viewport geometry updates the shell and requests coalesced Chronicle follow");
        Require(mobileJs.Contains("state.lastDistanceFromBottom <= bottomTolerance", StringComparison.Ordinal) && mobileJs.Contains("state.followAfterViewportResize = true", StringComparison.Ordinal), "Chronicle reflow keeps following only when the device was already at the latest message");
        Require(mobileJs.Contains("const composerResized = el =>", StringComparison.Ordinal) && mobileJs.Contains("if (!state || state.manuallyScrolled) return;", StringComparison.Ordinal), "viewport-driven follow preserves manually scrolled Chronicle position");
        Require(mobileJs.Contains("performance.now() <= state.userScrollIntentUntil", StringComparison.Ordinal) && mobileJs.Contains("addEventListener('touchmove', state.onUserScrollIntent", StringComparison.Ordinal), "keyboard-induced scroll adjustments do not count as manual reading gestures");
        var mobileComposerStart = mobileJs.IndexOf("window.installMobileComposer =", StringComparison.Ordinal);
        var mobileComposerEnd = mobileComposerStart >= 0 ? mobileJs.IndexOf("\n};", mobileComposerStart, StringComparison.Ordinal) : -1;
        var mobileComposerJs = mobileComposerStart >= 0 && mobileComposerEnd > mobileComposerStart ? mobileJs[mobileComposerStart..mobileComposerEnd] : "";
        Require(mobileComposerJs.Contains("event.preventDefault()", StringComparison.Ordinal) && mobileComposerJs.Contains("textarea.setRangeText('\\n', start, end, 'end')", StringComparison.Ordinal) && !mobileComposerJs.Contains("requestSubmit", StringComparison.Ordinal), "mobile Enter inserts a newline without submitting the form");
        var mobileCss = File.ReadAllText(Path.Combine(root, "src", "DBLC", "wwwroot", "css", "mobile.css"));
        Require(mobileCss.Contains("top:var(--mobile-viewport-top,0px)", StringComparison.Ordinal), "mobile shell follows visual viewport vertical offset");
        var mobileDocument = File.ReadAllText(Path.Combine(root, "src", "DBLC", "MobileHostDocument.razor"));
        Require(mobileDocument.Contains("mobile-composer-fix-1", StringComparison.Ordinal) && mobileDocument.Contains("chronicle-text-size-1", StringComparison.Ordinal), "mobile JS and CSS cache keys refresh after viewport and composer changes");
        Require(Regex.Matches(mobileDocument, "<script src=\"/app\\.js").Count == 1, "mobile document loads app.js once so Chronicle handlers are not reinitialized");

        // llama.cpp chat templates commonly require every system instruction at the start of the request.
        var buildMessages = typeof(LlamaClient).GetMethod("BuildMessages", BindingFlags.NonPublic | BindingFlags.Static)!;
        var promptMessages = (JsonArray)buildMessages.Invoke(null, [
            new List<ChatLine> { new() { Role = "user", Content = "Continue the scene." }, new() { Role = "assistant", Content = "The door opens." } },
            "Archived context", "Previous session recap", "Surprise packet", "NPC names", "◇", false, true,
            AppDefaults.DefaultGmProfile, "Night", (IReadOnlyList<NpcPortrayalCue>)[new("Cletus", "Broad drawl.")], "OPEN THREAD REMINDER\nThe crypt remains unexplored.", false, true, "shaky", true, false, null, "CAMPAIGN TIME REMINDER\nConsider the elapsed time.", null
        ])!;
        Require(promptMessages.OfType<JsonObject>().Count(message => (string?)message["role"] == "system") == 1, "GM requests retain one leading system message");
        Require((string?)promptMessages[0]?["role"] == "system" && promptMessages.Skip(1).All(message => (string?)message?["role"] is "user" or "assistant"), "system message precedes all conversational messages");
        var systemPrompt = (string)promptMessages[0]!["content"]!;
        var latestContext = (string?)promptMessages[^1]?["content"] ?? "";
        Require(!systemPrompt.Contains("Broad drawl.", StringComparison.Ordinal) && !systemPrompt.Contains("CAMPAIGN TIME REMINDER", StringComparison.Ordinal)
            && systemPrompt.Contains("crypt remains unexplored", StringComparison.Ordinal) && systemPrompt.Contains("Archived context", StringComparison.Ordinal) && systemPrompt.Contains("Surprise packet", StringComparison.Ordinal)
            && latestContext.Contains("CURRENT FICTIONAL CAMPAIGN TIME:\nNight", StringComparison.Ordinal)
            && latestContext.Contains("Cletus:\nBroad drawl.", StringComparison.Ordinal)
            && latestContext.Contains("CAMPAIGN TIME REMINDER\nConsider the elapsed time.", StringComparison.Ordinal),
            "portrayal and time reminders use the latest DBLC context while other dynamic sections keep their existing placement");
        var withoutTransientContext = (JsonArray)buildMessages.Invoke(null, [
            new List<ChatLine> { new() { Role = "user", Content = "Continue the scene." }, new() { Role = "assistant", Content = "The door opens." } },
            "Archived context", "Previous session recap", "Surprise packet", "NPC names", "◇", false, true,
            AppDefaults.DefaultGmProfile, "Night", null, "OPEN THREAD REMINDER\nThe crypt remains unexplored.", false, true, "shaky", true, false, null, null, null
        ])!;
        Require((string?)withoutTransientContext[0]?["content"] == systemPrompt
            && !((string?)withoutTransientContext[^1]?["content"] ?? "").Contains("Broad drawl.", StringComparison.Ordinal),
            "changing portrayal and time reminders no longer invalidate the otherwise identical system prefix");
        const string npcKnowledgeRule = "NPC knowledge is limited: NPCs know only what they witnessed, were told, or could reasonably infer. They do not automatically know the player's identity, private actions or history, or events elsewhere. Preserve established knowledge, relationships, and campaign facts; do not assume familiarity without support.";
        Require(systemPrompt.Contains(npcKnowledgeRule, StringComparison.Ordinal) && systemPrompt.IndexOf(npcKnowledgeRule, StringComparison.Ordinal) == systemPrompt.LastIndexOf(npcKnowledgeRule, StringComparison.Ordinal), "protected base prompt includes the universal NPC knowledge boundary exactly once");
        const string recordConsistencyRule = "Use tools when necessary to maintain accurate campaign records, not just when answering questions. Keep narration and recorded state consistent.";
        Require(systemPrompt.Contains(recordConsistencyRule, StringComparison.Ordinal) && systemPrompt.Contains("Do not claim an artifact, edit, or lifecycle action succeeded until the tool confirms it", StringComparison.Ordinal), "protected base prompt preserves record consistency and tool approval safeguards");
        Require(!systemPrompt.Contains("For exact past facts, use search_chronicle", StringComparison.Ordinal) && !systemPrompt.Contains("For PDFs, call get_catalogue", StringComparison.Ordinal), "protected base prompt leaves detailed tool procedures to tool schemas and skills");
        Require(systemPrompt.Contains("[[hyperspace]]", StringComparison.Ordinal) && systemPrompt.Contains("Inline choices are enabled", StringComparison.Ordinal), "optional stream marker guide appears only when presentation effects are enabled");
        var noEffectPrompt = (JsonArray)buildMessages.Invoke(null, [new List<ChatLine>(), "", "", null, "NPC names", "◇", false, true, AppDefaults.DefaultGmProfile, "", null, null, false, false, "focus", true, false, null, null, null])!;
        Require(!((string)noEffectPrompt[0]!["content"]!).Contains("[[hyperspace]]", StringComparison.Ordinal), "stream marker instructions are absent when animations are disabled");
        var workbenchPrompt = (JsonArray)buildMessages.Invoke(null, [new List<ChatLine>(), "", "", null, "NPC names", "◇", false, true, AppDefaults.DefaultGmProfile, "", null, null, true, false, "focus", true, false, null, null, null])!;
        var workbenchInstructions = (string)workbenchPrompt[0]!["content"]!;
        Require(workbenchInstructions.Contains("WORKBENCH AWARENESS AND LIFECYCLE", StringComparison.Ordinal) && workbenchInstructions.Contains("primarily out-of-character", StringComparison.Ordinal), "Workbench prompt explains its temporary out-of-character purpose");
        Require(workbenchInstructions.Contains("Ordinary Workbench narration and dialogue are discarded", StringComparison.Ordinal) && workbenchInstructions.Contains("presents those exact proposals for the player to review", StringComparison.Ordinal), "Workbench prompt accurately describes transcript disposal and approved retention");
        Require(workbenchInstructions.Contains("sample scenes and prose are fine when requested", StringComparison.Ordinal) && workbenchInstructions.Contains("direct them to the main Session", StringComparison.Ordinal), "Workbench advice stays permissive and points adventure play back to the Session");
        Require(!systemPrompt.Contains("WORKBENCH AWARENESS AND LIFECYCLE", StringComparison.Ordinal), "Workbench-only awareness is absent from main Session prompts");
        var speakersPrompt = (JsonArray)buildMessages.Invoke(null, [new List<ChatLine>(), "", "", null, "NPC names", "◇", false, true, AppDefaults.DefaultGmProfile, "", null, null, false, false, "focus", true, true, null, null, null])!;
        Require(((string)speakersPrompt[0]!["content"]!).Contains("CHARACTER SPEAKERS", StringComparison.Ordinal) && ((string)speakersPrompt[0]!["content"]!).Contains("[[speaker:Exact NPC Name]]", StringComparison.Ordinal), "speaker marker guidance is added when enabled");
        Require(!((string)noEffectPrompt[0]!["content"]!).Contains("CHARACTER SPEAKERS:", StringComparison.Ordinal)
            && ((string)noEffectPrompt[0]!["content"]!).Contains(AppDefaults.DefaultGmProfile, StringComparison.Ordinal),
            "disabled Character Speakers omits its conditional guidance without rewriting the editable GM Profile");
        var buildTools = typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tools = (JsonArray)buildTools.Invoke(null, [false])!;
        var endWorkbenchTool = tools.OfType<JsonObject>().Single(tool => (string?)tool["function"]?["name"] == "request_end_workbench");
        var endWorkbenchParameters = (JsonObject)endWorkbenchTool["function"]!["parameters"]!;
        Require(endWorkbenchParameters["properties"]?["notes"] is JsonObject && endWorkbenchParameters["properties"]?["chronicle_facts"] is JsonObject, "Workbench close tool accepts both reviewed retention categories in the normal inference");
        var parseWorkbenchRetention = typeof(LlamaClient).GetMethod("ParseWorkbenchExitRetentionArguments", BindingFlags.NonPublic | BindingFlags.Static)!;
        var toolRetention = (WorkbenchExitRetention)parseWorkbenchRetention.Invoke(null, ["{\"notes\":[\"Table ruling\"],\"chronicle_facts\":[\"The chapel is sealed\"]}"])!;
        Require(toolRetention.Notes.SequenceEqual(["Table ruling"]) && toolRetention.ChronicleFacts.SequenceEqual(["The chapel is sealed"]), "Workbench tool retention arguments parse into the exact player-review proposal");
        try { parseWorkbenchRetention.Invoke(null, ["{\"notes\":[\"\"],\"chronicle_facts\":[]} "]); throw new Exception("Invalid retention should fail"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { Require(true, "invalid Workbench tool retention is rejected locally"); }
        var cleanPresentationArguments = typeof(LlamaClient).GetMethod("CleanPresentationToolArguments", BindingFlags.NonPublic | BindingFlags.Static)!;
        var presentationArguments = """{"gm_mark":"◇","segments":[{"speaker":"GM","text":"Before [[shaky]] after."}]}""";
        presentationArguments = presentationArguments.Replace("after.", "after. " + tick + "[[3d]]" + tick, StringComparison.Ordinal);
        var cleanedPresentationArguments = (string)cleanPresentationArguments.Invoke(null, [presentationArguments])!;
        var cleanedPresentationNode = JsonNode.Parse(cleanedPresentationArguments)!;
        var cleanedPresentationText = (string)cleanedPresentationNode["segments"]![0]!["text"]!;
        Require(!cleanedPresentationText.Contains("[[shaky]]", StringComparison.Ordinal) && cleanedPresentationText.Contains(tick + "[[3d]]" + tick, StringComparison.Ordinal), "presentation marker directives are omitted from later tool-history context while literal code stays intact");
        var findMisorderedSystem = typeof(LlamaClient).GetMethod("FindMisorderedSystemMessage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var validToolExchange = JsonNode.Parse("""[{"role":"system"},{"role":"user"},{"role":"assistant","tool_calls":[]},{"role":"tool","tool_call_id":"call_1"},{"role":"assistant"}]""")!;
        Require(findMisorderedSystem.Invoke(null, [validToolExchange]) is null, "system-order check permits supported assistant/tool exchange");
        var invalidOrder = JsonNode.Parse("""[{"role":"user"},{"role":"system"},{"role":"assistant"}]""")!;
        Require((int?)findMisorderedSystem.Invoke(null, [invalidOrder]) == 1, "system-order check identifies late system message");
        var invalidRequest = new JsonObject { ["model"] = "fixture", ["messages"] = invalidOrder.DeepClone() };
        var untouchedPayload = invalidRequest.ToJsonString();
        var warningPath = Path.Combine(temporary, "chat-order-warning.jsonl");
        var orderDiagnostics = Isolate(new GmRequestDiagnostics(), "_path", warningPath);
        var createValidatedContent = typeof(LlamaClient).GetMethod("CreateValidatedChatContent", BindingFlags.NonPublic | BindingFlags.Static)!;
        using (var checkedContent = (JsonContent)createValidatedContent.Invoke(null, [invalidRequest, "fixture_auxiliary", orderDiagnostics])!)
            Require(invalidRequest.ToJsonString() == untouchedPayload, "message-order diagnostic does not rewrite the auxiliary request");
        Require(File.ReadAllText(warningPath).Contains("chat_message_order_warning", StringComparison.Ordinal), "auxiliary ordering issue is recorded diagnostically");

        var fence = new string((char)96, 3);
        var markerParser = new GmNarrationMarkerParser("focus", allowInlineEffects: true);
        var narrationBatches = new List<GmNarrationTextBatch>();
        foreach (var chunk in new[] { "Before [[sha", "ky]] phrase. [[3", "d]]Deep[[vaporwave]] drift.", " [[smoke]]Mist", "[[hyperspace]]Rush[[focus]]End. " + tick + "[[shaky]]" + tick + ".\n\n" + fence + "text\n[[vaporwave]]\n" + fence + "\nUnknown [[glitch]] and malformed [[sha" })
            narrationBatches.AddRange(markerParser.Append(chunk));
        narrationBatches.AddRange(markerParser.Flush());
        var cleanNarration = string.Concat(narrationBatches.Select(batch => batch.Text));
        Require(cleanNarration == "Before  phrase. Deep drift. MistRushEnd. " + tick + "[[shaky]]" + tick + ".\n\n" + fence + "text\n[[vaporwave]]\n" + fence + "\nUnknown [[glitch]] and malformed [[sha", "split markers are removed while code, unknown markers and malformed text remain literal");
        Require(narrationBatches.Any(batch => batch.Effect == "shaky" && batch.Text.Contains("phrase", StringComparison.Ordinal))
            && narrationBatches.Any(batch => batch.Effect == "3d" && batch.Text == "Deep")
            && narrationBatches.Any(batch => batch.Effect == "vaporwave" && batch.Text.Contains("drift", StringComparison.Ordinal))
            && narrationBatches.Any(batch => batch.Effect == "smoke" && batch.Text == "Mist")
            && narrationBatches.Any(batch => batch.Effect == "hyperspace" && batch.Text == "Rush")
            && narrationBatches.Any(batch => batch.Effect == "focus" && batch.Text.Contains("End.", StringComparison.Ordinal)),
            "several inline effects switch deterministically within one response");
        Require(narrationBatches.Select(batch => batch.Start).SequenceEqual(narrationBatches.Select(batch => batch.Start).Order()), "effect batches retain source order");
        var noInlineParser = new GmNarrationMarkerParser("smoke", allowInlineEffects: false);
        var noInlineBatches = noInlineParser.Append("A[[shaky]]B").Concat(noInlineParser.Flush()).ToList();
        Require(string.Concat(noInlineBatches.Select(batch => batch.Text)) == "AB" && noInlineBatches.All(batch => batch.Effect == "smoke"), "disabled inline choices stay hidden and use the selected default");
        Require(GmNarrationMarkerParser.RemoveMarkers("a[[focus]]b " + tick + "[[3d]]" + tick) == "ab " + tick + "[[3d]]" + tick, "history marker cleanup preserves literal inline-code examples");

        var speakerParser = new GmNarrationMarkerParser("focus", allowInlineEffects: true, allowCharacterSpeakers: true);
        var speakerBatches = new List<GmNarrationTextBatch>();
        foreach (var chunk in new[] { "The door opens.\n[[spe", "aker:Example NPC]]\n[[smo", "ke]]Ah, you look at me.\n[[speaker:GM]]She turns. [[hyperspace]]Space.\n[[speaker:Other NPC]]Fine.\n" + tick + "[[speaker:GM]]" + tick + " literal " + tick + "[[speaker:GM]]" + tick + " and\n```text\n[[speaker:Example NPC]]\n[[smoke]]\n```" })
            speakerBatches.AddRange(speakerParser.Append(chunk));
        speakerBatches.AddRange(speakerParser.Flush());
        Require(string.Concat(speakerBatches.Select(batch => batch.Text)).Contains("Ah, you look at me.", StringComparison.Ordinal) && speakerBatches.Any(batch => batch.Speaker == "Example NPC" && batch.Effect == "smoke"), "split speaker and effect markers switch the same streaming batches");
        Require(speakerBatches.Any(batch => batch.Speaker == "GM" && batch.Effect == "focus" && batch.Text.Contains("She turns.", StringComparison.Ordinal)), "speaker change resets effect to default");
        Require(speakerBatches.Any(batch => batch.Speaker == "Other NPC" && batch.Text.Contains("Fine.", StringComparison.Ordinal)), "unknown but valid character names remain unlinked speaker labels");
        Require(string.Concat(speakerBatches.Select(batch => batch.Text)).Contains("literal " + tick + "[[speaker:GM]]" + tick, StringComparison.Ordinal)
            && string.Concat(speakerBatches.Select(batch => batch.Text)).Contains("```text\n[[speaker:Example NPC]]\n[[smoke]]", StringComparison.Ordinal), "speaker/effect examples in inline and fenced code remain literal");
        var disabledSpeakerParser = new GmNarrationMarkerParser("focus", allowInlineEffects: true, allowCharacterSpeakers: false);
        var disabledSpeakerBatches = disabledSpeakerParser.Append("Before [[speaker:Example NPC]]After [[speaker:GM]]Done").Concat(disabledSpeakerParser.Flush()).ToList();
        Require(string.Concat(disabledSpeakerBatches.Select(batch => batch.Text)) == "Before After Done" && disabledSpeakerBatches.All(batch => batch.Speaker == "GM"), "disabled speakers strip markers but keep all prose under GM");
        Require(GmNarrationMarkerParser.NormalizeSpeaker("THE GM") == "GM" && GmNarrationMarkerParser.NormalizeSpeaker("Game Master") == "GM", "GM aliases cannot create a duplicate Chronicle identity");
        var npc = new NpcCard { Id = Guid.NewGuid(), Name = "Example NPC" };
        Require(NpcSpeakerResolver.Resolve([npc], "Example NPC")?.Id == npc.Id, "exact case-insensitive NPC lookup uses stable identity");
        Require(NpcSpeakerResolver.Resolve([npc], "Example NPC II") is null, "unknown speaker does not create an NPC card");
        Require(NpcSpeakerResolver.Resolve([npc, new NpcCard { Name = "Example NPC" }], "Example NPC") is null, "ambiguous NPC name stays safely unlinked");
        GmPresentation? multiSpeakerPresentation = null;
        var structuredSpeakerText = JsonSerializer.Serialize(new { segments = new[] { new { speaker = "GM", text = "She leans closer." }, new { speaker = "Example NPC", text = "[[smoke]]Ah, your eyes." }, new { speaker = "THE GM", text = "She smiles." } } });
        using (var server = new ModelStub((_, _) => Calls(("present_response", structuredSpeakerText))))
        {
            await RunModel(server, new(), presentation: presentation => multiSpeakerPresentation = presentation, allowCharacterSpeakers: true);
            Require(server.Requests.Count == 1 && multiSpeakerPresentation!.Segments.Select(segment => segment.Speaker).SequenceEqual(["GM", "Example NPC", "THE GM"]), "present_response keeps a multi-speaker reply in one model turn");
            var toolDescription = (string)server.Requests[0]["tools"]!.AsArray().Single(tool => (string?)tool!["function"]!["name"] == "present_response")!["function"]!["description"]!;
            Require(toolDescription.Contains("meaningful character speech", StringComparison.Ordinal), "enabled setting describes character speakers in its presentation tool");
        }
        using (var server = new ModelStub((_, _) => Calls(("present_response", structuredSpeakerText))))
        {
            await RunModel(server, new(), presentation: _ => { });
            var toolDescription = (string)server.Requests[0]["tools"]!.AsArray().Single(tool => (string?)tool!["function"]!["name"] == "present_response")!["function"]!["description"]!;
            Require(toolDescription.Contains("Character Speakers are disabled", StringComparison.Ordinal), "disabled setting constrains structured speakers to GM");
        }

        var pendingProbe = new ChronicleProbe();
        pendingProbe.Update("Saved narration survives initialization.");
        pendingProbe.Update("Saved narration survives initialization.");
        Require(pendingProbe.NeedsRender, "repeated startup parameters preserve pending Chronicle browser initialization");
        await pendingProbe.CompleteRender();
        Require(pendingProbe.Html.Contains("Saved narration survives initialization.", StringComparison.Ordinal), "startup initialization retains complete historical narration");
        pendingProbe.Update("Saved narration survives initialization.");
        Require(!pendingProbe.NeedsRender, "unchanged historical narration stops rendering once initialized");
        var probe = new ChronicleProbe();
        var md = "**bold *nested***\n\n- first\n- second\n\n```text\n<unsafe>\n```\n\n<script>alert(1)</script>\n\n[x](javascript:alert(1))";
        probe.Update(md);
        await probe.CompleteRender();
        var html = probe.Html;
        Require(html.Contains("<strong>") && html.Contains("first") && html.Contains("<pre>"), "sanitized Markdown stays visible when browser rendering is unavailable");
        Require(html.Contains("<strong>") && html.Contains("<em>") && html.Contains("<ul>") && html.Contains("<pre>"), "Markdown layout retained");
        Require(!html.Contains("chronicle-focus", StringComparison.Ordinal), "focus effect stays outside persisted and rendered Markdown HTML");
        Require(!html.Contains("chronicle-stream-effect", StringComparison.Ordinal), "arrival effects remain outside completed Markdown HTML");
        Require(!html.Contains("<script") && !html.Contains("href=\"javascript:"), "Markdown safety");
        const string malformedSpeakerWrapper = "<div class=\"gm-response\"><div class=\"speaker-segment\" data-speaker=\"Mara\">The lantern rises.</div>";
        Require(GmResponseMarkupParser.TryUnwrap(malformedSpeakerWrapper, out var unwrappedWrapper)
            && unwrappedWrapper.Segments.Single().Speaker == "Mara"
            && unwrappedWrapper.Segments.Single().Text == "The lantern rises.", "malformed open response wrapper recovers the speaker segment");
        Require(GmResponseMarkupParser.ForDisplay(malformedSpeakerWrapper) == "The lantern rises.", "malformed wrapper is removed from live and historical display text");
        Require(GmResponseMarkupParser.ForDisplay("<div class=\"other\">literal</div>") == "<div class=\"other\">literal</div>", "unknown HTML remains untouched by wrapper handling");
        var normalizedPresentation = GmResponseMarkupParser.Normalize(new GmPresentation { Segments =
        [
            new GmPresentationSegment { Speaker = "GM", Text = "<div class=\"gm-response\"><div class=\"speaker-segment\" data-speaker=\"THE GM\">" },
            new GmPresentationSegment { Speaker = "GM", Text = "A visible paragraph survives." }
        ] });
        Require(normalizedPresentation.Segments.Count == 1 && normalizedPresentation.Segments[0].Text == "A visible paragraph survives.", "wrapper normalization drops empty segments without removing narration");
        var chronicleTextSource = File.ReadAllText(Path.Combine(root, "src", "DBLC", "ChronicleText.razor"));
        Require(chronicleTextSource.Contains("_useServerFallback", StringComparison.Ordinal) && chronicleTextSource.Contains("ChronicleMarkdownRenderer.Render(displayText)", StringComparison.Ordinal), "Chronicle retains sanitized narration if browser stream rendering fails");
        Require(systemPrompt.Contains("Never output HTML/XML tags or wrapper markup", StringComparison.Ordinal), "GM request retains the HTML-wrapper prompt clarification");
        probe.Update(md);
        Require(!probe.NeedsRender && ReferenceEquals(html, probe.Html), "unchanged Markdown reused");
        probe.Update(md, "GLITCH_CURSED", 42);
        Require(probe.NeedsRender && probe.Html.Contains("data-clean-glyph"), "flavor invalidates rendered HTML");
        await probe.CompleteRender();
        var glitch = probe.Html; probe.Update(md, "GLITCH_CURSED", 42);
        Require(!probe.NeedsRender && glitch == probe.Html, "stable glitch reused");
        probe.Update(md, "GLITCH_CURSED", 43); Require(probe.Html != glitch, "seed invalidates rendering"); await probe.CompleteRender();
        probe.Update(md, "GLITCH_CURSED", 43, false); Require(probe.Html == html, "disabled flavors restore plain Markdown");

        const string escapedMarkdown = "## Calendar of Nechrubel\\n\\n**First age**\\n\\n- The black moon rises\\n- Fate turns\\n\\nInline code stays `\\n` and JSON stays {\"lineBreak\":\"\\n\"}.\\n\\n```json\\n{\"literal\":\"\\n\"}\\n```";
        var streamedEscapes = new StringBuilder();
        using (var server = new ModelStub((_, _) => Text(escapedMarkdown)))
            await RunModel(server, new(), onToken: token => streamedEscapes.Append(token));
        Require(streamedEscapes.ToString() == escapedMarkdown, "streamed deltas preserve model-emitted escape text for presentation rendering");
        GmPresentation? escapedPresentation = null;
        var encodedPresentation = JsonSerializer.Serialize(new { gm_mark = "◇", segments = new[] { new { speaker = "GM", text = escapedMarkdown } } });
        using (var server = new ModelStub((_, _) => Calls(("present_response", encodedPresentation))))
            await RunModel(server, new(), presentation: presentation => escapedPresentation = presentation);
        Require(escapedPresentation!.Segments.Single().Text == escapedMarkdown && escapedMarkdown.Contains("\\n", StringComparison.Ordinal), "model presentation escapes pass through assembly unchanged");
        var reloadStore = new ConversationStore(Path.Combine(temporary, "chronicle-render-reload"));
        var escapedLine = new ChatLine { Role = "assistant", Sequence = 1, Content = escapedPresentation.Segments.Single().Text, PresentationSegments = escapedPresentation.Segments };
        var effectLine = new ChatLine { Role = "assistant", Sequence = 2, Content = cleanNarration, NarrationEffectSpans = markerParser.Spans.ToList() };
        reloadStore.Save(new SavedState("local", "fixture", "", null, [new Conversation { Lines = [escapedLine, effectLine] }]));
        var reloadedLine = reloadStore.Load().Conversations.Single().Lines.Single(line => line.Sequence == 1);
        var reloadedEffectLine = reloadStore.Load().Conversations.Single().Lines.Single(line => line.Sequence == 2);
        Require(reloadedEffectLine.Content == cleanNarration && reloadedEffectLine.NarrationEffectSpans?.SequenceEqual(markerParser.Spans) == true, "clean narrative and effect metadata survive session reload");
        var escapedHtml = ChronicleMarkdownRenderer.Render(reloadedLine.Content);
        Require(reloadedLine.Content == escapedMarkdown && escapedHtml.Contains("<h2>") && escapedHtml.Contains("<ul>") && escapedHtml.Contains("<strong>"), "escaped Markdown renders correctly after session reload");
        Require(escapedHtml.Contains("<code>\\n</code>"), "inline code preserves literal backslash-n");
        Require(escapedHtml.Contains("\"lineBreak\":\"\\n\""), "inline JSON preserves literal backslash-n");
        Require(escapedHtml.Contains("\"literal\":\"\\n\""), "fenced JSON preserves literal backslash-n");
        var identityCampaign = new Campaign { PlayerIdentity = new PlayerChronicleIdentity { DisplayName = "Baba", PortraitMarkdownPath = "assets/portrait.png" } };
        var speakerNpcId = Guid.NewGuid();
        var speakerLine = new ChatLine { Role = "assistant", Sequence = 3, Content = "Example NPC: Hello.", PresentationSegments = [new GmPresentationSegment { Speaker = "Example NPC", SpeakerNpcId = speakerNpcId, Text = "Hello.", NarrationEffectSpans = [new(0, 6, "smoke")] }] };
        reloadStore.Save(new SavedState("local", "fixture", "", null, [new Conversation { CampaignId = identityCampaign.Id, Lines = [speakerLine] }], [identityCampaign], identityCampaign.Id, CharacterSpeakersEnabled: true));
        var identityReload = reloadStore.Load();
        Require(identityReload.Campaigns!.Single().PlayerIdentity.DisplayName == "Baba" && identityReload.Campaigns!.Single().PlayerIdentity.PortraitMarkdownPath == "assets/portrait.png", "structured Player Chronicle identity survives campaign reload independently of sheet Markdown");
        var persistedSpeaker = identityReload.Conversations.Single().Lines.Single().PresentationSegments!.Single();
        Require(persistedSpeaker.Speaker == "Example NPC" && persistedSpeaker.SpeakerNpcId == speakerNpcId && persistedSpeaker.NarrationEffectSpans!.Single().Effect == "smoke", "speaker ID and presentation-effect boundaries survive Chronicle reload");
        Require(identityReload.CharacterSpeakersEnabled, "Character Speakers setting persists");
        var oldCampaign = JsonSerializer.Deserialize<Campaign>("{}");
        var oldLine = JsonSerializer.Deserialize<ChatLine>("{}");
        Require(oldCampaign?.PlayerIdentity is not null && oldLine?.PresentationSegments is null, "campaigns and Chronicle lines without identities remain backward compatible");
        var playerViewSource = File.ReadAllText(Path.Combine(root, "src", "DBLC", "PaperView.razor"));
        var editorSource = File.ReadAllText(Path.Combine(root, "src", "DBLC", "PlayerMarkdownEditor.razor"));
        Require(playerViewSource.Contains("CHRONICLE NAME", StringComparison.Ordinal) && editorSource.Contains("ChroniclePortraitSelected", StringComparison.Ordinal) && File.ReadAllText(Path.Combine(root, "src", "DBLC", "wwwroot", "app.js")).Contains("Set as Chronicle Portrait", StringComparison.Ordinal), "Player controls name and selects a portrait from any embedded image block");
        Require(desktopShell.Contains("speaker-meta", StringComparison.Ordinal) && mobileShell.Contains("speaker-meta", StringComparison.Ordinal) && desktopShell.Contains("@key=\"segment.Id\"", StringComparison.Ordinal) && mobileShell.Contains("@key=\"segment.Id\"", StringComparison.Ordinal) && desktopShell.Contains("ResolveSpeakerNpc", StringComparison.Ordinal) && mobileShell.Contains("ResolveSpeakerNpc", StringComparison.Ordinal), "desktop and remote Chronicle render linked NPC identities through the same campaign data");
        Require(escapedHtml.Contains("<pre>"), "escaped fenced code renders as a fenced code block");
        var storageRoot = Path.Combine(temporary, "state-tests");
        var store = new ConversationStore(storageRoot);
        var campaign = new Campaign { Name = "Synthetic", Npcs = [new NpcCard { Name = "Cletus", Notes = "HISTORY_PRIVATE", PortrayalCue = "Rural voice" }],
            Bestiary = [new BestiaryEntry { Name = "Goblin", StatBlock = "HP 3", Notes = "Field note", Slain = 2, PortraitExtension = ".png" }],
            SavedMaps = [JsonNode.Parse("""{"Id":"legacy-sketch","Name":"Old sketch","Sketch":{"Name":"Old","Features":{"G7":"table"}}}""")!.AsObject()], GmNotes = [new GmNote { Text = "GM_PRIVATE" }] };
        var state = new SavedState("local", "fixture", "", null, [new Conversation { CampaignId = campaign.Id, Lines = [new ChatLine { Content = md }] }], [campaign]);
        store.Save(state); var loaded = store.Load();
        Require(JsonSerializer.Serialize(loaded) == JsonSerializer.Serialize(state), "all campaign data round trips");
        store.Save(state with { Model = "different" });
        var file = Path.Combine(storageRoot, "DBLC/state.json"); File.WriteAllText(file, "{broken");
        Require(store.Load().Model == "fixture", "recover previous state");
        store.Save(state); Require(File.ReadAllText(file + ".unreadable") == "{broken", "keep corrupt original on recovery");
        Require(store.Load().Campaigns![0].SavedMaps[0]["Sketch"]?["Features"]?["G7"]?.GetValue<string>() == "table", "legacy saved sketch record survives save and reload unchanged");
        File.WriteAllText(file, "broken"); File.WriteAllText(file + ".previous", "also broken");
        Reject(() => store.Load()); Require(File.ReadAllText(file) == "broken", "unrecoverable data fails closed");

        var archive = new ChronicleArchiveStore(Path.Combine(temporary, "archive.db"));
        var session = Guid.NewGuid();
        var archived = new ChatLine { Role = "assistant", Sequence = 1, Content = "Synthetic lantern reference", GmMark = "◇", PresentationSegments = [new GmPresentationSegment { Text = "Synthetic lantern reference", Flavor = "GLITCH_CURSED", GlitchSeed = 123 }] };
        archive.Archive(session, [archived]); archive.Archive(session, [archived]);
        Require(archive.GetArchivedLines(session).Count == 1 && archive.Search(session, "lantern").Contains("lantern"), "archive idempotent/searchable");
        Require(archive.GetArchivedLines(session)[0].PresentationSegments![0].GlitchSeed == 123, "archive expression metadata");
        Require(NpcPortrayalMatcher.Match(campaign.Npcs, "CLETUS's shack", "Cletus").Cues.Count == 1, "cue deduplication");
        Require(NpcPortrayalMatcher.Match([new NpcCard { Name = "Ed", PortrayalCue = "Quiet" }], "red bed", "").Cues.Count == 0, "cue boundary");
        var activeSpeakerNpc = new NpcCard { Id = Guid.NewGuid(), Name = "Sarah Whittal", PortrayalCue = "Bold, eager voice." };
        var previousNpcSpeech = new ChatLine
        {
            Role = "assistant", Sequence = 10, Content = "She watches from the pillar.",
            PresentationSegments =
            [
                new GmPresentationSegment { Speaker = "GM", Text = "She watches from the pillar." },
                new GmPresentationSegment { Speaker = activeSpeakerNpc.Name, SpeakerNpcId = activeSpeakerNpc.Id, Text = "What brings you here?" }
            ]
        };
        var speakerCueMatch = NpcPortrayalMatcher.MatchForLatestPlayer([activeSpeakerNpc], [previousNpcSpeech],
            new ChatLine { Role = "user", Sequence = 11, Content = "I answer her question." });
        Require(speakerCueMatch.Cues.Single().Name == activeSpeakerNpc.Name && speakerCueMatch.Cues.Single().Text == activeSpeakerNpc.PortrayalCue,
            "stable NPC speaker identity keeps the active character's portrayal cue available when the player uses a pronoun");
        var laterGmOnlyResponse = new ChatLine { Role = "assistant", Sequence = 12, Content = "The room falls quiet." };
        Require(NpcPortrayalMatcher.MatchForLatestPlayer([activeSpeakerNpc], [previousNpcSpeech, laterGmOnlyResponse],
            new ChatLine { Role = "user", Sequence = 13, Content = "I wait." }).Cues.Count == 0,
            "speaker identity from an older GM response does not keep an NPC cue active indefinitely");
        await ArtifactChecks(temporary, campaign);
        await MobileHostChecks(root, temporary);
        LibraryChecks(temporary);
        await ModelChecks();
        await PresentationStateChecks(temporary);
        await DiceAndContinuationChecks(temporary);
        await ChronicleOrderAndArrowChecks(temporary);
        Console.Error.WriteLine($"PASS: {_checks} assertions (synthetic data, isolated temporary storage).");
    }

    private static async Task WorkbenchCompactionChecks(string temporary)
    {
        using (var delayedSummaryServer = new ModelStub((_, _) =>
        {
            Thread.Sleep(500);
            return JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "Late summary" } } } });
        }))
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(60)))
        {
            var wasCancelled = false;
            try
            {
                await new LlamaClient().SummarizeChronicleAsync(delayedSummaryServer.Endpoint, "fixture", "", [new ChatLine { Role = "user", Sequence = 1, Content = "Original discussion" }], cancellationToken: stop.Token);
            }
            catch (OperationCanceledException) { wasCancelled = true; }
            Require(wasCancelled, "a stopped turn cancels its outstanding automatic summary request");
        }
        using (var countServer = new ModelStub((_, _) => "{\"input_tokens\":73}"))
        {
            var count = await new LlamaClient().CountInputTokensAsync(countServer.Endpoint, "fixture", [], "", campaignTime: "Examplemonth 4",
                allowCharacterSpeakers: true, isWorkbench: true, retainedWorkbenchArtifacts: ["Player Sheet updated"], streamAnimationsEnabled: true);
            var payload = countServer.Requests.Single().ToJsonString();
            Require(count == 73 && payload.Contains("CHARACTER SPEAKERS", StringComparison.Ordinal)
                && payload.Contains("GM STREAM PRESENTATION", StringComparison.Ordinal)
                && payload.Contains("WORKBENCH AWARENESS", StringComparison.Ordinal)
                && payload.Contains("Player Sheet updated", StringComparison.Ordinal)
                && payload.Contains("Examplemonth 4", StringComparison.Ordinal),
                "context count request includes active speaker, stream, Workbench, retained-artifact and campaign-time instructions");
        }
        var root = Path.Combine(temporary, "workbench-local-compaction");
        Directory.CreateDirectory(root);
        var campaign = new Campaign { Name = "Workbench compaction fixture" };
        var parent = new Conversation { CampaignId = campaign.Id, SessionNumber = 0, Title = "Parent session", ContextTokenLimit = 100 };
        var workbench = new Conversation
        {
            CampaignId = campaign.Id, ParentSessionId = parent.Id, IsWorkbench = true, Title = "Temporary planning",
            ContextTokenLimit = 100, WorkbenchContextSnapshot = "Parent Session background remains unchanged.",
            WorkbenchRetainedArtifacts = ["Player Sheet updated"]
        };
        for (var index = 1; index <= 12; index++)
            workbench.Lines.Add(new ChatLine { Sequence = index, Role = index % 2 == 1 ? "user" : "assistant", Content = $"Original Workbench detail {index}." });
        workbench.NextSequence = 13;
        var store = new ConversationStore(Path.Combine(root, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [workbench, parent], [campaign], campaign.Id));
        var archivePath = Path.Combine(root, "archive.db");
        var archive = new ChronicleArchiveStore(archivePath);
        var state = new AppState(store, new LibraryService(), archive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(root, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(root, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(root, "campaigns")));
        var activeWorkbench = state.Conversations.Single(item => item.Id == workbench.Id);
        state.Select(activeWorkbench);
        var countRequest = 0;
        var summaryRequest = 0;
        using (var server = new ModelStub((request, _) => request["stream"]?.ToString() switch
        {
            "true" => Text("The Workbench conversation continues."),
            "false" => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = $"Temporary Workbench summary {++summaryRequest}. Preserve the earlier agreed ruling and unresolved item." } } } }),
            _ => JsonSerializer.Serialize(new { input_tokens = ++countRequest % 2 == 1 ? 85 : 42 })
        }))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("What did we decide?", new LlamaClient(), contextLimit: 100);
            Require(activeWorkbench.WorkbenchContextSummary.Contains("summary 1", StringComparison.Ordinal), "Workbench compaction begins at the shared approximately 80 percent threshold and stores a temporary summary");
            Require(activeWorkbench.WorkbenchCompactedThroughSequence == 6 && activeWorkbench.Lines.Count == 14
                && activeWorkbench.Lines.Any(line => line.Content == "Original Workbench detail 1."), "compaction keeps the complete disposable transcript while marking its summarized prefix");
            var compactedHistory = state.GetContextHistory(activeWorkbench);
            Require(compactedHistory.Any(line => line.Content.Contains("Parent Session background remains unchanged", StringComparison.Ordinal))
                && compactedHistory.Any(line => line.Content.Contains("summary 1", StringComparison.Ordinal)), "Workbench model context preserves the parent snapshot and adds the temporary summary");
            Require(compactedHistory.All(line => !line.Content.Contains("Original Workbench detail 1.", StringComparison.Ordinal))
                && compactedHistory.Any(line => line.Content.Contains("Original Workbench detail 12.", StringComparison.Ordinal))
                && compactedHistory.Any(line => line.Content.Contains("The Workbench conversation continues.", StringComparison.Ordinal)), "older details leave active context while recent discussion and the new turn remain verbatim");
            Require(activeWorkbench.LastContextTokens == 42, "Workbench context meter is refreshed to the compacted request size");
            Require(activeWorkbench.WorkbenchRetainedArtifacts.SequenceEqual(["Player Sheet updated"]), "compaction preserves existing retained-artifact tracking");

            await state.SendAsync("And what remains unresolved?", new LlamaClient(), contextLimit: 100);
            Require(activeWorkbench.WorkbenchCompactedThroughSequence > 6 && summaryRequest == 2, "long Workbenches can compact again and continue using the same mechanism");
            var secondSummaryRequest = server.Requests.Where(request => request["stream"]?.ToString() == "false").Last();
            Require(secondSummaryRequest.ToJsonString().Contains("Temporary Workbench summary 1", StringComparison.Ordinal)
                && secondSummaryRequest.ToJsonString().Contains("Original Workbench detail 7.", StringComparison.Ordinal), "successive temporary summaries incorporate the previous summary and newly compacted verbatim lines");
            var gmRequests = server.Requests.Where(request => request["stream"]?.ToString() == "true").ToList();
            Require(gmRequests.Count == 2 && gmRequests[1].ToJsonString().Contains("Temporary Workbench summary of older discussion", StringComparison.Ordinal)
                && gmRequests[1].ToJsonString().Contains("Player Sheet updated", StringComparison.Ordinal), "subsequent GM requests use compacted context and retained-artifact labels");
        }

        var reloaded = new AppState(store, new LibraryService(), new ChronicleArchiveStore(archivePath),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(root, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(root, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(root, "campaigns")));
        var restoredWorkbench = reloaded.Conversations.Single(item => item.Id == workbench.Id);
        Require(restoredWorkbench.WorkbenchContextSummary.Contains("summary 2", StringComparison.Ordinal)
            && restoredWorkbench.WorkbenchCompactedThroughSequence == activeWorkbench.WorkbenchCompactedThroughSequence
            && reloaded.GetContextHistory(restoredWorkbench).Any(line => line.Content.Contains("summary 2", StringComparison.Ordinal)), "temporary summary and compaction boundary survive state save and reload");
        Require(archive.GetArchivedLines(workbench.Id).Count == 0 && archive.GetArchivedLines(parent.Id).Count == 0
            && string.IsNullOrWhiteSpace(restoredWorkbench.ArchiveSummary), "Workbench compaction never enters the permanent Session archive or parent Session summary");

        reloaded.Select(restoredWorkbench);
        restoredWorkbench.ContextTokenLimit = 0;
        using (var searchServer = new ModelStub((_, pass) => pass switch
        {
            0 => Calls(("search_chronicle", "{\"query\":\"Original Workbench detail 1\",\"scope\":\"workbench\"}")),
            1 => Calls(("read_chronicle", $"{{\"scope\":\"workbench\",\"line_ids\":[1]}}")),
            _ => Text("The original detail remains available.")
        }))
        {
            reloaded.Endpoint = searchServer.Endpoint;
            await reloaded.SendAsync("Please revisit our earliest detail.", new LlamaClient());
            var searchResult = searchServer.Requests[1]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
            var readResult = searchServer.Requests[2]["messages"]!.AsArray().Last(item => (string?)item?["role"] == "tool")!["content"]!.ToString();
            Require(searchResult.Contains($"chronicle_id: {restoredWorkbench.Id:N}:1", StringComparison.Ordinal)
                && readResult.Contains("Original Workbench detail 1.", StringComparison.Ordinal),
                "explicit Workbench scope retrieves older saved discussion after compaction and reload");
            Require(archive.GetArchivedLines(restoredWorkbench.Id).Count == 0 && archive.GetArchivedLines(parent.Id).Count == 0,
                "Workbench-local retrieval does not write to the permanent archive");
        }

        using (var retentionServer = new ModelStub((_, pass) => pass == 0
            ? Calls(("request_end_workbench", "{\"notes\":[\"Retain the earlier ruling\"],\"chronicle_facts\":[]}"))
            : Text("I have prepared the review.")))
        {
            WorkbenchExitRetention? retention = null;
            var history = reloaded.GetContextHistory(restoredWorkbench);
            await RunModel(retentionServer, new StringBuilder(), onWorkbenchEnd: proposal => { retention = proposal; return Task.FromResult("Review prepared."); }, isWorkbench: true, history: history);
            var reviewRequest = retentionServer.Requests[0].ToJsonString();
            Require(reviewRequest.Contains("Temporary Workbench summary of older discussion", StringComparison.Ordinal)
                && reviewRequest.Contains("summary 2", StringComparison.Ordinal)
                && reviewRequest.Contains("request_end_workbench", StringComparison.Ordinal), "ordinary Review & End model context exposes compacted older discussion through the existing exit-tool path");
            Require(retention?.Notes.SequenceEqual(["Retain the earlier ruling"]) == true, "Review & End continues to propose retention through the existing tool");
        }

        reloaded.EndWorkbench(restoredWorkbench.Id, [], []);
        var endedState = store.Load();
        Require(endedState.Conversations.All(item => item.Id != restoredWorkbench.Id)
            && endedState.Conversations.All(item => string.IsNullOrEmpty(item.WorkbenchContextSummary))
            && archive.GetArchivedLines(restoredWorkbench.Id).Count == 0, "ending or discarding the Workbench removes its temporary compaction state");

        var failedSummaryWorkbench = reloaded.CreateWorkbench(parent.Id, "Summary failure")!;
        for (var index = 1; index <= 12; index++)
            failedSummaryWorkbench.Lines.Add(new ChatLine { Sequence = index, Role = index % 2 == 1 ? "user" : "assistant", Content = $"Uncompacted Workbench detail {index}." });
        failedSummaryWorkbench.NextSequence = 13;
        using (var server = new ModelStub((request, _) => request["stream"]?.ToString() switch
        {
            "true" => Text("The conversation remains available."),
            "false" => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "" } } } }),
            _ => JsonSerializer.Serialize(new { input_tokens = 85 })
        }))
        {
            reloaded.Endpoint = server.Endpoint;
            await reloaded.SendAsync("Keep going.", new LlamaClient(), contextLimit: 100);
        }
        Require(string.IsNullOrWhiteSpace(failedSummaryWorkbench.WorkbenchContextSummary)
            && failedSummaryWorkbench.Lines.Count == 14
            && reloaded.GetContextHistory(failedSummaryWorkbench).Any(line => line.Content.Contains("Uncompacted Workbench detail 1.", StringComparison.Ordinal)), "a failed temporary summary leaves the original Workbench transcript and forward conversation intact");
        reloaded.EndWorkbench(failedSummaryWorkbench.Id, [], []);

        var sessionRoot = Path.Combine(root, "ordinary-session");
        Directory.CreateDirectory(sessionRoot);
        var ordinaryCampaign = new Campaign { Name = "Ordinary session fixture" };
        var ordinarySession = new Conversation { CampaignId = ordinaryCampaign.Id, SessionNumber = 1, ContextTokenLimit = 100 };
        for (var index = 1; index <= 12; index++)
            ordinarySession.Lines.Add(new ChatLine { Sequence = index, Role = index % 2 == 1 ? "user" : "assistant", Content = $"Session detail {index}." });
        ordinarySession.NextSequence = 13;
        var ordinaryStore = new ConversationStore(Path.Combine(sessionRoot, "state"));
        ordinaryStore.Save(new SavedState("local", "fixture", "", null, [ordinarySession], [ordinaryCampaign], ordinaryCampaign.Id));
        var ordinaryArchive = new ChronicleArchiveStore(Path.Combine(sessionRoot, "archive.db"));
        var ordinaryState = new AppState(ordinaryStore, new LibraryService(), ordinaryArchive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(sessionRoot, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(sessionRoot, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(sessionRoot, "campaigns")));
        var activeOrdinarySession = ordinaryState.Conversations.Single(item => item.Id == ordinarySession.Id);
        ordinaryState.Select(activeOrdinarySession);
        var ordinaryCount = 0;
        using (var server = new ModelStub((request, _) => request["stream"]?.ToString() switch
        {
            "true" => Text("The Session continues."),
            "false" => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "Session continuity summary." } } } }),
            _ => JsonSerializer.Serialize(new { input_tokens = ++ordinaryCount % 2 == 1 ? 85 : 42 })
        }))
        {
            ordinaryState.Endpoint = server.Endpoint;
            await ordinaryState.SendAsync("Continue.", new LlamaClient(), contextLimit: 100);
        }
        Require(ordinaryArchive.GetArchivedLines(activeOrdinarySession.Id).Count >= 2
            && activeOrdinarySession.ArchiveSummary.Contains("Session continuity summary", StringComparison.Ordinal), "ordinary Session archival remains on its existing 80 percent permanent archive path");
    }

    private static async Task MobileHostChecks(string root, string temporary)
    {
        var data = Path.Combine(temporary, "mobile-host-tests");
        var store = new ConversationStore(data);
        var campaign = new Campaign { Name = "Mobile host fixture" };
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1" };
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        store.Save(new SavedState("local", "fixture", "", null, [conversation], [campaign], campaign.Id, MobileRemoteAccessPort: port));

        var images = Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns"));
        var audit = Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns"));
        var playerImages = Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns"));
        var mapImages = new CampaignMapAssetStore(Path.Combine(data, "campaigns"));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")), images, audit, playerImages, mapImages);
        var map = state.ImportCampaignMap(campaign.Id, "Visual fixture", "fixture.png", TestMapPng());
        var webpFixture = Convert.FromBase64String("UklGRjwAAABXRUJQVlA4IDAAAADQAQCdASoCAAIAAUAmJaACdLoB+AADsAD+8ut//NgVzXPv9//S4P0uD9Lg/9KQAAA=");
        var webpMap = state.ImportCampaignMap(campaign.Id, "WebP fixture", "fixture.webp", webpFixture);
        var jpegMap = state.ImportCampaignMap(campaign.Id, "JPEG fixture", "fixture.jpg", TestMapJpeg());
        Require(webpMap.Extension == ".webp" && state.GetOwnerMapDataUrl(campaign.Id, webpMap.Id, "thumbnail")?.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) == true, "WebP originals receive a small JPEG thumbnail");
        Require(jpegMap.Extension == ".jpg" && state.GetOwnerMapDataUrl(campaign.Id, jpegMap.Id, "original")?.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) == true, "JPEG maps preserve their original format");
        Require(state.GetImportedMaps(campaign.Id).Count == 3 && state.GetMapDisplayEntries(campaign.Id).Count == 3, "all imported maps appear in the shared campaign Maps catalogue");
        Require(state.GetCampaignMapImage(campaign.Id, map.Id, "original") is not null && state.GetCampaignMapImage(Guid.NewGuid(), map.Id, "thumbnail") is null, "map image lookup remains campaign scoped");
        state.UpdateImportedMap(campaign.Id, map.Id, "Secret cellar map");
        var renamedListing = state.GetMapDisplayEntries(campaign.Id).Single(entry => entry.Id == map.Id);
        Require(renamedListing.Title == "Secret cellar map" && renamedListing.Width == map.Width && renamedListing.Height == map.Height
            && state.GetOwnerMapDataUrl(campaign.Id, map.Id, "original") is not null, "renamed maps retain their shared catalogue details and full image");
        var serviceSource = File.ReadAllText(Path.Combine(root, "src", "DBLC", "Services.cs"));
        Require(!serviceSource.Contains("set_campaign_map_visibility", StringComparison.Ordinal), "GM tools no longer include map hide/share controls");
        var mapUiSource = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        Require(mapUiSource.Contains("State.GetMapDisplayEntries(State.ActiveCampaignId)", StringComparison.Ordinal)
            && !mapUiSource.Contains("Hidden from player", StringComparison.Ordinal)
            && !mapUiSource.Contains("Visible to player", StringComparison.Ordinal), "desktop and mobile Maps UI share one unfiltered imported-map view");

        var legacyStore = new ConversationStore(Path.Combine(data, "legacy-hidden-map"));
        var legacyCampaign = new Campaign { Id = campaign.Id, Name = campaign.Name, ImportedMaps = [new ImportedCampaignMap { Id = map.Id, Title = map.Title, Extension = map.Extension, Width = map.Width, Height = map.Height }] };
        legacyStore.Save(new SavedState("local", "fixture", "", null, [conversation], [legacyCampaign], campaign.Id));
        var legacyStatePath = Path.Combine(legacyStore.DataRoot, "state.json");
        var legacyJson = JsonNode.Parse(File.ReadAllText(legacyStatePath))!.AsObject();
        legacyJson["Campaigns"]!.AsArray()[0]!["ImportedMaps"]!.AsArray()[0]!["SharedWithPlayer"] = false;
        File.WriteAllText(legacyStatePath, legacyJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var legacyState = new AppState(legacyStore, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "legacy-map-archive.db")), images, audit, playerImages, mapImages);
        Require(legacyState.GetMapDisplayEntries(campaign.Id).Single().Title == map.Title
            && legacyState.GetCampaignMapImage(campaign.Id, map.Id, "original") is not null,
            "legacy maps with SharedWithPlayer false load and remain visible and usable");
        Require(state.OpenImportedMapForGm(campaign.Id, map.Id.ToString(), false).Image is null, "vision-disabled map opening cannot supply an image");
        Require(state.OpenImportedMapForGm(campaign.Id, map.Id.ToString(), true).Image is { MimeType: "image/jpeg" }, "GM map opening supplies bounded actual image bytes when vision is available");
        Require(store.Load().Campaigns![0].ImportedMaps.Count == 3, "imported map metadata survives campaign reload");
        var malformedRejected = false;
        try { state.ImportCampaignMap(campaign.Id, "False image", "false.png", webpFixture); }
        catch (InvalidDataException) { malformedRejected = true; }
        Require(malformedRejected && state.GetImportedMaps(campaign.Id).Count == 3, "malformed or mislabeled image import changes no campaign data");
        Require(state.ChronicleTextSizePercent == 100, "Chronicle text size defaults to Normal for existing settings");
        state.SetChronicleTextSizePercent(112);
        Require(store.Load().ChronicleTextSizePercent == 112, "Large Chronicle text size persists in settings");        var reloadedTextSize = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")), images, audit, playerImages);
        Require(reloadedTextSize.ChronicleTextSizePercent == 112, "saved Chronicle text size is restored when DBLC reloads");
        state.SetChronicleTextSizePercent(125);
        Require(store.Load().ChronicleTextSizePercent == 125 && state.ChronicleTextScaleCss == "1.25", "Extra Large Chronicle text size persists and maps to the shared CSS scale");
        state.SetChronicleTextSizePercent(100);
        Require(store.Load().ChronicleTextSizePercent == 100, "Normal Chronicle text size can be restored");
        Require(!state.GmStreamAnimationsEnabled, "stream effects are disabled for existing settings by default");
        state.GmStreamAnimationsEnabled = true; state.GmStreamDefaultEffect = "hyperspace"; state.GmInlineStreamEffectsEnabled = false; state.SaveSettings();
        var savedPresentation = store.Load();
        Require(savedPresentation.GmStreamFocusEnabled && savedPresentation.GmStreamAnimationsEnabled == true && savedPresentation.GmStreamDefaultEffect == "hyperspace" && !savedPresentation.GmInlineStreamEffectsEnabled, "stream presentation settings persist with the legacy Focus setting");
        var papers = new PaperDocumentState(state,
            Isolate(new WorkspaceNotesStore(), "_folder", Path.Combine(data, "papers")),
            Isolate(new PlayerSheetStore(), "_folder", Path.Combine(data, "papers")));
        var services = new ServiceCollection();
        var host = new MobileWebHostManager();
        services.AddSingleton(state);
        services.AddSingleton(new LlamaClient());
        services.AddSingleton(papers);
        services.AddSingleton(images);
        services.AddSingleton(new PaperWindowManager());
        services.AddSingleton(new CampaignWindowManager());
        services.AddSingleton(host);
        services.AddSingleton(playerImages);
        services.AddSingleton(new ViewOrderStore());
        services.AddSingleton(new PrivacyLockStore(Path.Combine(data, "privacy.json")));
        services.AddSingleton(new LlamaModelInfoClient());
        services.AddSingleton(new NpcPortraitStore());
        services.AddSingleton(mapImages);
        services.AddSingleton(new GmActivityCatalog());
        using var provider = services.BuildServiceProvider();
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherThread = new Thread(() =>
        {
            dispatcherReady.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        dispatcherThread.SetApartmentState(ApartmentState.STA);
        dispatcherThread.Start();
        var testDispatcher = await dispatcherReady.Task;
        var priorKeyDirectory = Environment.GetEnvironmentVariable("DBLC_MOBILE_KEY_DIRECTORY");
        Environment.SetEnvironmentVariable("DBLC_MOBILE_KEY_DIRECTORY", Path.Combine(data, "mobile-keys"));
        try
        {
            host.Initialize(provider, testDispatcher);
            await WaitFor(() => host.Status == "Mobile access is off.", "disabled mobile host status");
            using (var closed = new TcpClient())
            {
                try { await closed.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException("FAILED: host must not listen while disabled"); }
                catch (Exception ex) when (ex is SocketException or TimeoutException) { _checks++; }
            }

            var code = state.GenerateMobileAccessCode();
            state.SetMobileRemoteAccessEnabled(true);
            await WaitFor(() => host.Status.StartsWith("Ready on this PC", StringComparison.Ordinal), "enabled mobile host startup");
            var mobileServices = ((Microsoft.AspNetCore.Builder.WebApplication)typeof(MobileWebHostManager).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Services;
            using var mobileScope = mobileServices.CreateScope();
            foreach (var property in typeof(AppShell).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(property => property.GetCustomAttribute<Microsoft.AspNetCore.Components.InjectAttribute>() is not null))
                Require(mobileScope.ServiceProvider.GetService(property.PropertyType) is not null, $"mobile host resolves AppShell dependency {property.PropertyType.Name}");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            var baseUri = new Uri($"http://127.0.0.1:{port}/");
            var unauthorized = await client.GetAsync(baseUri);
            Require(unauthorized.StatusCode == HttpStatusCode.Redirect, "mobile routes require login");
            var mapUrl = new Uri(baseUri, $"campaign-maps/{campaign.Id:D}/{map.Id:D}/original");
            Require((await client.GetAsync(mapUrl)).StatusCode == HttpStatusCode.Redirect, "unauthenticated map images require login");

            var loginResponse = await client.GetAsync(new Uri(baseUri, "login"));
            var loginHtml = await loginResponse.Content.ReadAsStringAsync();
            Require(loginResponse.StatusCode == HttpStatusCode.OK && loginHtml.Contains("Mobile access code", StringComparison.Ordinal), "mobile login page served");
            var antiCookie = loginResponse.Headers.TryGetValues("Set-Cookie", out var loginCookies)
                ? loginCookies.FirstOrDefault(cookie => cookie.StartsWith("DBLC.Mobile.AntiForgery=", StringComparison.Ordinal))?.Split(';')[0]
                : null;
            var requestToken = Regex.Match(loginHtml, "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Require(!string.IsNullOrWhiteSpace(antiCookie) && !string.IsNullOrWhiteSpace(requestToken), "login includes antiforgery token and cookie");
            using var login = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "login")) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = requestToken, ["code"] = code }) };
            login.Headers.TryAddWithoutValidation("Cookie", antiCookie);
            var signedIn = await client.SendAsync(login);
            var hasAuthCookies = signedIn.Headers.TryGetValues("Set-Cookie", out var authCookies);
            Require(signedIn.StatusCode == HttpStatusCode.Redirect && hasAuthCookies, "valid code creates an auth session");
            var authCookie = authCookies?.FirstOrDefault(cookie => cookie.StartsWith("DBLC.Mobile.Session=", StringComparison.Ordinal))?.Split(';')[0];
            Require(!string.IsNullOrWhiteSpace(authCookie), "mobile auth cookie is set");
            using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, baseUri);
            authorizedRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);
            var mobilePage = await client.SendAsync(authorizedRequest);
            var mobileHtml = await mobilePage.Content.ReadAsStringAsync();
            Require(mobilePage.StatusCode == HttpStatusCode.OK && mobileHtml.Contains("What happens next?", StringComparison.Ordinal), "authenticated mobile Chronicle renders");
            var portrait = playerImages.Save(campaign.Id, "portrait.png", [137, 80, 78, 71]);
            var portraitUri = new Uri(baseUri, $"chronicle-portraits/{campaign.Id:D}/player/portrait.png");
            Require((await client.GetAsync(portraitUri)).StatusCode == HttpStatusCode.Redirect, "Chronicle portraits require authentication");
            using var portraitRequest = new HttpRequestMessage(HttpMethod.Get, portraitUri);
            portraitRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);
            var portraitResponse = await client.SendAsync(portraitRequest);
            Require(portraitResponse.StatusCode == HttpStatusCode.OK
                && (await portraitResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] {137, 80, 78, 71}), "authenticated Chronicle portrait endpoint returns original bytes");
            Require(playerImages.FindImagePath(campaign.Id, "assets/../state.json") is null, "portrait paths reject traversal");
            using var absentPortrait = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"chronicle-portraits/{Guid.NewGuid():D}/player/portrait.png"));
            absentPortrait.Headers.TryAddWithoutValidation("Cookie", authCookie);
            Require((await client.SendAsync(absentPortrait)).StatusCode == HttpStatusCode.NotFound, "portrait requests cannot resolve a different campaign's image");
            using var sharedMapRequest = new HttpRequestMessage(HttpMethod.Get, mapUrl);
            sharedMapRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);
            var sharedMapResponse = await client.SendAsync(sharedMapRequest);
            Require(sharedMapResponse.StatusCode == HttpStatusCode.OK && sharedMapResponse.Content.Headers.ContentType?.MediaType == "image/png", "authenticated mobile Maps can fetch every imported original");
            using var thumbRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"campaign-maps/{campaign.Id:D}/{map.Id:D}/thumbnail"));
            thumbRequest.Headers.TryAddWithoutValidation("Cookie", authCookie);
            Require((await client.SendAsync(thumbRequest)).StatusCode == HttpStatusCode.OK, "authenticated mobile Maps can fetch each thumbnail");

            using var crossSiteSocket = new HttpRequestMessage(HttpMethod.Get, baseUri);
            crossSiteSocket.Headers.TryAddWithoutValidation("Cookie", authCookie);
            crossSiteSocket.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
            crossSiteSocket.Headers.TryAddWithoutValidation("Upgrade", "websocket");
            crossSiteSocket.Headers.TryAddWithoutValidation("Connection", "Upgrade");
            var rejectedOrigin = await client.SendAsync(crossSiteSocket);
            Require(rejectedOrigin.StatusCode == HttpStatusCode.Forbidden, "cross-origin browser socket is rejected");

            var externalAddress = NetworkInterface.GetAllNetworkInterfaces().Where(item => item.OperationalStatus == OperationalStatus.Up)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses).Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
            if (externalAddress is not null)
            {
                using var external = new TcpClient();
                try { await external.ConnectAsync(externalAddress, port).WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException("FAILED: mobile host must bind only to loopback"); }
                catch (Exception ex) when (ex is SocketException or TimeoutException) { _checks++; }
            }

            var rotationCompleted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRotationCompleted() => rotationCompleted.TrySetResult(host.Status);
            host.Changed += OnRotationCompleted;
            string rotatedCode;
            try
            {
                rotatedCode = state.GenerateMobileAccessCode();
                var rotationStatus = await rotationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Require(rotationStatus.StartsWith("Ready on this PC", StringComparison.Ordinal), "host restarts after access-code rotation");
            }
            finally { host.Changed -= OnRotationCompleted; }

            using var oldSession = await SendAfterRestart(client, () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
                request.Headers.TryAddWithoutValidation("Cookie", authCookie);
                return request;
            });
            Require(oldSession.StatusCode == HttpStatusCode.Redirect, "rotating code revokes an existing session");

            using var rotatedLoginPage = await SendAfterRestart(client, () => new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "login")));
            var rotatedLoginHtml = await rotatedLoginPage.Content.ReadAsStringAsync();
            var rotatedAntiCookie = rotatedLoginPage.Headers.TryGetValues("Set-Cookie", out var rotatedLoginCookies)
                ? rotatedLoginCookies.FirstOrDefault(cookie => cookie.StartsWith("DBLC.Mobile.AntiForgery=", StringComparison.Ordinal))?.Split(';')[0]
                : null;
            var rotatedToken = Regex.Match(rotatedLoginHtml, "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Require(rotatedLoginPage.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(rotatedAntiCookie) && !string.IsNullOrWhiteSpace(rotatedToken), "restarted host serves a fresh login challenge");
            HttpRequestMessage RotationLogin(string accessCode)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "login"))
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = rotatedToken, ["code"] = accessCode })
                };
                request.Headers.TryAddWithoutValidation("Cookie", rotatedAntiCookie);
                return request;
            }
            using var oldCodeResponse = await SendAfterRestart(client, () => RotationLogin(code));
            Require(oldCodeResponse.StatusCode == HttpStatusCode.Unauthorized, "rotated access code rejects the previous code");
            using var newCodeResponse = await SendAfterRestart(client, () => RotationLogin(rotatedCode));
            var newSessionCookie = newCodeResponse.Headers.TryGetValues("Set-Cookie", out var newAuthCookies)
                ? newAuthCookies.FirstOrDefault(cookie => cookie.StartsWith("DBLC.Mobile.Session=", StringComparison.Ordinal))?.Split(';')[0]
                : null;
            Require(newCodeResponse.StatusCode == HttpStatusCode.Redirect && !string.IsNullOrWhiteSpace(newSessionCookie), "new access code creates a session after restart");
            using var newSession = await SendAfterRestart(client, () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
                request.Headers.TryAddWithoutValidation("Cookie", newSessionCookie);
                return request;
            });
            Require(newSession.StatusCode == HttpStatusCode.OK, "new session can reach the mobile Chronicle after rotation");

            state.SetMobileRemoteAccessEnabled(false);
            await WaitFor(() => host.Status == "Mobile access is off.", "turning off browser access stops the host");
            using var disabled = new TcpClient();
            try { await disabled.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException("FAILED: disabled mobile host must stop listening"); }
            catch (Exception ex) when (ex is SocketException or TimeoutException) { _checks++; }
            state.UpdateImportedMap(campaign.Id, map.Id, "Renamed image");
            Require(store.Load().Campaigns![0].ImportedMaps.Single(item => item.Id == map.Id).Title == "Renamed image", "map rename survives reload");
            var duplicate = state.DuplicateCampaign(campaign.Id, Guid.NewGuid());
            Require(duplicate.ImportedMaps.Count == 3 && state.GetOwnerMapDataUrl(duplicate.Id, map.Id, "original") is not null, "campaign duplication copies map metadata and originals");
            state.DeleteCampaign(duplicate.Id);
            Require(!Directory.Exists(Path.Combine(data, "campaigns", duplicate.Id.ToString("N"), "imported-maps")), "campaign deletion removes copied image assets");
            state.DeleteImportedMap(campaign.Id, map.Id);
            Require(state.GetImportedMap(campaign.Id, map.Id) is null && state.GetOwnerMapDataUrl(campaign.Id, map.Id, "original") is null, "deleting an imported map removes metadata and image bytes");
        }
        finally
        {
            try
            {
                await host.StopAsync();
                state.SetMobileRemoteAccessEnabled(false);
                await host.DisposeAsync();
            }
            finally
            {
                testDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                dispatcherThread.Join(TimeSpan.FromSeconds(2));
                Environment.SetEnvironmentVariable("DBLC_MOBILE_KEY_DIRECTORY", priorKeyDirectory);
            }
        }

        static async Task WaitFor(Func<bool> condition, string check)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(25);
            Require(condition(), check);
        }

        static async Task<HttpResponseMessage> SendAfterRestart(HttpClient client, Func<HttpRequestMessage> createRequest)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (true)
            {
                using var request = createRequest();
                try { return await client.SendAsync(request); }
                catch (HttpRequestException) when (DateTime.UtcNow < until) { await Task.Delay(50); }
            }
        }
    }

    private static async Task ArtifactChecks(string temporary, Campaign seed)
    {
        File.WriteAllBytes(Path.Combine(temporary, "image.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));
        var data = Path.Combine(temporary, "artifact-tests");
        var store = new ConversationStore(data);
        var parent = new Conversation { CampaignId = seed.Id, SessionNumber = 0 };
        store.Save(new SavedState("local", "fixture", "", null, [parent], [seed], seed.Id));
        var images = Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns"));
        var chronicleImages = Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns"));
        var audit = Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns"));
        var app = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")), chronicleImages, audit, images);
        var initialNotes = app.GetGmNotes(seed.Id);
        var portable = GmNoteTransfer.ExportJson(initialNotes);
        var roundTrip = GmNoteTransfer.Preview(portable, seed.Id, initialNotes, false);
        Require(roundTrip.CreatedCount == 0 && roundTrip.UpdatedCount == 0 && roundTrip.Skipped == initialNotes.Count, "GM Notes JSON round trip preserves IDs and contents");
        var otherCampaign = new Campaign { Name = "Portable target" };
        var otherStore = new ConversationStore(Path.Combine(data, "other-installation"));
        otherStore.Save(new SavedState("local", "fixture", "", null,
            [new Conversation { CampaignId = otherCampaign.Id }], [otherCampaign], otherCampaign.Id));
        var otherApp = new AppState(otherStore, new LibraryService(),
            new ChronicleArchiveStore(Path.Combine(data, "other-installation-archive.db")), chronicleImages, audit, images);
        otherApp.ApplyGmNoteImport(otherApp.PreviewGmNoteImport(otherCampaign.Id, portable, false));
        Require(otherStore.Load().Campaigns![0].GmNotes![0].Id == initialNotes[0].Id &&
            otherStore.Load().Campaigns![0].GmNotes![0].Text == initialNotes[0].Text, "JSON imports into a separate installation and campaign with stable IDs");
        Require(portable.Contains("\"version\": 1", StringComparison.Ordinal) && GmNoteTransfer.ExportMarkdown(initialNotes).Contains("# GM Notes", StringComparison.Ordinal), "GM Notes exports are versioned JSON and readable Markdown");
        var importedId = Guid.NewGuid();
        var candidate = GmNoteTransfer.ExportJson([new GmNote { Id = importedId, Text = "The cellar bargain\nis settled." }, new GmNote { Id = initialNotes[0].Id, Text = "Changed private ruling" }]);
        var safePreview = app.PreviewGmNoteImport(seed.Id, candidate, false);
        Require(safePreview.CreatedCount == 1 && safePreview.UpdatedCount == 0 && safePreview.Rejected == 1, "conflicting IDs are rejected by default");
        var updatePreview = app.PreviewGmNoteImport(seed.Id, candidate, true);
        Require(updatePreview.CreatedCount == 1 && updatePreview.UpdatedCount == 1, "conflicting IDs require explicit update selection");
        app.ApplyGmNoteImport(updatePreview);
        Require(app.GetGmNotes(seed.Id).Any(note => note.Id == importedId) && store.Load().Campaigns![0].GmNotes!.Any(note => note.Id == importedId), "confirmed GM Note import persists to selected campaign");
        Require(!app.SearchGmNotes(Guid.NewGuid(), "cellar").Contains(importedId.ToString("N"), StringComparison.Ordinal), "GM Note search is isolated by campaign");
        Require(app.ReadGmNote(seed.Id, importedId.ToString("N")).Contains("cellar bargain", StringComparison.Ordinal), "GM can read one matching note by stable ID");
        var citedReply = new ChatLine { Role = "assistant", Content = "The prior ruling applies.", Sequence = 701 };
        app.ReadGmNote(seed.Id, importedId.ToString("N"));
        Require(citedReply.GmNoteReferences is null, "reading a GM Note alone does not claim it informed the response");
        citedReply.GmNoteReferences = [new GmNoteReference(seed.Id, importedId, "The cellar bargain")]; // Historical snapshot compatibility.
        var citation = citedReply.GmNoteReferences!.Single();
        Require(citation.CampaignId == seed.Id && citation.NoteId == importedId && citation.Title == "The cellar bargain", "historical GM Note reference remains stable and title-only");
        Require(!JsonSerializer.Serialize(citation).Contains("is settled.", StringComparison.Ordinal), "Chronicle note metadata excludes full note text");
        var citationSnapshot = MobileChronicleSnapshot.Capture(new Conversation { Lines = [citedReply] });
        Require(citationSnapshot.Lines[0].GmNoteReferences?.Single() == citation, "mobile Chronicle snapshot carries note references");
        var citationArchiveId = Guid.NewGuid();
        var citationArchive = new ChronicleArchiveStore(Path.Combine(data, "gm-note-reference-archive.db"));
        citationArchive.Archive(citationArchiveId, [citedReply]);
        Require(citationArchive.GetArchivedLines(citationArchiveId).Single().GmNoteReferences?.Single() == citation, "archived Chronicle preserves GM Note references");
        var citationStateStore = new ConversationStore(Path.Combine(data, "gm-note-reference-state"));
        citationStateStore.Save(new SavedState("local", "fixture", "", null,
            [new Conversation { CampaignId = seed.Id, Lines = [citedReply] }], [new Campaign { Id = seed.Id }], seed.Id));
        Require(citationStateStore.Load().Conversations![0].Lines.Single().GmNoteReferences?.Single() == citation, "saved Session reload preserves GM Note references");
        var legacyArchivePath = Path.Combine(data, "legacy-note-reference-archive.db");
        using (var legacyConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyArchivePath}"))
        {
            legacyConnection.Open();
            using var legacyCommand = legacyConnection.CreateCommand();
            legacyCommand.CommandText = "CREATE TABLE archived_lines(conversation_id TEXT NOT NULL, sequence INTEGER NOT NULL, role TEXT NOT NULL, created_at TEXT NOT NULL, content TEXT NOT NULL, roll_sides INTEGER, gm_mark TEXT, presentation_json TEXT, image_attachment_key TEXT, entry_type TEXT, event_id TEXT, PRIMARY KEY(conversation_id, sequence));";
            legacyCommand.ExecuteNonQuery();
        }
        Require(new ChronicleArchiveStore(legacyArchivePath).GetArchivedLines(Guid.NewGuid()).Count == 0, "legacy Chronicle archive opens after additive note-reference migration");
        Require(app.CanOpenGmNoteReference(citation) && !app.CanOpenGmNoteReference(citation with { CampaignId = Guid.NewGuid() }), "Chronicle reference opens only in its active campaign");
        Require(app.ReadGmNotes(seed.Id).Contains("search_gm_notes", StringComparison.Ordinal) && !app.ReadGmNotes(seed.Id).Contains("is settled.", StringComparison.Ordinal), "GM Note catalogue does not dump note bodies");
        var stale = app.PreviewGmNoteImport(seed.Id, GmNoteTransfer.ExportJson([new GmNote { Text = "Stale candidate" }]), false);
        app.AddGmNote(seed.Id, "A separate new note");
        var staleRejected = false;
        try { app.ApplyGmNoteImport(stale); } catch (InvalidOperationException) { staleRejected = true; }
        Require(staleRejected && !app.GetGmNotes(seed.Id).Any(note => note.Text == "Stale candidate"), "stale import preview cannot overwrite changed campaign state");
        var interruptedId = Guid.NewGuid();
        var interruptedPlan = app.PreviewGmNoteImport(seed.Id, GmNoteTransfer.ExportJson([new GmNote { Id = interruptedId, Text = "Interrupted import" }]), false);
        var interruptedRejected = false;
        using (var heldState = new FileStream(Path.Combine(data, "DBLC", "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { app.ApplyGmNoteImport(interruptedPlan); }
            catch (IOException) { interruptedRejected = true; }
        }
        Require(interruptedRejected && !app.GetGmNotes(seed.Id).Any(note => note.Id == interruptedId) &&
            !store.Load().Campaigns![0].GmNotes!.Any(note => note.Id == interruptedId), "failed disk write rolls back the entire GM Note import in memory and on disk");
        var wrongCampaignPlan = app.PreviewGmNoteImport(Guid.NewGuid(), GmNoteTransfer.ExportJson([new GmNote { Text = "Wrong campaign" }]), false);
        var wrongCampaignRejected = false;
        try { app.ApplyGmNoteImport(wrongCampaignPlan); } catch (InvalidOperationException) { wrongCampaignRejected = true; }
        Require(wrongCampaignRejected && !app.GetGmNotes(seed.Id).Any(note => note.Text == "Wrong campaign"), "import cannot apply to a nonselected campaign");
        var malformedRejected = false;
        try { GmNoteTransfer.Preview("{", seed.Id, app.GetGmNotes(seed.Id), false); } catch (JsonException) { malformedRejected = true; }
        Require(malformedRejected, "malformed GM Note import is rejected without mutation");
        var duplicateFile = GmNoteTransfer.ExportJson([new GmNote { Id = importedId, Text = "Duplicate one" }, new GmNote { Id = importedId, Text = "Duplicate two" }]);
        Require(app.PreviewGmNoteImport(seed.Id, duplicateFile, false).Rejected == 2, "duplicate IDs and conflicts are counted before import");
        var thousands = Enumerable.Range(0, 5000).Select(index => new GmNote { Text = index == 4312 ? "Example NPC's cellar ruling" : $"Routine ruling {index}" }).ToList();
        var searchClock = System.Diagnostics.Stopwatch.StartNew();
        var targeted = GmNoteTransfer.Search(thousands, "Example NPC cellar");
        searchClock.Stop();
        Require(targeted.Contains("Example NPC", StringComparison.Ordinal) && targeted.Length < 1000 && searchClock.Elapsed < TimeSpan.FromSeconds(5), "5,000-note search is targeted, bounded, and fast");
        var relevance = GmNoteTransfer.Search([
            new GmNote { Text = "Example attends the market" },
            new GmNote { Text = "Example NPC's cellar ruling" }
        ], "Example NPC");
        Require(relevance.IndexOf("Example NPC", StringComparison.Ordinal) < relevance.IndexOf("Example attends", StringComparison.Ordinal), "multi-term GM Note search ranks the complete match first");
        var manyMatches = GmNoteTransfer.Search(thousands, "Routine");
        Require(manyMatches.Split("note_id:", StringSplitOptions.None).Length - 1 == GmNoteTransfer.MaxResults, "GM Note search caps matching results");
        var largeExport = GmNoteTransfer.ExportJson(thousands);
        var largePreview = GmNoteTransfer.Preview(largeExport, seed.Id, [], false);
        Require(largePreview.CreatedCount == 5000 && largePreview.Rejected == 0, "5,000-note export previews as an intact import");
        var whitespaceNote = new GmNote { Text = "  exact text\nwith spacing  " };
        Require(GmNoteTransfer.Preview(GmNoteTransfer.ExportJson([whitespaceNote]), seed.Id, [], false).Creates.Single().Text == whitespaceNote.Text, "portable JSON round trip preserves exact note content");
        Console.WriteLine($"GM Notes: 5,000-note local search {searchClock.Elapsed.TotalMilliseconds:0.0} ms; result {targeted.Length} chars; import preview {largePreview.CreatedCount} notes.");
        app.DeleteGmNote(seed.Id, importedId);
        Require(!app.CanOpenGmNoteReference(citation) && citation.Title == "The cellar bargain", "deleted note leaves an unavailable historical reference with its frozen title");
        var failedTurn = new ChatLine { Role = "assistant", EntryType = "technical_error", Content = "Could not reach the model endpoint." };
        var historyProbe = new Conversation { Lines = [new ChatLine { Role = "user", Content = "Try again." }, failedTurn, new ChatLine { Role = "assistant", Content = "A real GM response." }] };
        Require(app.GetContextHistory(historyProbe).Count == 2 && historyProbe.Lines.Contains(failedTurn), "technical error remains in Chronicle but is excluded from GM history by entry type");
        Require(!app.MobileRemoteAccessEnabled && !app.HasMobileAccessCode, "mobile host defaults disabled without a code");
        var mobileCode = app.GenerateMobileAccessCode();
        Require(mobileCode.Length == 48 && app.VerifyMobileAccessCode(mobileCode) && !app.VerifyMobileAccessCode("wrong"), "mobile code has high entropy and verifies safely");
        app.SetMobileRemoteAccessEnabled(true);
        Require(app.MobileRemoteAccessEnabled, "mobile host can be enabled after code creation");
        app.SetMobileRemoteAccessEnabled(false);
        var reloadedMobile = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")), chronicleImages, audit, images);
        Require(!reloadedMobile.MobileRemoteAccessEnabled && reloadedMobile.VerifyMobileAccessCode(mobileCode), "mobile access choice and code hash persist");
        var workbench = app.CreateWorkbench(parent.Id, "Level up fixture")!;
        var tools = app.GetBestiaryTools(seed.Id);
        var proposal = tools.ProposeUpdate("Goblin", "HP 4", "Updated field note");
        Require(!proposal.IsCompleted && app.FindBestiaryEntry(seed.Id, "Goblin")!.StatBlock == "HP 3", "Bestiary proposal does not write");
        app.CancelBestiaryProposal(); Require((await proposal).Contains("rejected"), "Bestiary rejection result");
        proposal = tools.ProposeUpdate("Goblin", "HP 4", "Updated field note");
        app.ApproveBestiaryProposal(); await proposal;
        Require(app.FindBestiaryEntry(seed.Id, "Goblin")!.PortraitExtension == ".png", "approved Bestiary keeps portrait");

        // Deny replacement by holding the destination without write/delete sharing.
        proposal = tools.ProposeUpdate("Goblin", "HP 99", "Must not stick");
        using (var held = new FileStream(Path.Combine(data, "DBLC/state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { app.ApproveBestiaryProposal(); throw new Exception("Write should fail"); } catch (IOException) { }
            Require(app.FindBestiaryEntry(seed.Id, "Goblin")!.StatBlock == "HP 4", "failed approval rolls back in-memory Bestiary");
        }
        app.CancelBestiaryProposal(); await proposal;
        proposal = tools.Create("Troll", "HP 8", "Synthetic");
        using (var held = new FileStream(Path.Combine(data, "DBLC/state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { app.ApproveBestiaryProposal(); throw new Exception("Write should fail"); } catch (IOException) { }
            Require(app.FindBestiaryEntry(seed.Id, "Troll") is null, "failed creation rolls back");
        }
        app.CancelBestiaryProposal(); await proposal;
        app.AddOpenThread(seed.Id, "Public loose end", "player"); app.AddOpenThread(seed.Id, "Private loose end", "gm");
        Require(app.GetPlayerOpenThreads(seed.Id).Count == 1 && app.GetOpenThreads(seed.Id).Count == 2, "thread visibility");
        var settings = new OpenThreadReminderSettings { MinimumBeats = 1, MaximumBeats = 1 };
        var plan = OpenThreadReminderScheduler.Prepare(settings, new(), app.GetOpenThreads(seed.Id));
        Require(plan.Context is not null && plan.ThreadId is not null, "one reminder available");
        var privateReminder = OpenThreadReminderScheduler.Prepare(settings, new(), app.GetOpenThreads(seed.Id).Where(thread => thread.Visibility == "gm").ToList());
        Require(privateReminder.Context?.Contains("Visibility: gm", StringComparison.Ordinal) == true
            && privateReminder.Context.Contains("GM-private knowledge", StringComparison.Ordinal), "private thread reminder retains its visibility boundary");
        var publicReminder = OpenThreadReminderScheduler.Prepare(settings, new(), app.GetOpenThreads(seed.Id).Where(thread => thread.Visibility == "player").ToList());
        Require(publicReminder.Context?.Contains("Visibility: player", StringComparison.Ordinal) == true, "public thread reminder retains its visibility boundary");
        app.EndWorkbench(workbench.Id, []);
        Require(store.Load().Campaigns![0].Bestiary[0].StatBlock == "HP 4" && store.Load().Campaigns![0].OpenThreads.Count == 2, "Workbench artifacts survive discard/reload");

        // The pending Workbench review and its decision belong to shared AppState,
        // so desktop and mobile resolve the same one-time approval.
        var sharedReviewWorkbench = app.CreateWorkbench(parent.Id, "Shared exit review")!;
        app.Busy = true; // The GM tool is waiting for the player's review.
        Require(app.TryBeginWorkbenchExitReview(sharedReviewWorkbench.Id, out var reservation), "GM can reserve one shared exit review during its turn");
        Require(!app.TryBeginWorkbenchExitReview(sharedReviewWorkbench.Id, out _), "a second client cannot prepare a duplicate review");
        var retention = new WorkbenchExitRetention(["A reviewed note"], ["A reviewed campaign fact"]);
        Require(app.PublishWorkbenchExitReview(reservation, retention, out var approval) && approval is not null, "GM publishes a shared review and awaits its decision");
        Require(app.PendingWorkbenchExitReview?.WorkbenchId == sharedReviewWorkbench.Id && app.WorkbenchExitReviewBusy && app.Status == "Waiting for Workbench exit review…", "shared review is visible and has a clear waiting status");
        Require(app.ResolveWorkbenchExitReview(WorkbenchExitReviewAction.Approve) is { Action: WorkbenchExitReviewAction.Approve } && app.Status == "Waiting for GM output…", "first client approval is accepted once and resumes GM status");
        Require(await approval! == "The player approved ending the Workbench and reviewed the proposed retention. Continue the current reply; DBLC will discard the Workbench after this GM turn completes.", "approval releases the waiting GM tool with the intended decision");
        Require(app.ResolveWorkbenchExitReview(WorkbenchExitReviewAction.Approve) is null, "a duplicate approval cannot be applied twice");
        app.Busy = false;
        typeof(AppState).GetMethod("CompleteWorkbenchExitAfterTurn", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [sharedReviewWorkbench.Id]);
        Require(!app.Conversations.Any(item => item.Id == sharedReviewWorkbench.Id) && app.Current?.Id == parent.Id, "approved GM exit closes once after its turn and returns to the parent Session");
        Require(store.Load().Campaigns![0].GmNotes!.Any(note => note.Text == "A reviewed note") && app.Conversations.Single(item => item.Id == parent.Id).Lines.Any(line => line.Content.Contains("A reviewed campaign fact", StringComparison.Ordinal)), "approved review applies only its reviewed artifacts");

        var duplicateRetentionWorkbench = app.CreateWorkbench(parent.Id, "Duplicate note retention")!;
        var retainedNoteCount = app.GetGmNotes(seed.Id).Count;
        app.EndWorkbench(duplicateRetentionWorkbench.Id, ["A reviewed note"]);
        Require(app.GetGmNotes(seed.Id).Count == retainedNoteCount, "Workbench retention locally skips an exact approved GM Note duplicate without loading all notes into its prompt");
        Require(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "src/DBLC/AppShell.razor")).Contains("State.PendingWorkbenchExitReview", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "src/DBLC/MobileShell.razor")).Contains("State.PendingWorkbenchExitReview", StringComparison.Ordinal), "desktop and mobile render the same AppState Workbench review");

        var playerStore = Isolate(new PlayerSheetStore(), "_folder", Path.Combine(data, "papers"));
        var notesStore = Isolate(new WorkspaceNotesStore(), "_folder", Path.Combine(data, "papers"));
        var papers = new PaperDocumentState(app, notesStore, playerStore);
        var asset = images.Save(seed.Id, "portrait.png", File.ReadAllBytes(Path.Combine(temporary, "image.png")));
        images.SaveFrame(seed.Id, asset.MarkdownPath, new PlayerImageFrame(35, -20, 2));
        var original = $"![portrait]({asset.MarkdownPath})\n\n# Original\nUnrelated note.";
        papers.SetPlayerMarkdown(original); papers.SaveAll();
        var replacement = images.PreserveExistingImageReferences(seed.Id, original, "# Updated\nUnrelated note.\nSword");
        papers.ApplyApprovedPlayerUpdate(seed.Id, original, replacement);
        Require(playerStore.Load(seed.Id).Contains(asset.MarkdownPath) && images.Find(seed.Id, asset.MarkdownPath)!.Frame.Zoom == 2, "Player portrait/frame preserved on update");
        papers.RestorePreviousPlayerSheet(); Require(playerStore.Load(seed.Id) == original, "one-step undo restores exact sheet");
        papers.SetNotes("old"); papers.SetNotes("newest"); papers.SaveAll(); await Task.Delay(480);
        Require(notesStore.Load(seed.Id) == "newest", "autosave cannot overwrite explicit save with stale text");
        var portraitStore = Isolate(new NpcPortraitStore(), "_root", Path.Combine(data, "portraits"));
        var npcId = Guid.NewGuid();
        var extension = portraitStore.Save(seed.Id, npcId, Path.Combine(temporary, "image.png"));
        portraitStore.SaveFrame(seed.Id, npcId, extension, new NpcPortraitFrame(20, -30, 2));
        var browserNpcId = Guid.NewGuid();
        var browserImage = await File.ReadAllBytesAsync(Path.Combine(temporary, "image.png"));
        var browserExtension = await portraitStore.SaveAsync(seed.Id, browserNpcId, "phone.webp", new AsyncOnlyReadStream(browserImage));
        var browserPortrait = portraitStore.ReadDataUrl(seed.Id, browserNpcId, browserExtension);
        Require(browserExtension == ".webp" && browserPortrait == "data:image/webp;base64," + Convert.ToBase64String(browserImage), "browser NPC portrait uploads use asynchronous file-stream reads");
        var browserBestiaryId = Guid.NewGuid();
        var bestiaryExtension = await portraitStore.SaveBestiaryPortraitAsync(seed.Id, browserBestiaryId, "creature.jpg", new AsyncOnlyReadStream(browserImage));
        Require(bestiaryExtension == ".jpg" && portraitStore.ReadBestiaryDataUrl(seed.Id, browserBestiaryId, bestiaryExtension) == "data:image/jpeg;base64," + Convert.ToBase64String(browserImage), "browser Bestiary portrait uploads use asynchronous file-stream reads");
        Require(portraitStore.ReadFrame(seed.Id, npcId, extension).X == 20, "NPC framing metadata round trip");
        Require(images.Find(seed.Id, asset.MarkdownPath)!.Frame.X == 35, "framing metadata round trip");
        var diagnosticFile = Path.Combine(temporary, "diagnostics.jsonl");
        var diagnostics = Isolate(new GmRequestDiagnostics(), "_path", diagnosticFile);
        diagnostics.RecordContext(new JsonArray(Obj("""{"role":"system","content":"PRIVATE_SENTINEL"}""")), TimeSpan.Zero);
        diagnostics.RecordTool(1, "read_player", "PRIVATE_ARGUMENT", TimeSpan.Zero, "PRIVATE_RESULT", true);
        diagnostics.Complete("completed");
        var log = File.ReadAllText(diagnosticFile);
        Require(!log.Contains("PRIVATE_") && log.Contains("system_characters") && log.Contains("state_save_ms"), "diagnostics contain only counts/hashes/timings");
        using (var held = new FileStream(Path.Combine(data, "DBLC/state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string? rejection = null;
            await app.SendAsync("Synthetic save failure", new LlamaClient(), diagnostics: diagnostics, onRejected: reason => rejection = reason);
            Require(rejection?.Contains("draft was kept", StringComparison.OrdinalIgnoreCase) == true, "pre-accept save failure explicitly rejects and retains the draft");
            Require(!app.Busy && !app.CanSteerCurrentResponse, "save failure releases busy/steering state");
        }
    }

    private sealed class AsyncOnlyReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous browser file reads are unsupported.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous browser file reads are unsupported.");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    private static T Isolate<T>(T instance, string field, string root)
    {
        typeof(T).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, root);
        return instance;
    }

    private static void LibraryChecks(string temporary)
    {
        var folder = Path.Combine(temporary, "library"); Directory.CreateDirectory(folder);
        var pdf = Path.Combine(folder, "Rules.pdf"); WritePdf(pdf);
        var json = Path.Combine(folder, "Rules.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Rules", source_pdf = "Rules.pdf" },
            sections = new[] { new { id = "creation", title = "Creation", page_start = 1, page_end = 1, content = "CREATION_ONLY" }, new { id = "advancement", title = "Advancement", page_start = 1, page_end = 1, content = "ADVANCEMENT_ONLY" } }, future_field = "allowed" }));
        var library = new LibraryService(); var book = library.GetBooks(folder).Single();
        Require(book.JsonCompanionPath == json, "exact companion pair");
        var catalogue = library.GetJsonCatalogue(json, pdf, 1);
        Require(catalogue.Contains("creation") && !catalogue.Contains("CREATION_ONLY"), "catalogue excludes contents");
        var section = library.ReadJsonSections(json, pdf, 1, ["advancement"]);
        Require(section.Contains("ADVANCEMENT_ONLY") && !section.Contains("CREATION_ONLY"), "section retrieval remains bounded");
        File.WriteAllText(json, "{\"schema_version\":\"1.0\",\"book\":{\"title\":\"Rules\",\"source_pdf\":\"Rules.pdf\"},\"sections\":[{\"id\":\"parent\",\"title\":\"Parent\",\"page_start\":1,\"page_end\":1,\"content\":\"PARENT_ONLY\",\"subsections\":[{\"id\":\"child\",\"title\":\"Child\",\"page_start\":1,\"page_end\":1,\"content\":\"CHILD_ONLY\"}]}]}");
        var parentChild = library.ReadJsonSections(json, pdf, 1, ["child", "parent"]);
        Require(Regex.Matches(parentChild, "CHILD_ONLY").Count == 1 && parentChild.Contains("PARENT_ONLY", StringComparison.Ordinal)
            && parentChild.Contains("Requested child sections already included", StringComparison.Ordinal),
            "requesting a child and its parent emits the complete parent once while retaining child coverage");
        Require(library.ReadPages(pdf, 1, 1).Contains("Fixture page"), "PDF retrieval unchanged");
        File.Move(json, Path.Combine(folder, "Rule.json"));
        Require(library.GetBooks(folder).Single().JsonCompanionPath is null, "near-name JSON is not paired");
        File.WriteAllText(json, "malformed"); Require(library.GetBooks(folder).Single().JsonCompanionPath is null, "malformed companion ignored");
    }

    internal static void RulesIndexChecks(string temporary)
    {
        var folder = Path.Combine(temporary, "rules-index"); Directory.CreateDirectory(folder);
        var pdf = Path.Combine(folder, "Open Game Rules.pdf"); WritePdf(pdf);
        var json = Path.Combine(folder, "Open Game Rules.json");
        File.WriteAllText(json, JsonSerializer.Serialize(new { schema_version = "1.0", book = new { title = "Open Game Rules", source_pdf = "Open Game Rules.pdf" },
            sections = new[] { new { id = "combat-basics", title = "Combat Basics", page_start = 1, page_end = 1, content = "Rule text is not part of the index." } } }));
        var library = new LibraryService();
        var small = library.GetRulesIndex(folder, out var initialBooks);
        Require(small.Contains("Open Game Rules", StringComparison.Ordinal) && small.Contains("id: " + Path.GetFullPath(pdf), StringComparison.Ordinal)
            && small.Contains("  - combat-basics | Combat Basics | p.1", StringComparison.Ordinal), "small companion library gets a section-level metadata index with usable IDs and page position");
        Require(!small.Contains("Rule text is not part", StringComparison.Ordinal) && Encoding.UTF8.GetByteCount(small) <= LibraryService.RulesIndexUtf8ByteBudget,
            "index contains no rule text and stays within its fixed UTF-8 byte ceiling");

        var updated = File.ReadAllText(json).Replace("Combat Basics", "Combat and Defense", StringComparison.Ordinal);
        File.WriteAllText(json, updated);
        var refreshed = library.GetRulesIndex(folder, out _);
        Require(refreshed.Contains("Combat and Defense", StringComparison.Ordinal) && !refreshed.Contains("Combat Basics", StringComparison.Ordinal),
            "cached index refreshes after a paired companion changes");

        for (var book = 0; book < 4; book++)
        {
            var name = $"Reference Volume {book}.pdf"; var path = Path.Combine(folder, name); WritePdf(path);
            var sections = Enumerable.Range(0, 24).Select(section => new
            {
                id = $"ref-{book}-section-{section}", title = $"Reference Topic {book}-{section}", page_start = 1, page_end = 1, content = ""
            }).ToArray();
            File.WriteAllText(Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ".json"), JsonSerializer.Serialize(new
            {
                schema_version = "1.0", book = new { title = $"Reference Volume {book}", source_pdf = name }, sections
            }));
        }
        var compact = library.GetRulesIndex(folder, out var refreshedBooks);
        Require(compact.Contains("book directory only", StringComparison.Ordinal) && !compact.Contains("section_id:", StringComparison.Ordinal)
            && Encoding.UTF8.GetByteCount(compact) <= LibraryService.RulesIndexUtf8ByteBudget
            && refreshedBooks.Count == initialBooks.Count + 4,
            "larger companion library falls back to a bounded book-level directory and refreshes the tool book list");

        var buildMessages = typeof(LlamaClient).GetMethod("BuildMessages", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = buildMessages.GetParameters().Select(parameter => parameter.Name switch
        {
            "history" => (object)new List<ChatLine> { new() { Role = "user", Content = "Which book covers combat?" } },
            "archiveSummary" => "", "rulesIndex" => (object)small,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue! : parameter.ParameterType == typeof(bool) ? false : ""
        }).ToArray();
        var messages = (JsonArray)buildMessages.Invoke(null, args)!;
        var system = (string)messages[0]!["content"]!;
        Require(system.Contains(small, StringComparison.Ordinal) && system.IndexOf(small, StringComparison.Ordinal) < system.IndexOf("GM PROFILE", StringComparison.Ordinal),
            "rule index appears once near the stable beginning of the system prefix");
    }

    private static void WritePdf(string path, string pageText = "Fixture page")
    {
        var text = $"BT /F1 12 Tf 30 100 Td ({pageText}) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {text.Length} >>\nstream\n{text}\nendstream"];
        var output = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(output.Length); output.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = output.Length; output.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) output.Append($"{offset:0000000000} 00000 n \n");
        output.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, output.ToString(), Encoding.ASCII);
    }

    private static async Task ModelChecks()
    {
        var reads = 0;
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("request_end_workbench", "{\"notes\":[\"Table ruling\"],\"chronicle_facts\":[\"The chapel is sealed\"]}"))
            : Text("The GM gives a brief goodbye.")))
        {
            WorkbenchExitRetention? proposed = null;
            var visible = new StringBuilder();
            await RunModel(server, visible, onWorkbenchEnd: retention => { proposed = retention; return Task.FromResult("The player approved; do not request closure again this turn."); }, isWorkbench: true);
            Require(proposed?.Notes.SequenceEqual(["Table ruling"]) == true && proposed.ChronicleFacts.SequenceEqual(["The chapel is sealed"]), "GM close tool passes its own reviewed proposals without a second model request");
            Require(server.Requests.Count == 2 && visible.ToString() == "The GM gives a brief goodbye.", "Workbench closure proposal and confirmation use the normal GM inference/tool continuation");
        }
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("read_player", "{}"), ("read_workspace", "{}")) : Text("Complete.")))
        {
            var output = new StringBuilder();
            await RunModel(server, output, readPlayer: () => { reads++; return "Player fixture"; });
            Require(server.Requests.Count == 2 && reads == 1 && output.ToString() == "Complete.", "multi-tool turn uses only one continuation");
            var second = server.Requests[1]["messages"]!.AsArray();
            Require(second.OfType<JsonObject>().Count(m => (string?)m["role"] == "tool") == 2, "one result per tool call");
            var findLateSystem = typeof(LlamaClient).GetMethod("FindMisorderedSystemMessage", BindingFlags.NonPublic | BindingFlags.Static)!;
            Require(server.Requests.All(request => findLateSystem.Invoke(null, [request["messages"]]) is null), "normal GM/tool-continuation requests pass ordering check");
        }
        const string toolPreamble = "Let me check my notes before I continue.";
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Delta(new { content = toolPreamble }) + Calls(("read_workspace", "{}"))
            : Text("The rulebook says…")))
        {
            var visible = new StringBuilder();
            await RunModel(server, visible,
                onToken: token => visible.Append(token));
            Require(visible.ToString() == toolPreamble + "The rulebook says…", "streamed tool-call preamble remains visible through the continuation");
            Require(server.Requests[1]["messages"]!.ToJsonString().Contains(toolPreamble), "tool-call preamble remains in the valid in-flight assistant/tool exchange");
        }
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Delta(new { content = toolPreamble }) + Calls(("read_workspace", "{}"))
            : Calls(("present_response", "{\"segments\":[{\"speaker\":\"GM\",\"text\":\"Final answer.\"}]}"))))
        {
            GmPresentation? finalPresentation = null;
            var visible = new StringBuilder();
            await RunModel(server, visible, onToken: token => visible.Append(token), presentation: presentation => finalPresentation = presentation);
            Require(string.Concat(finalPresentation!.Segments.Select(segment => segment.Text)) == toolPreamble + "Final answer.", "tool-loop stream text appears once in the final presentation");
            Require(visible.ToString() == toolPreamble, "presentation handoff does not retract already streamed tool-loop text");
        }
        using (var server = new ModelStub((_, _) => Calls(("read_player", "{}"), done: false)))
        {
            reads = 0;
            try { await RunModel(server, new(), readPlayer: () => { reads++; return "unsafe"; }); throw new Exception("Incomplete stream accepted"); }
            catch (IOException) { }
            Require(reads == 0, "EOF cannot execute a tool");
        }
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("propose_player_update", "{\"markdown\":\"# New sheet\"}")) : Text("Reviewed.")))
        {
            var review = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = RunModel(server, new(), propose: _ => { entered.SetResult(); return review.Task; });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(!task.IsCompleted && server.Requests.Count == 1, "approval waits without inference");
            review.SetResult("Player rejected the proposed Player Sheet update. The existing Player Sheet is unchanged.");
            await task;
            Require(server.Requests[1]["messages"]!.ToJsonString().Contains("rejected"), "rejection reaches model");
        }
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("propose_player_update", "{}"), ("propose_player_update", "{}")) : Text("Retry separately.")))
        {
            var approvals = 0;
            await RunModel(server, new(), propose: _ => { approvals++; return Task.FromResult("approved"); });
            Require(approvals == 0 && server.Requests[1]["messages"]!.AsArray().OfType<JsonObject>().Count(m => (string?)m["role"] == "tool") == 2, "multiple proposals cannot leave dangling tool IDs");
        }
        var reply = new ChatLine(); var steering = new ResponseSteeringSession(Guid.NewGuid(), reply);
        using (var server = new ModelStub((_, pass) => Text(pass == 0 ? "The door—" : "opens in lantern light.")))
        {
            var once = false;
            await RunModel(server, new(), steering: steering, onToken: token =>
            {
                reply.Content += token;
                if (!once) { once = true; steering.Queue(() => new ChatLine { Role = "user", Content = "I carry the lantern." }, out _); }
            });
            Require(reply.Content == "The door—opens in lantern light.", "steering preserves exact streamed prefix");
            Require(server.Requests.Count == 2 && server.Requests[1]["messages"]!.ToJsonString().Contains("I carry the lantern."), "steer enters genuine context");
        }
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("read_player", "{}")) : Text("Image read.")))
        {
            await RunModel(server, new(), image: new LlamaVisionImage("image/png", "data:image/png;base64,fixture"));
            Require(server.Requests.All(r => r["messages"]!.ToJsonString().Contains("image_url")), "vision survives current tool loop");
        }
        using (var server = new ModelStub((_, _) => Calls(("present_response", "{\"gm_mark\":\"◇\",\"segments\":[{\"speaker\":\"GM\",\"text\":\"Clean prose\",\"flavor\":\"GLITCH_CURSED\"}]}"))))
        {
            GmPresentation? presentation = null;
            await RunModel(server, new(), presentation: p => presentation = p);
            Require(server.Requests.Count == 1 && presentation!.Segments.Single().Text == "Clean prose", "presentation ends without extra inference");
        }
        using (var server = new ModelStub((_, _) => Text("Ordinary response.")))
        {
            await RunModel(server, new());
            Require(server.Requests.Count == 1 && !server.Requests[0]["messages"]!.ToJsonString().Contains("image_url"), "ordinary turn has no historical image");
        }
        var stoppedReply = new ChatLine(); var stop = new ResponseSteeringSession(Guid.NewGuid(), stoppedReply);
        using (var server = new ModelStub((_, _) => Text("Preserve this prefix.")))
        {
            await RunModel(server, new(), steering: stop, onToken: token => { stoppedReply.Content += token; stop.RequestStop(); });
            Require(stoppedReply.Content == "Preserve this prefix." && server.Requests.Count == 1, "stop preserves prose without continuation");
        }
        var markdown = "# Character\n" + new string('x', 10000);
        var arguments = JsonSerializer.Serialize(new { markdown });
        var fragmented = Delta(new { tool_calls = new[] { new { index = 0, id = "fragmented", function = new { name = "propose_player_update", arguments = "" } } } });
        foreach (var chunk in arguments.Chunk(37)) fragmented += Delta(new { tool_calls = new[] { new { index = 0, function = new { arguments = new string(chunk) } } } });
        using (var server = new ModelStub((_, pass) => pass == 0 ? fragmented + "data: [DONE]\n\n" : Text("Approved.")))
        {
            string? captured = null;
            await RunModel(server, new(), propose: proposed => { captured = proposed; return Task.FromResult("Player approved the proposed Player Sheet update."); });
            Require(captured == markdown && server.Requests.Count == 2, "fragmented long tool argument is exact");
        }
    }

    private static async Task ChronicleTimestampChecks(string root, string temporary)
    {
        var data = Path.Combine(temporary, "chronicle-completion-time");
        var campaign = new Campaign { Name = "Completion time fixture" };
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1", IsWorkbench = true };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [conversation], [campaign], campaign.Id));
        var archive = new ChronicleArchiveStore(Path.Combine(data, "archive.db"));
        var state = new AppState(store, new LibraryService(), archive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns")));
        DateTimeOffset? acceptedPlayerTime = null;
        DateTimeOffset? initialGmTime = null;
        using (var server = new ModelStub((request, _) =>
        {
            if (request["messages"] is JsonArray) Thread.Sleep(1200);
            return Text("This reply arrives after a deliberate delay.");
        }))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("A delayed request.", new LlamaClient(), onAccepted: () =>
            {
                acceptedPlayerTime = state.Current!.Lines[0].CreatedAt;
                initialGmTime = state.Current.Lines[1].CreatedAt;
            });
        }

        var completedLines = state.Current!.Lines.TakeLast(2).ToArray();
        var player = completedLines[0];
        var gm = completedLines[1];
        Require(player.Role == "user" && player.CreatedAt == acceptedPlayerTime && player.CompletedAt is null,
            "player Chronicle time remains the send timestamp");
        Require(gm.Role == "assistant" && gm.CreatedAt == initialGmTime && gm.CompletedAt > gm.CreatedAt
            && gm.DisplayTimestamp == gm.CompletedAt,
            "successful GM completion adds a later display timestamp without replacing its original timestamp");
        var reloaded = store.Load().Conversations!.Single().Lines.TakeLast(2).ToArray();
        Require(reloaded[0].CreatedAt == acceptedPlayerTime && reloaded[0].CompletedAt is null
            && reloaded[1].CreatedAt == initialGmTime && reloaded[1].CompletedAt == gm.CompletedAt,
            "player send time and finalized GM display time survive session reload");

        archive.Archive(state.Current.Id, [gm]);
        var archived = archive.GetArchivedLines(state.Current.Id).Single();
        Require(archived.CreatedAt == gm.CreatedAt && archived.CompletedAt == gm.CompletedAt
            && archived.DisplayTimestamp == gm.DisplayTimestamp,
            "archive round trip preserves the GM completion display time separately from its original timestamp");

        var desktop = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        var mobile = File.ReadAllText(Path.Combine(root, "src", "DBLC", "MobileShell.razor"));
        Require(desktop.Contains("line.DisplayTimestamp.ToLocalTime()", StringComparison.Ordinal)
            && mobile.Contains("line.DisplayTimestamp.ToLocalTime()", StringComparison.Ordinal),
            "desktop and mobile Chronicle render the same persisted display timestamp");
    }

    private static async Task ChronicleOrderAndArrowChecks(string temporary)
    {
        const string arrowSource = "Gain $\\rightarrow$ 25 silver; $\\leftarrow$ return; $\\leftrightarrow$ exchange.";
        var arrowHtml = ChronicleMarkdownRenderer.Render(arrowSource);
        Require(arrowHtml.Contains("→ 25 silver", StringComparison.Ordinal) && arrowHtml.Contains("← return", StringComparison.Ordinal)
            && arrowHtml.Contains("↔ exchange", StringComparison.Ordinal), "simple model LaTeX arrows render as Unicode in saved prose");
        Require(ChronicleMarkdownRenderer.NormalizeProseSymbols("Gain $\\right", true) == "Gain "
            && ChronicleMarkdownRenderer.NormalizeProseSymbols(arrowSource, true).Contains("→ 25 silver", StringComparison.Ordinal),
            "split streaming arrow tokens append only their finished Unicode symbol");
        var protectedSource = "`$\\rightarrow$`\n\n```text\n$\\leftarrow$\n```\n\n$x \\rightarrow y$ and \\$\\rightarrow$";
        var protectedHtml = ChronicleMarkdownRenderer.Render(protectedSource);
        Require(protectedHtml.Contains("<code>$\\rightarrow$</code>", StringComparison.Ordinal), "inline-code arrow stays literal");
        Require(protectedHtml.Contains("$\\leftarrow$", StringComparison.Ordinal), "fenced-code arrow stays literal");
        Require(ChronicleMarkdownRenderer.NormalizeProseSymbols(protectedSource).Contains("$x \\rightarrow y$", StringComparison.Ordinal),
            "genuine math stays unchanged in the presentation normalization source");

        var data = Path.Combine(temporary, "chronicle-order");
        var campaign = new Campaign { Name = "Order fixture" };
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1", IsWorkbench = true };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [conversation], [campaign], campaign.Id));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns")));
        var steered = false;
        state.Changed += () =>
        {
            if (steered || state.Current?.Lines.LastOrDefault()?.Content != "The GM finishes the breakdown.") return;
            steered = state.SteerCurrentResponse("I will change the silver myself.");
        };
        var firstPresentation = JsonSerializer.Serialize(new { segments = new[] { new { speaker = "GM", text = "The GM finishes the breakdown." } } });
        var continuationPresentation = JsonSerializer.Serialize(new { segments = new[] { new { speaker = "GM", text = "Understood. The ledger remains yours." } } });
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("present_response", firstPresentation)) : Calls(("present_response", continuationPresentation))))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Review the character stats.", new LlamaClient());
            Require(steered && server.Paths.Count(path => path.EndsWith("/chat/completions", StringComparison.Ordinal)) == 2,
                "steer at the final presentation handoff continues through one normal tool round");
        }
        var lines = state.Current!.Lines;
        Require(lines.Select(line => line.Role).SequenceEqual(["user", "assistant", "user", "assistant"])
            && lines[1].Content == "The GM finishes the breakdown."
            && lines[2].Content == "I will change the silver myself."
            && lines[3].Content == "Understood. The ledger remains yours.",
            "already-visible GM narration remains before a late steer without duplicating the presentation prefix");
        Require(lines.Select(line => line.Sequence).SequenceEqual(lines.Select(line => line.Sequence).OrderBy(sequence => sequence))
            && lines[1].PresentationSegments?.Single().Id != lines[3].PresentationSegments?.Single().Id,
            "steering preserves chronological sequence and distinct stable segment identities");
        var saved = store.Load().Conversations.Single().Lines;
        Require(saved.Select(line => line.Id).SequenceEqual(lines.Select(line => line.Id))
            && saved.Select(line => line.Content).SequenceEqual(lines.Select(line => line.Content)),
            "session reload retains the same Chronicle order and text after a steer");
        using (var server = new ModelStub((_, _) => Text("The next response follows.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("A fresh message after completion.", new LlamaClient());
        }
        Require(state.Current!.Lines.TakeLast(2).Select(line => line.Role).SequenceEqual(["user", "assistant"])
            && state.Current.Lines[^2].Sequence < state.Current.Lines[^1].Sequence,
            "a message sent immediately after completion starts a new chronological turn");

        var streamSteered = false;
        state.Changed += () =>
        {
            if (streamSteered || state.Current?.Lines.LastOrDefault()?.Content != "First visible text.") return;
            streamSteered = state.SteerCurrentResponse("Interrupt after the visible text.");
        };
        using (var server = new ModelStub((_, pass) => Text(pass == 0 ? "First visible text." : "Continuation after steer.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Begin an ordinary response.", new LlamaClient());
        }
        Require(streamSteered && state.Current!.Lines.TakeLast(4).Select(line => line.Role).SequenceEqual(["user", "assistant", "user", "assistant"])
            && state.Current.Lines[^3].Content == "First visible text."
            && state.Current.Lines[^1].Content == "Continuation after steer.",
            "plain streamed narration also stays ahead of a mid-response player steer");

        state.CharacterSpeakersEnabled = true;
        state.GmStreamAnimationsEnabled = true;
        var speakerSteered = false;
        state.Changed += () =>
        {
            if (speakerSteered || state.Current?.Lines.LastOrDefault()?.PresentationSegments is not { Count: 2 } segments ||
                segments[1].Speaker != "Example NPC") return;
            speakerSteered = state.SteerCurrentResponse("Ask Example NPC to wait.");
        };
        var speakerStart = JsonSerializer.Serialize(new { segments = new[]
        {
            new { speaker = "GM", text = "The ledger opens." },
            new { speaker = "Example NPC", text = "[[smoke]]The debt stands." }
        } });
        var speakerEnd = JsonSerializer.Serialize(new { segments = new[] { new { speaker = "GM", text = "[[focus]]She closes the ledger." } } });
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("present_response", speakerStart)) : Calls(("present_response", speakerEnd))))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Open the ledger.", new LlamaClient());
        }
        var speakerLines = state.Current!.Lines.TakeLast(4).ToArray();
        Require(speakerSteered && speakerLines.Select(line => line.Role).SequenceEqual(["user", "assistant", "user", "assistant"])
            && speakerLines[1].PresentationSegments?.Select(segment => segment.Speaker).SequenceEqual(["GM", "Example NPC"]) == true
            && speakerLines[1].PresentationSegments![1].NarrationEffectSpans?.Any(span => span.Effect == "smoke") == true
            && speakerLines[3].PresentationSegments?.Single().Speaker == "GM"
            && speakerLines[3].PresentationSegments![0].NarrationEffectSpans?.Any(span => span.Effect == "focus") == true,
            "speaker and presentation-effect segments remain in chronological replies around a steer");

        var arrowLine = new ChatLine { Role = "assistant", Content = arrowSource };
        state.Current.Lines.Add(arrowLine);
        state.SaveSettings();
        var loadedArrow = store.Load().Conversations.Single().Lines.Single(line => line.Id == arrowLine.Id);
        Require(loadedArrow.Content == arrowSource && ChronicleMarkdownRenderer.Render(loadedArrow.Content).Contains("→ 25 silver", StringComparison.Ordinal),
            "reloading retains the model's source text and displays its prose arrow consistently");
    }

    private static async Task CalendarPageChecks(string root, string temporary)
    {
        var gregorian = new CampaignDateTimeSettings { CalendarMode = "Gregorian", GregorianAnchorDate = new DateTime(2024, 1, 31), GregorianAnchorCampaignDay = 1 };
        Require(CampaignCalendar.TryGetMonthPage(gregorian, 1, 1, out var february) && february is { Days: 29, FirstCampaignDay: 2, FirstWeekdayIndex: 3 }
            && february.Title == "February 2024", "Gregorian month page respects the leap-year boundary and weekday alignment");
        Require(CampaignCalendar.TryGetMonthPage(gregorian, 1, 2, out var march) && march is { Days: 31, FirstCampaignDay: 31 }
            && march.Title == "March 2024", "Gregorian month navigation advances by actual month length");
        gregorian.GregorianAnchorDate = new DateTime(2024, 12, 31);
        Require(CampaignCalendar.TryGetMonthPage(gregorian, 1, 1, out var january) && january is { FirstCampaignDay: 2 }
            && january.Title == "January 2025", "Gregorian navigation crosses a year boundary");

        var custom = new CampaignDateTimeSettings
        {
            CalendarMode = "Custom", CustomWeekdays = "A\nB\nC\nD\nE", CustomPeriods = "Dawn, 3\nDusk, 5",
            CustomAnchorWeekday = "D", CustomAnchorDay = 2, CustomAnchorPeriod = "Dusk", CustomAnchorYear = 7,
            CustomAnchorCampaignDay = 10, CustomEra = "Example Era"
        };
        Require(CampaignCalendar.TryGetMonthPage(custom, 10, 0, out var dusk) && dusk is { Days: 5, FirstCampaignDay: 9, FirstWeekdayIndex: 2 }
            && dusk.Weekdays.Count == 5 && dusk.Title == "Dusk · Example Era 7", "custom anchor and non-seven-day week locate the correct month start");
        var previousMonthValid = CampaignCalendar.TryGetMonthPage(custom, 10, -1, out var dawn);
        var nextMonthValid = CampaignCalendar.TryGetMonthPage(custom, 10, 1, out var nextDawn);
        Require(previousMonthValid && dawn is { Days: 3, FirstCampaignDay: 6 }
            && nextMonthValid && nextDawn is { Days: 3, FirstCampaignDay: 14 }
            && nextDawn.Title == "Dawn · Example Era 8", "custom navigation respects different month lengths and year rollover");
        Require(dusk!.ContainsCampaignDay(12) && !nextDawn!.ContainsCampaignDay(12) && nextDawn.ContainsCampaignDay(14),
            "selected day belongs only to its displayed month after navigation");
        custom.CustomWeekdays = string.Join('\n', Enumerable.Range(1, 32).Select(index => $"W{index}"));
        custom.CustomAnchorWeekday = "W4";
        Require(CampaignCalendar.TryGetMonthPage(custom, 10, 0, out var wide) && wide?.Weekdays.Count == 32,
            "long custom weekday lists retain their full count for a horizontally scrollable grid");
        Require(!CampaignCalendar.TryGetMonthPage(new CampaignDateTimeSettings(), 1, 0, out _), "Day Count mode remains list-only");

        gregorian.GregorianAnchorDate = new DateTime(2024, 1, 31);
        Require(CampaignCalendar.GmMoment(gregorian, 8 * 60).StartsWith("Wed · Jan 31, 2024", StringComparison.Ordinal),
            "GM time context includes the authoritative Gregorian weekday and date");
        Require(CampaignCalendar.TryResolveCampaignDay(gregorian, 1, "February", 29, null, out var leapDay, out _)
            && leapDay == 30 && CampaignCalendar.GmMoment(gregorian, (leapDay - 1L) * 1440).StartsWith("Thu · Feb 29, 2024", StringComparison.Ordinal),
            "Gregorian lookup resolves leap-day weekday using the configured date anchor");

        custom.CustomPeriods = "Examplemonth, 13\nFrost, 3";
        custom.CustomAnchorPeriod = "Examplemonth";
        custom.CustomAnchorDay = 10;
        custom.CustomAnchorCampaignDay = 10;
        custom.CustomAnchorYear = 7;
        custom.CustomWeekdays = "A\nB\nC\nD\nE";
        custom.CustomAnchorWeekday = "D";
        custom.CustomEra = "Example Era";
        Require(CampaignCalendar.GmMoment(custom, (10L - 1) * 1440 + 11 * 60 + 30)
            .StartsWith("D, Examplemonth 10, Example Era 7 · Morning (11:30 AM) · Campaign Day 10", StringComparison.Ordinal),
            "GM time context includes the custom calendar day-of-month as well as weekday, month, year, and clock");
        Require(CampaignCalendar.TryResolveCampaignDay(custom, 10, "Examplemonth", 13, null, out var thirteenth, out _)
            && thirteenth == 13 && CampaignCalendar.GmMoment(custom, (thirteenth - 1L) * 1440).StartsWith("B, Examplemonth 13, Example Era 7", StringComparison.Ordinal),
            "custom date lookup uses configured weekday anchors rather than Gregorian weekdays");

        var campaign = new Campaign { Name = "Calendar visibility", DateTimeSettings = custom };
        campaign.Clock = new CampaignClock { TotalMinutes = (10L - 1) * 1440 + 11 * 60 + 30 };
        var data = Path.Combine(temporary, "calendar-pages");
        var store = new ConversationStore(data);
        store.Save(new SavedState("local", "fixture", "", null, [new Conversation { CampaignId = campaign.Id }], [campaign], campaign.Id));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns")));
        campaign = state.Campaigns.Single();
        var tools = state.GetAgendaTools(campaign.Id);
        var ExamplemonthCalendar = new CampaignDateTimeSettings
        {
            CalendarMode = "Custom", CustomWeekdays = "Firstday\nGods\nThirdday\nWyrms\nSaints\nAsh\nGraves",
            CustomPeriods = "Examplemonth, 30\nSecondmonth, 27\nThirdmonth, 31", CustomAnchorWeekday = "Thirdday",
            CustomAnchorDay = 4, CustomAnchorPeriod = "Examplemonth", CustomAnchorYear = 809,
            CustomAnchorCampaignDay = 1, CustomEra = ""
        };
        campaign.DateTimeSettings = ExamplemonthCalendar;
        campaign.Clock!.TotalMinutes = 690;
        Require(state.GetCampaignGmMoment(campaign.Id).StartsWith("Thirdday, Examplemonth 4, Year 809 · Morning (11:30 AM) · Campaign Day 1", StringComparison.Ordinal),
            "initial GM campaign-time context spells out Examplemonth day 4 rather than only Campaign Day 1");
        var ExamplemonthTwentyThird = tools.Read(new JsonObject { ["month"] = "Examplemonth", ["day_of_month"] = 23L });
        Require(ExamplemonthTwentyThird.Contains("Firstday, Examplemonth 23, Year 809", StringComparison.Ordinal)
            && ExamplemonthTwentyThird.Contains("Campaign Day 20", StringComparison.Ordinal)
            && ExamplemonthTwentyThird.Contains("Campaign-day offset: 19", StringComparison.Ordinal),
            "read_agenda includes the requested custom month-day and its independently calculated campaign-day mapping");
        campaign.DateTimeSettings = custom;
        campaign.Clock.TotalMinutes = (10L - 1) * 1440 + 11 * 60 + 30;
        var dateLookup = tools.Read(new JsonObject { ["month"] = "Examplemonth", ["day_of_month"] = 13L });
        Require(dateLookup.Contains("B, Examplemonth 13, Example Era 7", StringComparison.Ordinal)
            && dateLookup.Contains("Campaign-day offset: 3", StringComparison.Ordinal)
            && !dateLookup.Contains("Private arrival", StringComparison.Ordinal),
            "read_agenda date lookup returns deterministic custom date/offset without dumping event entries");
        var currentDateLookup = tools.Read(new JsonObject { ["calendar_day"] = 10L });
        Require(currentDateLookup.Contains("D, Examplemonth 10, Example Era 7", StringComparison.Ordinal)
            && currentDateLookup.Contains("Campaign-day offset: 0 (today)", StringComparison.Ordinal),
            "read_agenda resolves today's weekday from the authoritative campaign day");
        campaign.DateTimeSettings = new CampaignDateTimeSettings { CalendarMode = "DayCount" };
        Require(tools.Read(new JsonObject { ["calendar_day"] = 10L }).Contains("Day 10", StringComparison.Ordinal)
            && tools.Read(new JsonObject { ["month"] = "Examplemonth", ["day_of_month"] = 13L }).Contains("Day Count", StringComparison.Ordinal),
            "Day Count lookup retains numeric-day semantics and rejects invented named months");
        campaign.DateTimeSettings = custom;
        Require(tools.AddEvent(new JsonObject { ["title"] = "Visible meeting", ["day"] = 12L, ["hour"] = 9L, ["minute"] = 0L, ["visibility"] = "player" }).Contains("Agenda event added", StringComparison.Ordinal), "player-visible calendar event can be scheduled");
        Require(tools.AddEvent(new JsonObject { ["title"] = "Private threat", ["day"] = 12L, ["hour"] = 10L, ["minute"] = 0L, ["visibility"] = "gm" }).Contains("Agenda event added", StringComparison.Ordinal), "private event can share a visible calendar day");
        Require(tools.AddEvent(new JsonObject { ["title"] = "Private arrival", ["day"] = 13L, ["hour"] = 10L, ["minute"] = 0L, ["visibility"] = "gm" }).Contains("Agenda event added", StringComparison.Ordinal), "private event can occupy its own calendar day");
        var visibleEvents = state.GetPlayerAgenda(campaign.Id);
        Require(visibleEvents.Count == 1 && visibleEvents[0].Title == "Visible meeting"
            && visibleEvents.Where(item => CampaignAgendaFormat.Day(item.DueMinute) == 12).Count() == 1
            && !visibleEvents.Any(item => CampaignAgendaFormat.Day(item.DueMinute) == 13),
            "calendar dots and selected-day details based on player Agenda cannot reveal GM-only events or counts");
        Require(!visibleEvents.Any(item => nextDawn.ContainsCampaignDay(CampaignAgendaFormat.Day(item.DueMinute))),
            "an empty browsed month has no player-visible event indicators");

        var campaignClock = campaign.Clock;
        campaign.Clock.TotalMinutes = (13L - 1) * 1440 + 23 * 60 + 59;
        Require(tools.Advance(new JsonObject { ["amount"] = 1L, ["unit"] = "minutes" }).Contains("Time advanced", StringComparison.Ordinal)
            && ReferenceEquals(campaignClock, campaign.Clock) && CampaignAgendaFormat.Day(campaign.Clock.TotalMinutes) == 14
            && CampaignAgendaFormat.Hour(campaign.Clock.TotalMinutes) == 0,
            "advance_time crosses midnight by modifying the existing campaign clock");
        Require(tools.Advance(new JsonObject { ["amount"] = 1L, ["unit"] = "hours" }).Contains("Time advanced", StringComparison.Ordinal)
            && CampaignAgendaFormat.Day(campaign.Clock.TotalMinutes) == 14 && CampaignAgendaFormat.Hour(campaign.Clock.TotalMinutes) == 1,
            "advance_time applies hour units to the same authoritative clock");
        Require(tools.Advance(new JsonObject { ["amount"] = 3L, ["unit"] = "days" }).Contains("Time advanced", StringComparison.Ordinal)
            && CampaignAgendaFormat.Day(campaign.Clock.TotalMinutes) == 17
            && CampaignCalendar.GmMoment(custom, campaign.Clock.TotalMinutes).StartsWith("A, Examplemonth 1, Example Era 8", StringComparison.Ordinal),
            "day advancement respects custom month lengths and crosses the configured year boundary");
        state.SaveSettings();
        Require(store.Load().Campaigns!.Single().Clock!.TotalMinutes == campaign.Clock.TotalMinutes,
            "campaign time advances persist in the existing campaign clock");

        var builtTools = (JsonArray)typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
        var names = builtTools.OfType<JsonObject>().Select(tool => (string?)tool["function"]?["name"]).ToArray();
        Require(names.Contains("advance_time") && !names.Contains("advance_campaign_time"),
            "GM reuses the existing advance_time action rather than defining a second clock tool");
        var agendaTool = builtTools.OfType<JsonObject>().Single(tool => (string?)tool["function"]?["name"] == "read_agenda");
        var agendaSchema = (JsonObject)agendaTool["function"]!["parameters"]!;
        Require(agendaSchema["properties"]?["calendar_day"] is JsonObject && agendaSchema["properties"]?["month"] is JsonObject
            && agendaSchema["properties"]?["day_of_month"] is JsonObject,
            "existing read_agenda tool exposes bounded exact-date lookup fields");
        Require(((string?)agendaTool["function"]?["description"])?.Contains("Day Count supports calendar_day only.", StringComparison.Ordinal) == true,
            "Day Count tool guidance uses the implemented calendar_day field");
        var advanceTool = builtTools.OfType<JsonObject>().Single(tool => (string?)tool["function"]?["name"] == "advance_time");
        var setTimeTool = builtTools.OfType<JsonObject>().Single(tool => (string?)tool["function"]?["name"] == "set_campaign_time");
        var playerUpdateTool = builtTools.OfType<JsonObject>().Single(tool => (string?)tool["function"]?["name"] == "propose_player_update");
        Require(((string?)advanceTool["function"]?["description"])?.Contains("When the player asks to advance time", StringComparison.Ordinal) == true
            && ((string?)advanceTool["function"]?["description"])?.Contains("Wait for DBLC's result before claiming the clock changed", StringComparison.Ordinal) == true,
            "advance_time schema clearly maps explicit requests to the existing authoritative clock operation");
        Require(((string?)setTimeTool["function"]?["description"])?.Contains("exact campaign-time setting or correction", StringComparison.Ordinal) == true
            && ((string?)setTimeTool["function"]?["description"])?.Contains("For elapsed time, use advance_time", StringComparison.Ordinal) == true,
            "set_campaign_time schema distinguishes exact corrections from elapsed-time advances");
        Require(((string?)playerUpdateTool["function"]?["description"])?.Contains("When the player asks to change their Player Sheet, call this tool", StringComparison.Ordinal) == true
            && ((string?)playerUpdateTool["function"]?["description"])?.Contains("fresh player approval before any write", StringComparison.Ordinal) == true,
            "Player Sheet tool description ties explicit player requests to the existing approval workflow");

        using (var normalServer = new ModelStub((_, _) => Text("The scene remains quiet.")))
        {
            state.Endpoint = normalServer.Endpoint;
            var before = campaign.Clock.TotalMinutes;
            await state.SendAsync("We wait quietly.", new LlamaClient());
            Require(campaign.Clock.TotalMinutes == before,
                "ordinary GM turns do not automatically advance authoritative campaign time");
            var messages = (JsonArray)normalServer.Requests[0]["messages"]!;
            var systemText = (string?)messages[0]?["content"] ?? "";
            var finalUserText = (string?)messages[^1]?["content"] ?? "";
            Require(systemText.Contains("Use advance_time when the player requests a time advance or fiction clearly advances meaningful time", StringComparison.Ordinal)
                && !systemText.Contains("A, Examplemonth 1, Example Era 8", StringComparison.Ordinal)
                && finalUserText.Contains("We wait quietly.", StringComparison.Ordinal)
                && finalUserText.Contains("CURRENT FICTIONAL CAMPAIGN TIME:\n", StringComparison.Ordinal)
                && finalUserText.Contains("A, Examplemonth 1, Example Era 8", StringComparison.Ordinal),
                "normal GM request keeps stable Agenda guidance early and supplies the authoritative clock with the latest turn");
        }
        var shell = File.ReadAllText(Path.Combine(root, "src", "DBLC", "AppShell.razor"));
        Require(shell.Contains("var playerEventsByDay = playerEvents.GroupBy", StringComparison.Ordinal)
            && shell.Contains("playerEventsByDay.ContainsKey(day)", StringComparison.Ordinal)
            && shell.Contains("playerEventsByDay.TryGetValue(selectedDay", StringComparison.Ordinal),
            "calendar dots and day details use only the visibility-filtered player event list");
    }

    private static async Task DiceAndContinuationChecks(string temporary)
    {
        var data = Path.Combine(temporary, "dice-state");
        var campaign = new Campaign { Name = "Dice fixture" };
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1", IsWorkbench = true };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [conversation], [campaign], campaign.Id));
        var archive = new ChronicleArchiveStore(Path.Combine(data, "archive.db"));
        var legacyPath = Path.Combine(data, "legacy-archive.db");
        using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyPath}"))
        {
            legacy.Open();
            using var command = legacy.CreateCommand();
            command.CommandText = "CREATE TABLE archived_lines(conversation_id TEXT NOT NULL,sequence INTEGER NOT NULL,role TEXT NOT NULL,created_at TEXT NOT NULL,content TEXT NOT NULL,PRIMARY KEY(conversation_id,sequence));";
            command.ExecuteNonQuery();
        }
        var legacyArchive = new ChronicleArchiveStore(legacyPath);
        var legacyRoll = new ChatLine { Sequence = 1, Role = "user", RollSides = 6, Content = "3" };
        legacyArchive.Archive(conversation.Id, [legacyRoll]);
        Require(legacyArchive.GetArchivedLines(conversation.Id).Single().Id == legacyRoll.Id, "older archive schemas load and preserve new roll identities after additive column upgrade");
        var state = new AppState(store, new LibraryService(), archive,
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns")));
        var active = state.Current!;
        foreach (var sides in new[] { 4, 6, 8, 10, 12, 20 })
        {
            var id = Guid.NewGuid();
            Require(state.RecordDiceRoll(sides, sides, id, active.Id), $"manual d{sides} roll is recorded");
            Require(!state.RecordDiceRoll(sides, 1, id, active.Id), "duplicate event ID cannot append a second roll");
            Require(active.Lines.Last().RollDisplayText == $"d{sides} rolled: {sides}", "notation puts die type before result");
        }
        Require(!state.Busy && active.Lines.Count == 6 && active.Lines.All(line => line.Role == "user" && line.EntryType == ChatLine.PlayerRollEntryType), "rolling records ordered player events without starting inference");
        Require(active.Lines.Select(line => line.Sequence).SequenceEqual(Enumerable.Range(1, 6).Select(i => (long)i)), "rolls retain individual Chronicle order");
        Require(!state.RecordDiceRoll(20, 21) && !state.RecordDiceRoll(3, 1) && !state.RecordDiceRoll(4, 1, conversationId: Guid.NewGuid()), "invalid results, unsupported dice and stale Sessions are rejected");
        var snapshot = MobileChronicleSnapshot.Capture(active);
        Require(snapshot.Lines.Select(line => line.Id).SequenceEqual(active.Lines.Select(line => line.Id)), "remote snapshots retain each roll identity and notation");
        Require(store.Load().Conversations.Single().Lines.Select(line => line.Id).SequenceEqual(active.Lines.Select(line => line.Id)), "rolls survive save/reload with their event IDs");
        using (var server = new ModelStub((_, _) => Text("The recorded roll advances the scene.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("", new LlamaClient());
            var messages = server.Requests[0]["messages"]!.AsArray();
            Require(messages.OfType<JsonObject>().Count(message => ((string?)message["content"])?.Contains("Recorded roll, not a request.", StringComparison.Ordinal) == true) == 6, "empty Send includes each previous roll exactly once");
            Require(((string?)messages.Last()!["content"])!.Contains("Do not invent player actions or dialogue.", StringComparison.Ordinal), "empty Send uses a minimal request-local continuation instruction");
            Require(active.Lines.Count == 7 && active.Lines.Count(line => line.Role == "user") == 6 && active.Lines.All(line => !string.IsNullOrWhiteSpace(line.Content)), "empty Send adds a GM reply without a blank or internal player message");
            Require(server.Paths.Count(path => path.EndsWith("/chat/completions", StringComparison.Ordinal)) == 1, "empty Send uses only the normal GM inference");
        }
        var archivedRolls = active.Lines.Where(line => line.RollSides is not null).ToArray();
        archive.Archive(active.Id, archivedRolls);
        archive.Archive(active.Id, archivedRolls);
        Require(archive.GetArchivedLines(active.Id).Count == 6 && archive.GetArchivedLines(active.Id).Select(line => line.Id).SequenceEqual(archivedRolls.Select(line => line.Id)), "archiving rolls is idempotent and retains event IDs");
        Require(archive.Read(active.Id, [1]).Contains("d4 roll", StringComparison.Ordinal), "archive retrieval identifies recorded dice events");
        active.Lines.RemoveAll(line => line.RollSides is not null);
        Require(!state.RecordDiceRoll(4, 1, archivedRolls[0].Id, active.Id), "a repeated archived roll ID cannot create a new event");
        using (var server = new ModelStub((_, _) => Text("Continue without inventing player actions.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("   ", new LlamaClient());
            Require(active.Lines.Count(line => line.Role == "user") == 0 && active.Lines.Count == 2, "empty Send also works without recent rolls");
        }
        state.RecordDiceRoll(6, 3);
        using (var server = new ModelStub((_, _) => Text("Text follows the player's roll.")))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("I inspect the result.", new LlamaClient());
            var messages = server.Requests[0]["messages"]!.AsArray();
            Require(messages.OfType<JsonObject>().Count(message => ((string?)message["content"])?.Contains("Player rolled d6: 3", StringComparison.Ordinal) == true) == 1, "text Send includes a preceding roll once");
            Require(((string?)messages.Last()!["content"])?.StartsWith("I inspect the result.\n\n[DBLC CURRENT CONTEXT", StringComparison.Ordinal) == true, "text remains the final player request after recorded rolls");
        }
        Require(new ChatLine { Role = "assistant", RollSides = 6, Content = "4" }.ContextContent.Contains("GM rolled d6: 4", StringComparison.Ordinal), "GM-origin rolls are not attributed to the player");
        Require(new ChatLine { Role = "tool", RollSides = 6, Content = "4" }.ContextContent.Contains("Automated system rolled", StringComparison.Ordinal), "automated rolls preserve their origin");
        using (var server = new ModelStub((_, _) => Text("One reply only.")))
        {
            state.Endpoint = server.Endpoint;
            var first = state.SendAsync("", new LlamaClient());
            Require(!state.RecordDiceRoll(4, 1), "manual rolls cannot alter an active GM generation");
            await state.SendAsync("", new LlamaClient());
            await first;
            Require(server.Paths.Count(path => path.EndsWith("/chat/completions", StringComparison.Ordinal)) == 1, "single-turn gate rejects simultaneous empty sends");
        }
    }

    private static async Task PresentationStateChecks(string temporary)
    {
        var legacySpeakerSegment = JsonSerializer.Deserialize<GmPresentationSegment>("""{"Speaker":"Example NPC","Text":"Legacy line."}""")!;
        Require(legacySpeakerSegment.Id != Guid.Empty, "older saved speaker segments receive a safe identity on load");
        var data = Path.Combine(temporary, "speaker-state");
        var campaign = new Campaign { Name = "Speaker fixture" };
        var npc = new NpcCard { Name = "Example NPC", Notes = "Private notes stay out of automatic speaker context." };
        campaign.Npcs.Add(npc);
        var conversation = new Conversation { CampaignId = campaign.Id, SessionNumber = 1, Title = "Session 1" };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("http://127.0.0.1:1/v1", "fixture", "", null, [conversation], [campaign], campaign.Id, CharacterSpeakersEnabled: true));
        var playerImages = Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns"));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")), playerImages);
        state.Endpoint = "http://127.0.0.1:1/v1";
        state.GmStreamAnimationsEnabled = true;
        state.GmInlineStreamEffectsEnabled = true;
        state.CharacterSpeakersEnabled = true;

        Guid[] presentationSegmentIds = [];
        var structured = JsonSerializer.Serialize(new { segments = new[]
        {
            new { speaker = "GM", text = "The door opens." },
            new { speaker = "Example NPC", text = "\n[[smoke]]Ah, you look at me." },
            new { speaker = "THE GM", text = "\n[[focus]]She turns away." }
        } });
        using (var server = new ModelStub((_, _) => Calls(("present_response", structured))))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("The room falls quiet.", new LlamaClient());
            var response = state.Current!.Lines.Last(line => line.Role == "assistant");
            var segments = response.PresentationSegments!;
            presentationSegmentIds = segments.Select(segment => segment.Id).ToArray();
            Require(server.Paths.Count(path => path.EndsWith("/chat/completions", StringComparison.Ordinal)) == 1 && response.Role == "assistant" && state.Current.Lines.Count(line => line.Role == "assistant") == 1, "multi-speaker presentation remains one assistant turn and one model pass");
            Require(segments.Select(segment => segment.Speaker).SequenceEqual(["GM", "Example NPC", "GM"]), "GM aliases collapse and speaker switching groups consecutive narration");
            Require(segments[1].SpeakerNpcId == npc.Id && segments[1].NarrationEffectSpans!.Any(span => span.Effect == "smoke"), "NPC link and inline effect resolve into the same persisted segment");
            Require(segments[2].Speaker == "GM" && segments[2].NarrationEffectSpans!.Any(span => span.Effect == "focus"), "returning to narration resets to the configured default effect");
            Require(segments.All(segment => !string.IsNullOrWhiteSpace(segment.Text)) && response.Content.Contains("Example NPC: Ah, you look at me.", StringComparison.Ordinal), "presentation contains no empty speaker rows and keeps prose in one logical response");
            Require(segments.Select(segment => segment.Id).Distinct().Count() == segments.Count, "each live presentation segment receives a stable opaque identity");
            Require(!response.Content.Contains("[[smoke]]", StringComparison.Ordinal) && !response.Content.Contains("[[speaker:", StringComparison.Ordinal), "speaker and effect commands are not persisted as visible prose");
        }

        var image = playerImages.Save(campaign.Id, "portrait.png", [1, 2, 3, 4]);
        Require(state.SetPlayerChroniclePortrait(campaign.Id, image.MarkdownPath), "a Player-sheet image can be selected as Chronicle portrait");
        state.SetPlayerChronicleName(campaign.Id, "Baba");
        var selectedPath = state.GetPlayerChronicleIdentity(campaign.Id).PortraitMarkdownPath;
        state.SaveSettings();
        var reloaded = store.Load();
        var reloadedReply = reloaded.Conversations!.Single().Lines!.Last(line => line.Role == "assistant");
        var reloadedCampaign = reloaded.Campaigns!.Single();
        var reloadedSegments = reloadedReply.PresentationSegments!;
        Require(reloadedCampaign.PlayerIdentity.DisplayName == "Baba" && reloadedCampaign.PlayerIdentity.PortraitMarkdownPath == selectedPath,
            "Player Chronicle identity survives campaign reload");
        Require(reloadedSegments.Select(segment => segment.Speaker).SequenceEqual(["GM", "Example NPC", "GM"])
            && reloadedSegments[1].SpeakerNpcId == npc.Id
            && reloadedSegments.Select(segment => segment.Id).SequenceEqual(presentationSegmentIds),
            "speaker boundaries, resolved NPC identity and historical display name survive session reload");
        const string oldSheet = "![first](assets/portrait.png)\n\n![second](assets/other.png)";
        const string reorderedSheet = "![second](assets/other.png)\n\n![first](assets/portrait.png)";
        playerImages.DeleteUnreferenced(campaign.Id, oldSheet, reorderedSheet);
        Require(state.GetPlayerChronicleIdentity(campaign.Id).DisplayName == "Baba" && selectedPath == image.MarkdownPath, "identity settings stay separate from Markdown image order");
        playerImages.DeleteUnreferenced(campaign.Id, reorderedSheet, "![second](assets/other.png)");
        Require(state.GetPlayerChronicleIdentity(campaign.Id).PortraitMarkdownPath == image.MarkdownPath && playerImages.Find(campaign.Id, image.MarkdownPath) is null, "deleted selected portrait safely falls back while keeping the stale reference non-fatal");

        state.CharacterSpeakersEnabled = false;
        state.SaveSettings();
        using (var server = new ModelStub((_, _) => Calls(("present_response", structured))))
        {
            state.Endpoint = server.Endpoint;
            await state.SendAsync("Continue the scene.", new LlamaClient());
            var response = state.Current!.Lines.Last(line => line.Role == "assistant");
            Require(response.PresentationSegments!.All(segment => segment.Speaker == "GM") && response.Content.Contains("Ah, you look at me.", StringComparison.Ordinal), "disabled Character Speakers hide speaker markers and preserve all prose under GM");
            var prompt = (string)server.Requests[0]["messages"]![0]!["content"]!;
            var presentationTool = server.Requests[0]["tools"]!.AsArray().Single(tool => (string?)tool?["function"]?["name"] == "present_response");
            Require(!prompt.Contains("CHARACTER SPEAKERS:", StringComparison.Ordinal)
                && prompt.Contains(state.GmProfile, StringComparison.Ordinal)
                && ((string)presentationTool!["function"]!["description"]!).Contains("Set every segment speaker to GM", StringComparison.Ordinal),
                "disabled speakers omit conditional guidance, preserve the profile, and require GM-only tool presentation");
        }
    }

    private static async Task RetrievalStoppingChecks(string temporary)
    {
        var data = Path.Combine(temporary, "retrieval-stopping");
        Directory.CreateDirectory(data);
        var campaign = new Campaign { Name = "Retrieval stopping fixture" };
        var session = new Conversation { CampaignId = campaign.Id, Title = "Session", SessionNumber = 0 };
        var store = new ConversationStore(Path.Combine(data, "state"));
        store.Save(new SavedState("local", "fixture", "", null, [session], [campaign], campaign.Id));
        var state = new AppState(store, new LibraryService(), new ChronicleArchiveStore(Path.Combine(data, "archive.db")),
            Isolate(new ChronicleImageAssetStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new CampaignAgendaAuditStore(), "_root", Path.Combine(data, "campaigns")),
            Isolate(new PlayerImageAssetStore(), "_root", Path.Combine(data, "campaigns")));
        state.Select(state.Conversations.Single());
        using var server = new ModelStub((_, pass) => pass < 8 ? Calls(("list_books", "{}")) : Text("This should not be reached."));
        state.Endpoint = server.Endpoint;
        await state.SendAsync("Look up a rule.", new LlamaClient());
        var error = state.Current!.Lines.Last(line => line.Role == "assistant");
        Require(server.Requests.Count == 8 && error.Content.Contains("No additional tool was executed", StringComparison.Ordinal), "a tool request on the reserved final answer pass is rejected without execution");
        Require(server.Requests[7]["tools"] is null && (string?)server.Requests[7]["tool_choice"] == "none", "the eighth existing pass requests an answer without tools");
        Require(!error.Content.Contains("Could not reach the model endpoint", StringComparison.Ordinal), "tool-round exhaustion is not reported as a network failure");
        var system = (string)server.Requests[0]["messages"]![0]!["content"]!;
        Require(system.Contains("Use context already available. Retrieve only information needed", StringComparison.Ordinal)
            && system.Contains("avoid repeating unchanged lookups", StringComparison.Ordinal)
            && system.Contains("Cite only eligible retrieved sources that materially informed the response", StringComparison.Ordinal)
            && system.Contains("acknowledge uncertainty or corrections", StringComparison.Ordinal)
            && system.Contains("Use tools when necessary to maintain accurate campaign records", StringComparison.Ordinal),
            "normal GM instructions retain economical retrieval, source integrity, corrections, and necessary record updates");
    }
    private static Task RunModel(ModelStub server, StringBuilder output, Func<string>? readPlayer = null,
        Func<string, Task<string>>? propose = null, ResponseSteeringSession? steering = null, Action<string>? onToken = null,
        LlamaVisionImage? image = null, Action<GmPresentation>? presentation = null, bool allowCharacterSpeakers = false,
        Func<WorkbenchExitRetention, Task<string>>? onWorkbenchEnd = null, bool isWorkbench = false, IReadOnlyList<ChatLine>? history = null,
        Func<string?, int, int, string>? pages = null, Func<string>? listSkills = null, Func<string, string>? readSkill = null, Func<string>? listCampaignMaps = null, Func<string, bool, CampaignMapOpenResult>? openCampaignMap = null, bool mapVisionEnabled = false) => new LlamaClient().CompleteAsync(
            server.Endpoint, "fixture", history ?? [new ChatLine { Role = "user", Content = "Synthetic request" }], "", "",
            () => "books", _ => "title", _ => "catalogue", pages ?? ((_, _, _) => "pages"), _ => "sections", (_, _) => "section",
            () => "NPC names", _ => "NPC", (_, _, _, _) => "saved", readPlayer ?? (() => "Player"), () => "Workspace",
            _ => "archive search", _ => "archive lines", onToken ?? (token => output.Append(token)), _ => { }, (_, _, _) => { },
            onPresentation: presentation, onPlayerSheetUpdate: propose, steering: steering, visionImage: image, allowCharacterSpeakers: allowCharacterSpeakers,
            onWorkbenchEnd: onWorkbenchEnd, isWorkbench: isWorkbench, listSkills: listSkills, readSkill: readSkill, listCampaignMaps: listCampaignMaps, openCampaignMap: openCampaignMap, mapVisionEnabled: mapVisionEnabled);

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { _checks++; return; } throw new Exception("Expected rejection"); }
    private static string Delta(object value) => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = value } } }) + "\n\n";
    private static string Text(string text) => Delta(new { content = text }) + "data: [DONE]\n\n";
    private static string Calls((string Name, string Args) call, bool done = true) => Calls([call], done);
    private static string Calls((string Name, string Args) first, (string Name, string Args) second) => Calls([first, second], true);
    private static string Calls((string Name, string Args)[] calls, bool done) => Delta(new
    {
        tool_calls = calls.Select((c, i) => new { index = i, id = "call_" + i, function = new { name = c.Name, arguments = c.Args } }).ToArray()
    }) + (done ? "data: [DONE]\n\n" : "");

    private sealed class ModelStub : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly Task _server;
        public List<JsonObject> Requests { get; } = [];
        public List<string> Paths { get; } = [];
        public string Endpoint { get; }
        public ModelStub(Func<JsonObject, int, string> response)
        {
            _listener.Start(); Endpoint = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/v1";
            _server = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        using var stream = client.GetStream();
                        var header = new StringBuilder(); var one = new byte[1];
                        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                        { await stream.ReadExactlyAsync(one, _stop.Token); header.Append((char)one[0]); }
                        var headerLines = header.ToString().Split("\r\n");
                        var requestLine = headerLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var requestPath = requestLine.ElementAtOrDefault(1) ?? "";
                        Paths.Add(requestPath);
                        if (requestLine.ElementAtOrDefault(0) == "GET" && requestPath.EndsWith("/props", StringComparison.Ordinal))
                        {
                            var props = Encoding.UTF8.GetBytes("{\"modalities\":{\"vision\":true}}");
                            var propsHeaders = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {props.Length}\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(propsHeaders, _stop.Token); await stream.WriteAsync(props, _stop.Token);
                            continue;
                        }
                        var contentLength = headerLines.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                        byte[] body;
                        if (contentLength is not null)
                        {
                            var length = int.Parse(contentLength.Split(':')[1]);
                            body = new byte[length]; await stream.ReadExactlyAsync(body, _stop.Token);
                        }
                        else
                        {
                            using var chunked = new MemoryStream();
                            while (true)
                            {
                                var sizeLine = await ReadAsciiLineAsync(stream, _stop.Token);
                                var size = Convert.ToInt32(sizeLine.Split(';')[0], 16);
                                if (size == 0)
                                {
                                    while ((await ReadAsciiLineAsync(stream, _stop.Token)).Length > 0) { }
                                    break;
                                }
                                var chunk = new byte[size]; await stream.ReadExactlyAsync(chunk, _stop.Token); chunked.Write(chunk);
                                await stream.ReadExactlyAsync(new byte[2], _stop.Token);
                            }
                            body = chunked.ToArray();
                        }
                        var request = JsonNode.Parse(body)!.AsObject(); var pass = Requests.Count; Requests.Add(request);
                        var data = Encoding.UTF8.GetBytes(response(request, pass));
                        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n");
                        try { await stream.WriteAsync(headers, _stop.Token); await stream.WriteAsync(data, _stop.Token); }
                        catch (IOException) { /* A Stop test may cancel the HTTP request while the stub sends its reply. */ }
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
            });
        }
        private static async Task<string> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                await stream.ReadExactlyAsync(one, cancellationToken);
                if (one[0] == '\n') break;
                if (one[0] != '\r') line.Add(one[0]);
            }
            return Encoding.ASCII.GetString(line.ToArray());
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _server.GetAwaiter().GetResult(); _stop.Dispose(); }
    }
}

internal sealed class ChronicleProbe : ChronicleText
{
    private static readonly FieldInfo HtmlField = typeof(ChronicleText).GetField("_html", BindingFlags.NonPublic | BindingFlags.Instance)!;
    public string Html => (string)HtmlField.GetValue(this)!;
    public bool NeedsRender => ShouldRender();
    public async Task CompleteRender() { await OnAfterRenderAsync(false); if (NeedsRender) await OnAfterRenderAsync(false); }
    public void Update(string text, string? flavor = null, int? seed = null, bool enabled = true)
    { Text = text; Flavor = flavor; Seed = seed; FlavorsEnabled = enabled; StableKey = "fixture"; OnParametersSet(); }
}
