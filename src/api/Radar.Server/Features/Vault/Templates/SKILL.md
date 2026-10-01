---
name: radar-second-brain
description: Maintain the project's second brain, a markdown wiki kept in a vault outside the repository. Use it to ingest a new source into the wiki, to answer a question from the wiki, or to check the wiki for contradictions and orphaned pages. Trigger on requests such as "add this to the second brain", "what do we know about X", "check the second brain".
---

# Second brain

The project's `CLAUDE.local.md` has a "Second brain" section that names the vault and the project folder. If it does not, ask the user for the vault path and do nothing until you have it.

Always start by reading `<vault>/_indexMain.md` (the usage rules) and `<project folder>/_index.md` (what the project's wiki contains). Follow the rules in `_indexMain.md`.

## Ingest: "add this to the second brain"

1. Save the source unchanged in `raw/`: a copy of the file, or the pasted text as a markdown file named by date and topic. Never modify or delete an existing file in `raw/`.
2. Read it and update the wiki: create or update pages in `memory/`. Prefer updating an existing page over adding a near-duplicate. Link related pages to each other.
3. Add or update one line per touched page in `_index.md`. Add a line for the new source under `raw`.
4. Report which pages you created or changed and any contradiction with existing pages.

## Query: "what do we know about X"

1. Find the relevant pages through `_index.md` and read only those.
2. Answer from them and name the pages you used. Say clearly when the wiki does not cover the question; do not fill the gap from guesses.
3. If the answer is worth keeping, save it in `outputs/` and offer to fold it into `memory/`.

## Lint: "check the second brain"

Report, without changing anything until the user agrees:

- pages in `memory/` that `_index.md` does not list, and index entries pointing at missing files;
- pages that contradict each other or that look outdated;
- pages that nothing links to.

## Never

- Edit or delete anything in `raw/`.
- Write outside the project's folder in the vault.
- Store secrets, tokens or credentials in the vault.
