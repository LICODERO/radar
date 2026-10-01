# Second brain

Knowledge base for my projects, kept outside the repositories. Created by R.A.D.A.R.

## Projects

<!-- radar:projects -->
_No projects yet._
<!-- /radar:projects -->

The list above is maintained by R.A.D.A.R. Do not edit between the markers.

## How to use this vault

Read this file first, then the `_index.md` of the project you are working on.

Every project folder has three parts:

- `raw/` – source material: specs, articles, transcripts, exports. Read-only: never edit or delete files there. New sources are added by the user.
- `memory/` – the wiki you maintain: short markdown pages (decisions, concepts, how things work), linked to each other and listed in the project's `_index.md`.
- `outputs/` – results of your work that are worth keeping (analyses, reports, answers). Fold the important findings back into `memory/` as wiki pages.

Rules:

1. Before answering a question about a project, read its `_index.md` and open only the pages that are relevant.
2. When you learn something durable (a decision, a constraint, how something works), write or update a page in `memory/` and add one line for it to `_index.md`. Update the existing page instead of creating a duplicate.
3. Keep every `_index.md` entry to one line: `- [title](path) – what it contains`.
4. Never put secrets, tokens or credentials in the vault.
5. If pages contradict each other, or an index entry points to a file that does not exist, fix it or tell the user.

If the `radar-second-brain` skill is available, use it for the three routine operations: ingest (add a source and update the wiki), query (answer from the wiki) and lint (find contradictions and orphaned pages).
