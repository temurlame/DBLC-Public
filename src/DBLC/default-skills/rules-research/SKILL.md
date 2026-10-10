---
name: rules-research
description: Find and distinguish published rules, campaign house rules, and historical table decisions using DBLC's library and campaign records.
---

# Rules research

Use this skill when the player asks about a rule, house rule, custom mechanic, or earlier table ruling.

Keep the source types distinct:

- **Published rules:** DBLC's imported PDF books and valid paired Book Companion JSON. Reuse a book ID, page, or section ID already known and available in context. Call `list_books` only when you need to identify or verify a book. Use `get_catalogue` or `get_json_catalogue` only when you need to locate a page or exact section ID; then read only the relevant passage with `read_pages` or `read_json_sections`.
- **Custom rules and current table rulings:** Search `search_gm_notes`, then open relevant notes with `read_gm_note`. Label these as campaign house rules or table rulings, never as published text.
- **Historical decisions:** Use `search_chronicle` and then `read_chronicle` for what was said or decided in play; use `read_previous_session_ending` when that specific ending is what matters. Chronicle history records what happened at the time; do not assume one past ruling is a standing house rule unless a current GM Note or other campaign record establishes that.

Use only the source types relevant to the question. A GM Note may define the current campaign's ruling even when the published rule differs; explain that distinction instead of blending them. A Chronicle decision is evidence of past play, not automatically a general rule.

Stop as soon as the available evidence answers the question. Reuse material already read and make only focused lookups. Reads are bounded: if a request is too large, narrow it rather than treating partial text as complete. Do not invent missing text, page numbers, quotations, or rules.

Only substantive reads can support citations. If a tool result supplies a source handle and citation instruction, cite that source only when it materially informs the answer. Identify GM Note/Chronicle references as campaign records, not book citations; catalogues and searches alone are not citable reads.

Do not use a second model or repeat an unchanged lookup. Use `rules-adjudication` when the player asks how the researched material applies to a particular situation.
