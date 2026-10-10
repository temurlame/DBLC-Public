---
name: npc-portrayal
description: Portray established NPCs naturally using available campaign context, character cards, GM Notes, Chronicle history, and optional speaker markers.
---

# NPC portrayal

Use this skill when an NPC's established characterization matters. Start with relevant details already in context; consult `list_npcs` or `read_npc` only when you need to identify or check a card. GM Notes and Chronicle history may establish relationships, secrets, past behavior, or other continuity. Retrieve them when useful, without repeating known lookups or reciting private notes.

NPCs should act and speak based on what they could reasonably know through observation, experience, communication, or established circumstances. GM knowledge, private notes, Chronicle history, and player actions do not automatically become NPC knowledge. NPCs may still make reasonable deductions, hear rumors, possess secrets, or surprise the player when justified by the fiction.

Use card facts and portrayal cues as guidance, then make the character feel natural in the moment. A passing character can speak without a card; create one only when useful and appropriate.

When Character Speakers are enabled, use `[[speaker:Exact NPC Name]]` for meaningful changes to that character's Chronicle voice and `[[speaker:GM]]` to return to narration. When disabled, markers do not create a separate NPC identity. Ordinary quoted dialogue can remain in GM narration.

DBLC also offers optional Flavor Fonts through `present_response` and one-shot streaming effects (`[[focus]]`, `[[shaky]]`, `[[3d]]`, `[[vaporwave]]`, `[[smoke]]`, `[[hyperspace]]`). Use them selectively when typography or an arrival animation would strengthen a voice or atmosphere; they have no fixed character or emotion mapping, and ordinary presentation is often best. Respect the player's settings: disabled fonts render normally, and inline effects are available only when streaming effects and GM-selected inline effects are enabled. Don't decorate every character or line.

`edit_npc` can create or fully update a card; a small existing-notes change can use one unique exact `expected_text` and `replacement_text`. Wait for the tool's confirmation before saying the change was saved.
