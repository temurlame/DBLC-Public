using DBLC;
using System.Reflection;
using System.Text.Json.Nodes;

internal static partial class Checks
{
    internal static async Task RunTargetedEditChecks(string temporary)
    {
        var apply = typeof(LlamaClient).Assembly.GetType("DBLC.ExactTextEdit")!.GetMethod("TryApply", BindingFlags.Public | BindingFlags.Static)!;
        (bool Ok, string Updated, string Error) Edit(string original, string expected, string replacement)
        {
            object?[] args = [original, expected, replacement, null, null];
            var ok = (bool)apply.Invoke(null, args)!;
            return (ok, (string)args[3]!, (string)args[4]!);
        }
        var sheet = "# Player\n![portrait](assets/portrait.png)\nHP: 4\nOther: **untouched**\n";
        var changed = Edit(sheet, "HP: 4", "HP: 5");
        Require(changed.Ok && changed.Updated == sheet.Replace("HP: 4", "HP: 5", StringComparison.Ordinal), "targeted sheet edit preserves Markdown and image reference");
        Require(!Edit(sheet, "HP: 3", "HP: 5").Ok && !Edit("aaaa", "aaa", "x").Ok && !Edit(sheet, "", "x").Ok,
            "stale, overlapping and empty expected passages cannot edit");

        var (state, _) = AuditState(temporary, "targeted-edits", workbench: false);
        var campaignId = state.Current!.CampaignId;
        state.SaveNpc(campaignId, "ExampleNpc", "## Voice\nQuiet.\n![portrait](assets/example-npc.png)\nOther: intact\n");
        var originalNpc = state.FindNpc(campaignId, "ExampleNpc")!.Notes;
        Require(state.EditNpcText(campaignId, "ExampleNpc", "Quiet.", "Firm.").Contains("saved", StringComparison.OrdinalIgnoreCase)
            && state.FindNpc(campaignId, "ExampleNpc")!.Notes == originalNpc.Replace("Quiet.", "Firm.", StringComparison.Ordinal),
            "NPC targeted edit keeps all unrelated text and formatting");
        var npcBefore = state.FindNpc(campaignId, "ExampleNpc")!.Notes;
        Require(!state.EditNpcText(campaignId, "ExampleNpc", "Quiet.", "Loud.").Contains("saved", StringComparison.OrdinalIgnoreCase)
            && state.FindNpc(campaignId, "ExampleNpc")!.Notes == npcBefore
            && state.FindNpc(Guid.NewGuid(), "ExampleNpc") is null,
            "stale NPC edit is rejected and lookup stays campaign scoped");
        Require(!state.EditNpcText(campaignId, "ExampleNpc", "o", "x").Contains("saved", StringComparison.OrdinalIgnoreCase)
            && state.FindNpc(campaignId, "ExampleNpc")!.Notes == npcBefore,
            "ambiguous NPC passage cannot alter the card");

        var tools = state.GetBestiaryTools(campaignId);
        var create = tools.Create("Wight", "HP 4\nArmor 2", "Other: intact");
        state.ApproveBestiaryProposal(); await create;
        var proposal = tools.ProposeTextEdit!("Wight", "stat_block", "HP 4", "HP 5");
        Require(!proposal.IsCompleted && state.FindBestiaryEntry(campaignId, "Wight")!.StatBlock == "HP 4\nArmor 2"
            && state.PendingBestiaryProposal!.Updated.StatBlock == "HP 5\nArmor 2", "Bestiary targeted edit awaits existing approval with intact unrelated field");
        state.CancelBestiaryProposal();
        Require((await proposal).Contains("rejected", StringComparison.OrdinalIgnoreCase)
            && state.FindBestiaryEntry(campaignId, "Wight")!.StatBlock == "HP 4\nArmor 2", "rejected Bestiary edit changes nothing");
        Require((await tools.ProposeTextEdit!("Wight", "stat_block", "missing", "HP 5")).Contains("no longer present", StringComparison.OrdinalIgnoreCase)
            && state.PendingBestiaryProposal is null, "stale Bestiary edit cannot open approval");
        proposal = tools.ProposeTextEdit!("Wight", "notes", "intact", "preserved");
        state.ApproveBestiaryProposal(); await proposal;
        Require(state.FindBestiaryEntry(campaignId, "Wight")!.Notes == "Other: preserved"
            && state.FindBestiaryEntry(campaignId, "Wight")!.StatBlock == "HP 4\nArmor 2",
            "approved Bestiary targeted edit changes only the chosen passage");

        var playerProposalSeen = false;
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("propose_player_update", "{\"expected_text\":\"HP: 4\",\"replacement_text\":\"HP: 5\"}"))
            : Text("The player declined the proposed edit.")))
        {
            await new LlamaClient().CompleteAsync(server.Endpoint, "fixture", [new ChatLine { Role = "user", Content = "Change HP." }], "", "",
                () => "books", _ => "title", _ => "catalogue", (_, _, _) => "pages", _ => "sections", (_, _) => "section",
                () => "NPCs", _ => "NPC", (_, _, _, _) => "saved", () => sheet, () => "Workspace", _ => "search", _ => "read",
                _ => { }, _ => { }, (_, _, _) => { },
                onPlayerSheetTextEdit: (expected, replacement) =>
                {
                    var proposed = Edit(sheet, expected, replacement);
                    var review = state.RequestPlayerSheetUpdate(campaignId, sheet, proposed.Updated);
                    playerProposalSeen = proposed.Ok && state.PendingPlayerUpdate?.OriginalMarkdown == sheet
                        && state.PendingPlayerUpdate?.UpdatedMarkdown == changed.Updated;
                    state.ResolvePlayerSheetUpdate("The player rejected the proposed update. No change was made.");
                    return review;
                });
            Require(playerProposalSeen && server.Requests.Count == 2 && state.PendingPlayerUpdate is null,
                "targeted Player tool uses existing full-document approval and rejection flow");
        }
        using (var server = new ModelStub((_, pass) => pass == 0
            ? Calls(("propose_player_update", "{\"expected_text\":\"HP: 4\",\"replacement_text\":\"HP: 5\"}"))
            : Text("The sheet remains unchanged.")))
        {
            var appProposalSeen = false;
            state.Endpoint = server.Endpoint;
            state.Changed += () =>
            {
                if (state.PendingPlayerUpdate is not { } pending) return;
                appProposalSeen = pending.OriginalMarkdown == sheet && pending.UpdatedMarkdown == changed.Updated;
                state.ResolvePlayerSheetUpdate("The player rejected the proposed update. No change was made.");
            };
            await state.SendAsync("Change HP.", new LlamaClient(), readPlayer: _ => sheet);
            Require(appProposalSeen && server.Requests.Count >= 2 && state.PendingPlayerUpdate is null,
                "AppState wires targeted Player edits into the existing review without writing the sheet");
        }

        var schema = (JsonArray)typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
        Require(schema.Count == 50, "targeted editing retains the 48 established tools alongside two optional skill tools");
        Console.WriteLine("Targeted editing: focused checks passed.");
    }
}
