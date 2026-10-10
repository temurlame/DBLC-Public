# DBLC Book Processing Specification

**Status:** Authoritative  
**Schema version:** 1.0  
**Purpose:** Define the canonical process for converting visually complex RPG PDFs into faithful, structured, AI-legible JSON companions for DBLC.

---

## 1. Purpose

DBLC book companions exist so the local GM can reliably navigate and retrieve material from RPG books without having to interpret difficult PDF layouts during play.

The canonical pipeline is:

**Original PDF → visually verified structured JSON companion → section-based DBLC retrieval**

The JSON is **not a summary** and is **not a replacement for the PDF**. The original PDF remains the authoritative source. The JSON is its faithful machine-readable representation and navigation layer.

A companion should be predictable, comprehensive, traceable, and trustworthy even when the source PDF is visually chaotic.

---

## 2. Governing fidelity rule

Never invent, infer, repair, simplify, rebalance, reinterpret, or silently complete source material.

If something is unclear, preserve the uncertainty rather than guessing.

Accuracy is more important than speed.

In particular:

- Do not paraphrase a precise rule merely to make it shorter.
- Do not make ambiguous wording more definite.
- Do not silently reconcile contradictions between pages.
- If two passages disagree, preserve both with their provenance.
- Do not add explanations of what a rule “really means” to source content.
- Do not optimize the wording for the GM model.
- Do not pre-answer likely GM questions.

The companion represents **the book**, not the processor's interpretation of the book.

---

## 3. Process the visual PDF, not merely extracted text

Visually complex RPG books cannot be treated as ordinary text documents.

PDF text extraction may:

- scramble reading order;
- merge unrelated columns;
- omit text;
- split or corrupt tables;
- detach headings from their content;
- mishandle rotated text;
- miss text embedded in artwork;
- combine decorative and substantive text.

Therefore the actual rendered pages must be inspected.

Text extraction tools may be used as an **aid**, but extracted text is never unquestioned truth.

Reconstruct the intended human reading order from the visual page.

Pay particular attention to:

- multiple columns;
- rotated text;
- text embedded in artwork;
- sidebars;
- tables and random tables;
- captions and callouts;
- numbered lists;
- footnotes;
- rules split across pages;
- visual hierarchy established by headings or placement.

---

## 4. Canonical staged processing workflow

Every book must go through the following stages.

### Stage A — Source validation

1. Locate the intended PDF.
2. Record its filename.
3. Determine its total PDF page count.
4. Leave the original PDF untouched.

### Stage B — Full visual rendering and inspection

Render the complete PDF at sufficient resolution for reliable inspection.

**150 DPI is a practical baseline**, not a mandatory ceiling. Increase resolution whenever a page cannot be confidently read at that resolution.

Inspect the entire book, including pages that appear decorative.

### Stage C — Extraction-assisted draft

Use PDF text extraction as an aid to transcription and reconstruction.

Build a first structured JSON draft from the visually inspected source.

At this stage:

- establish meaningful sections;
- establish page ranges;
- reconstruct intended reading order;
- preserve substantive prose and rules;
- structure tables where practical;
- record uncertainties rather than resolving them by guesswork.

A draft is not finished merely because every page has been assigned somewhere.

### Stage D — Structural refinement

Review whether the draft's retrieval units make sense to a GM.

Split material when a distinct subject is independently useful to retrieve.

Do not split material merely to make chunks smaller.

Do not impose arbitrary token-sized chunks.

### Stage E — Independent verification/audit

Perform a separate verification pass against the rendered original.

Where practical, treat this as an independent audit rather than merely rereading the draft while assuming it is correct.

Verify especially:

- section/page mapping;
- numbers;
- dice notation;
- modifiers;
- HP;
- damage;
- armor;
- costs;
- durations;
- difficulties or target values;
- table rows and ranges;
- rule exceptions;
- headings;
- sidebars;
- multi-column reading order;
- cross-page material.

Numerical and mechanical information deserves especially careful verification because one incorrect digit can change a rule.

### Stage F — Focused correction pass

Apply confirmed audit corrections.

If an uncertainty can be resolved by returning to the visual source, resolve it.

If it cannot be confidently resolved, leave it explicitly recorded as an uncertainty.

### Stage G — Page-by-page completeness pass

Walk through the original PDF from beginning to end.

Every PDF page must be accounted for as one of:

1. represented in the JSON;
2. intentionally excluded because it contains no substantive retrievable information;
3. recorded as uncertain or unreadable.

There must be no unexplained coverage gaps.

### Stage H — Final validation

Before declaring the companion complete, confirm:

- the JSON parses correctly;
- the required top-level structure exists;
- section IDs are stable and unique;
- page ranges are present;
- meaningful tables have been preserved;
- unresolved uncertainty is explicit;
- substantive pages have been accounted for;
- the source PDF has not been modified.

Only then may the companion be called final.

---

## 5. What must be preserved

Preserve all mechanically or informationally meaningful material, including where present:

- rules;
- procedures;
- exceptions;
- prerequisites;
- numbers;
- dice notation;
- modifiers;
- durations;
- costs;
- damage;
- armor;
- HP;
- abilities;
- character options;
- class features;
- equipment;
- powers, spells, or equivalent abilities;
- monsters and creatures;
- NPC information;
- tables;
- random tables;
- character creation;
- advancement;
- setting information relevant to play;
- GM procedures;
- adventure procedures and content.

Do not discard information simply because its presentation is unusual.

---

## 6. Remove layout noise, not content

The companion should eliminate artifacts that exist only because of graphic design or extraction.

Examples may include:

- repeated headers and footers;
- page furniture;
- meaningless extraction garbage;
- purely decorative typography.

However, unusual presentation is **not evidence that text is decorative**.

Design-heavy RPG books frequently communicate real rules and information through unconventional visual presentation.

When uncertain, preserve the material and record the uncertainty.

---

## 7. Page provenance is mandatory

Every retrievable section must remain tied to the original PDF.

At minimum, preserve:

- PDF page start;
- PDF page end;
- printed/book page information where useful and available.

Material spanning pages should retain the complete source range.

The system must be capable of establishing statements such as:

> This information comes from the source book, PDF pages X–Y.

Never detach extracted information from its source location.

The original PDF remains the final authority if the JSON is questioned.

---

## 8. Canonical JSON structure

The minimum common structure is:

```json
{
  "schema_version": "1.0",
  "book": {
    "title": "Book Title",
    "source_pdf": "Book Title.pdf"
  },
  "sections": [
    {
      "id": "combat",
      "title": "Combat",
      "page_start": 28,
      "page_end": 29,
      "content": "...",
      "tables": [],
      "notes": [],
      "uncertainties": []
    }
  ]
}
```

Do not create dozens of mandatory metadata fields merely because they might theoretically be useful.

The schema should remain simple enough to inspect manually.

---

## 9. Sections are semantic retrieval units

Break a book into meaningful units that a GM could reasonably ask to retrieve.

Examples include:

- Character Creation;
- Abilities;
- Combat;
- Rest;
- Equipment;
- Advancement;
- individual character options or classes;
- creatures;
- powers or magic;
- meaningful setting sections;
- adventure sections.

Do **not** use arbitrary token-sized chunks such as fixed 500-token fragments.

A coherent rule should remain coherent even when it crosses a PDF page boundary.

If a logical section is large and contains independently useful material, use meaningful subsections or separate sections rather than meaningless chunk boundaries.

---

## 10. Sections are also DBLC retrieval boundaries

A complete book companion may be much larger than the GM's available context.

Therefore:

**The complete companion JSON is not intended to be injected wholesale into GM context.**

DBLC should treat section metadata as a lightweight catalogue.

The intended runtime flow is:

**GM sees/searches book catalogue → GM selects relevant section(s) → DBLC returns only those section(s).**

For example, a question about character creation and advancement should retrieve those relevant sections, not unrelated setting, creature, adventure, or table material.

Consequences for processing:

- every major section needs a useful title;
- every major section needs a stable ID;
- page ranges must be reliable;
- independently useful large subjects should be independently retrievable;
- subsection structure may be used where appropriate.

This does **not** mean processing should optimize source content for context size.

Fidelity remains the priority.

**Retrieval controls how much faithful content enters context.**

---

## 11. Stable IDs

Give every section a stable, simple, machine-readable ID.

Examples:

```text
character_creation
combat
advancement
equipment
class_example_name
creatures
```

IDs should be:

- deterministic;
- human-understandable;
- unique within the book;
- stable across ordinary reprocessing where the conceptual section has not changed.

Do not use random GUIDs unless DBLC later explicitly requires them.

---

## 12. Content fields

`content` contains the clean, legible reconstruction of source material in intended reading order.

Preserve meaningful:

- paragraphs;
- lists;
- headings where useful;
- source-page distinctions where they aid provenance or comprehension.

Do not flatten structure unnecessarily.

Do not insert processor commentary into source content.

Notes about processing belong in `notes` or `uncertainties`, not disguised as book prose.

---

## 13. Tables

Tables should receive explicit structure wherever practical and useful.

Example:

```json
{
  "title": "Example Table",
  "page": 20,
  "columns": ["Roll", "Result"],
  "rows": [
    ["1", "First result"],
    ["2", "Second result"]
  ]
}
```

Preserve:

- dice ranges;
- row order;
- column relationships;
- headings;
- modifiers;
- footnotes affecting interpretation.

Never replace a complete random table with a prose summary.

If the source contains 20 results, the companion should contain all 20 results.

Structured tables may coexist with reconstructed page content where this preserves both fidelity and machine readability.

---

## 14. Subsections

Subsections are allowed when they improve meaningful retrieval or preserve natural hierarchy.

A subsection should follow the same general principles as a section:

- stable ID;
- useful title;
- source page range;
- faithful content;
- structured tables where applicable;
- notes;
- uncertainties.

Do not create subsection hierarchy merely for aesthetic neatness.

Use it when the source itself or likely retrieval boundaries justify it.

---

## 15. Cross-page rules

If a procedure begins on one page and continues on another, reconstruct it as one coherent logical unit while preserving the full page range.

Do not split a rule merely because the PDF has a page boundary.

---

## 16. Cross-references

Preserve explicit source cross-references such as references to another rule or page.

Where straightforward and unambiguous, a machine-readable reference to the corresponding section ID may also be recorded.

Do not invent cross-references unsupported by the book.

---

## 17. Notes

Use `notes` for factual processing or provenance information that is useful but is not itself source prose.

Examples include:

- a page was visually inspected and found to be illustration-only;
- printed page numbering differs from PDF page numbering;
- a visually verified structured table is provided because raw extracted text has layout-order artifacts.

Notes must not become a place for interpreting or rewriting rules.

---

## 18. Uncertainty

When material cannot be confidently reconstructed, do not guess.

Record the problem explicitly.

Example:

```json
"uncertainties": [
  {
    "page": 37,
    "issue": "Text partially obscured by artwork; final word cannot be verified."
  }
]
```

Where necessary, preserve the uncertain transcription and identify the uncertain portion.

An honest gap is preferable to a fabricated rule.

Uncertainty is not a processing failure.

**Unrecorded uncertainty is.**

---

## 19. Illustration-only and non-substantive pages

A page containing no substantive retrievable material may be excluded from source content, but it must still be accounted for during the completeness pass.

Record that it was visually inspected and intentionally excluded.

Do not assume a page is non-substantive merely because ordinary text extraction returns nothing.

Visual inspection is required before making that determination.

---

## 20. Do not optimize for a particular GM model during extraction

Do not:

- summarize aggressively to save tokens;
- rewrite rules into GM instructions;
- generate “helpful” interpretations;
- pre-answer common questions;
- discard flavor or setting material merely because it seems less mechanically useful.

Build the faithful source representation first.

DBLC's retrieval layer decides which section(s) the GM receives at runtime.

The companion should remain useful if DBLC changes models later.

---

## 21. Do not rebuild a RAG system inside the companion

This processing standard does not require:

- embeddings;
- vector databases;
- arbitrary semantic chunks;
- generated interpretations;
- confidence scores for rules;
- AI-generated tags everywhere;
- inferred campaign relevance;
- duplicate-rule resolution.

The canonical job remains:

**PDF → verified structured JSON companion.**

DBLC may build a lightweight catalogue from the companion's existing section metadata.

That catalogue is an access mechanism, not a second interpretation of the book.

---

## 22. File relationship

The intended relationship is:

```text
Book Name.pdf
Book Name.json
```

The PDF is the original human-readable authority.

The JSON is DBLC's machine-readable companion.

They represent the same book and should remain directly associated.

Do not modify the original PDF during processing.

---

## 23. Generic JSON reading versus DBLC book companions

A DBLC book companion should be recognized as structured book data rather than treated as an ordinary arbitrary JSON document.

The presence of the canonical schema—particularly `schema_version`, `book`, and `sections`—provides a basis for recognition.

At runtime, recognized DBLC book companions should use catalogue/section retrieval.

A generic whole-file JSON reader may remain useful for small unrelated JSON documents, but DBLC should not rely on whole-file reading for book companions.

This is a runtime DBLC requirement, not a reason to alter or shrink the companion itself.

---

## 24. Processing a new book

For each new book:

1. Preserve the original PDF.
2. Render and visually inspect the entire PDF.
3. Use extraction only as an aid.
4. Establish meaningful semantic sections.
5. Reconstruct substantive content faithfully.
6. Preserve structured tables.
7. Record page provenance.
8. Record genuine uncertainties.
9. Refine independently useful retrieval units.
10. Perform a separate mechanical/layout verification audit.
11. Correct confirmed errors.
12. Walk the entire PDF page-by-page for completeness.
13. Validate the final JSON.
14. Report the processing result.

Do not call the book complete before stages 10–13 are finished.

---

## 25. Completion report

When a book is finished, report at minimum:

- book processed;
- source PDF page count;
- number of top-level meaningful sections;
- whether all substantive pages were accounted for;
- number and nature of unresolved uncertainties, if any;
- any source structures the schema could not represent cleanly;
- output JSON path.

Do not hide unresolved problems merely to produce a clean completion report.

---

## 26. What must remain flexible

This specification standardizes **method and guarantees**, not the editorial structure of every RPG book.

Different books may legitimately require:

- more or fewer sections;
- deeper subsection hierarchy;
- unusual table structures;
- longer narrative or adventure sections;
- special notes for maps or diagrams;
- additional uncertainty records.

Do not force a new book into the structure of a previously processed book.

Preserve the common schema and principles while representing the actual source faithfully.

If the current schema genuinely cannot represent important source material cleanly, report the limitation rather than silently inventing an incompatible extension.

---

## 27. Final acceptance criteria

A companion is acceptable only when all of the following are true:

- **Faithful:** it does not knowingly alter the source's meaning.
- **Complete:** every substantive PDF page has been accounted for.
- **Verified:** mechanical values and visually difficult material received a separate verification pass.
- **Traceable:** retrievable material remains tied to source pages.
- **Structured:** meaningful sections and tables are machine-legible.
- **Retrievable:** major subjects have stable semantic retrieval boundaries.
- **Honest:** unresolved ambiguity is explicitly recorded.
- **Parseable:** the JSON is syntactically valid.
- **Source-preserving:** the original PDF remains untouched.
- **Model-neutral:** the representation is not rewritten for one particular GM model.

---

# Final rule

When fidelity and convenience conflict, choose **fidelity**.

When certainty and guessing conflict, record **uncertainty**.

When text extraction and the actual rendered page conflict, verify against the **rendered source**.

When a book is too large for context, improve **retrieval**, not the fidelity of the companion.

**The PDF is the authority.**

**The JSON is the faithful machine-readable companion.**

**The catalogue tells the GM what is available.**

**The section reader gives the GM only what it deliberately asks to read.**
