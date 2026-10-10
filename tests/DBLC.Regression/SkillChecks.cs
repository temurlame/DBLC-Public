using DBLC;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

internal static partial class Checks
{
    internal static async Task RunSkillChecks(string root, string temporary)
    {
        var defaults = Path.Combine(root, "src", "DBLC", "default-skills");
        var skills = Path.Combine(temporary, "skills");
        var initialize = typeof(SkillLibrary).GetMethod("InitializeDefaults", BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string), typeof(string)])!;
        var list = typeof(SkillLibrary).GetMethod("List", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)])!;
        var read = typeof(SkillLibrary).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string), typeof(string)])!;
        string List() => (string)list.Invoke(null, [skills])!;
        string Read(string name) => (string)read.Invoke(null, [skills, name])!;

        initialize.Invoke(null, [skills, defaults]);
        var discovered = List();
        foreach (var name in new[] { "rules-research", "rules-adjudication", "combat", "advancement", "campaign-memory", "npc-portrayal" })
            Require(discovered.Contains("- " + name + ":", StringComparison.Ordinal), $"default skill discovered: {name}");
        Require(!discovered.Contains("Use this skill when", StringComparison.Ordinal), "discovery lists metadata without loading skill instructions");
        var research = Read("rules-research");
        Require(research.Contains("list_books", StringComparison.Ordinal) && research.Contains("read_json_sections", StringComparison.Ordinal),
            "on-demand load returns the complete rules-research instructions");
        var researchPath = Path.Combine(skills, "rules-research", "SKILL.md");
        File.AppendAllText(researchPath, "\nEdited while DBLC is open.\n");
        Require(Read("rules-research").Contains("Edited while DBLC is open.", StringComparison.Ordinal), "skill edits are read from disk immediately");
        Directory.Delete(Path.Combine(skills, "combat"), true);
        Require(!List().Contains("- combat:", StringComparison.Ordinal) && Read("combat").Contains("No unique skill", StringComparison.Ordinal),
            "removed skills disappear from discovery and cannot be loaded");
        var custom = Path.Combine(skills, "table-procedure"); Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(custom, "SKILL.md"), "\uFEFF---\nname: table-procedure\ndescription: \"A quoted: short description\"\nlicense: MIT\n---\nFollow the existing DBLC procedure.\n");
        Require(List().Contains("A quoted: short description", StringComparison.Ordinal)
            && Read("table-procedure").Contains("Follow the existing DBLC procedure.", StringComparison.Ordinal)
            && Read("../rules-research").Contains("exact skill name", StringComparison.Ordinal),
            "ordinary YAML frontmatter, UTF-8 BOM files, and safe exact-name loading work");
        var invalid = Path.Combine(skills, "broken"); Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "SKILL.md"), "---\nname: broken\ndescription: Broken test skill\n---\n" + new string('x', SkillLibrary.MaxInstructionCharacters + 1));
        Require(List().Contains("broken:", StringComparison.Ordinal) && Read("broken").Contains("no text was truncated", StringComparison.Ordinal),
            "oversized skill is discoverable by metadata but rejected intact at load rather than truncated");

        var schema = (JsonArray)typeof(LlamaClient).GetMethod("BuildTools", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
        var names = schema.OfType<JsonObject>().Select(tool => (string?)tool["function"]?["name"]).ToHashSet(StringComparer.Ordinal);
        var previousToolNames = "present_response list_books get_catalogue read_pages get_json_catalogue read_json_sections list_npcs read_npc edit_npc list_bestiary read_bestiary create_bestiary propose_bestiary_update update_bestiary_slain read_player propose_player_update read_workspace read_gm_notes search_gm_notes read_gm_note propose_gm_note read_threads add_thread update_thread resolve_thread search_chronicle read_chronicle read_previous_session_ending read_agenda advance_time set_campaign_time add_agenda_event edit_agenda_event resolve_agenda_event remove_agenda_event list_campaign_maps open_campaign_map request_workbench request_end_workbench".Split(' ');
        Require(previousToolNames.Length == 39 && previousToolNames.All(names.Contains) && names.Count == 41
            && names.Contains("list_skills") && names.Contains("read_skill"), "both skill tools are added while all remaining tools stay directly available; sketch-only tools are absent; expected-count=" + previousToolNames.Length + "; count=" + names.Count + "; skills=" + names.Contains("list_skills") + "/" + names.Contains("read_skill") + "; missing=" + string.Join(',', previousToolNames.Where(name => !names.Contains(name))));

        using (var server = new ModelStub((_, pass) => pass switch
        {
            0 => Calls(("list_skills", "{}")),
            1 => Calls(("read_skill", "{\"name\":\"rules-research\"}")),
            _ => Text("The library has the rulebook."),
        }))
        {
            await RunModel(server, new StringBuilder(), listSkills: () => List(), readSkill: Read);
            Require(server.Requests.Count == 3, "skill discovery and load use the ordinary tool continuation");
            var firstMessages = server.Requests[0]["messages"]!.ToJsonString();
            var secondMessages = server.Requests[1]["messages"]!.ToJsonString();
            var thirdMessages = server.Requests[2]["messages"]!.ToJsonString();
            Require(!firstMessages.Contains("Find and report rules from DBLC's imported", StringComparison.Ordinal)
                && !secondMessages.Contains("Use this skill when the player asks", StringComparison.Ordinal)
                && thirdMessages.Contains("Use this skill when the player asks", StringComparison.Ordinal),
                "skill body enters model context only after read_skill returns it");
            Require(server.Requests.All(request => request["tools"]!.AsArray().Any(tool => (string?)tool?["function"]?["name"] == "read_pages")),
                "existing source-retrieval tools remain directly available after skill loading");
        }
        using (var server = new ModelStub((_, pass) => pass == 0 ? Calls(("read_pages", "{\"book_id\":\"rules\",\"from_page\":1,\"to_page\":1}")) : Text("The page says so.")))
        {
            var pageReads = 0;
            await RunModel(server, new StringBuilder(), pages: (_, _, _) => { pageReads++; return "Retrieved page text."; });
            Require(pageReads == 1 && server.Requests.Count == 2 && server.Requests[0]["tools"]!.AsArray().Count == 41,
                "an existing direct gameplay tool still executes normally with skills enabled");
        }
        Console.WriteLine($"Agent Skills: {_checks} focused checks passed.");
    }
}
