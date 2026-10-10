# DBLC Agent Skills

DBLC stores optional local GM skills in `%LOCALAPPDATA%\DBLC\skills` (or the active `DBLC_DATA_ROOT\skills`). Each skill is a folder containing `SKILL.md` with YAML frontmatter for `name` and `description`, followed by its instructions:

```text
skills/
└── my-skill/
    └── SKILL.md
```

On first startup DBLC copies its six starter skills into this folder. The GM can call `list_skills` to see names and short descriptions, then `read_skill` with an exact name to load applicable instructions for that response. Skill bodies are not included in the normal prompt. Existing files can be edited or removed and new folders added while DBLC is running; the next discovery or load reads the current files.

Use lowercase kebab-case names matching the folder. Keep the description concise and the instruction body under 24,000 characters. DBLC reads only the `name` and `description` metadata; additional YAML metadata is ignored. Invalid or oversized files are reported/skipped rather than silently truncated. Skills are plain instructions: they can refer to DBLC's existing tools but do not add tools or permissions.
