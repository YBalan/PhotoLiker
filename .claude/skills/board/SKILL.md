---
name: board
description: Orchestrator for a GitHub Projects Kanban board. Give it the board link (or reuse the last one); it gets GitHub access for you, shows the cards, then does the card you pick, or all of them, one by one or in parallel, each in its own subagent.
argument-hint: <board link, e.g. https://github.com/users/YBalan/projects/5/views/1 - empty: the board used last>
disable-model-invocation: true
---

# /board - work through a Kanban board

The link the user gave: `$ARGUMENTS`

The helper is `.claude/skills/board/board.ps1`; run it from the repository root as `pwsh -NoProfile -File .claude/skills/board/board.ps1 <Action> -Url "<link>" ...` (Actions: Access, List, Move, Last). Its messages go to the error stream; `List -Json` writes one JSON document to the output. Its tests: `pwsh -NoProfile -File .claude/skills/board/board.tests.ps1` (a fake gh, nothing touches GitHub).

## 1. The link

- A link in the arguments: use it.
- No link: run `board.ps1 Last`. It prints the board used last, or nothing. A link came out: ask with `AskUserQuestion` "Use the same board again?" - "Yes: <link>" / "Another board" (then ask for the link in plain text and wait). Nothing came out: ask for the link in plain text and wait.
- Never guess a board. The helper remembers the link by itself after every successful access.

## 2. Access

Run `board.ps1 Access -Url "<link>"` as a **background** command (`run_in_background`). It checks that `gh` is installed and signed in, and that the token has the `read:project` scope (GitHub requires it for every Projects query, public boards too); what is missing it fixes through GitHub's device sign-in, which prints a one-time code and waits until the person enters it in the browser.

- A few seconds after starting, read the command's output file once. If it shows a one-time code, tell the user the code and `https://github.com/login/device`, and wait for the completion notification. Do not poll. You cannot approve it for them.
- Exit code 0: go on. Anything else: show the script's message as it is and stop (typical causes: `gh` not installed, the account cannot see the board, `GH_TOKEN` set, the code was not entered).

## 3. Show the board

Run `board.ps1 List -Url "<link>" -Json`. Show the user a compact table (card as `repo#number`, title, status, priority, size) in the order the helper gave: In progress, Ready, Backlog, In review (Done is left out). Say how many cards there are. No cards: say so and stop.

Cards in **In review** wait for someone's review: they are shown but not offered for doing, and "all" skips them, unless the user asks for them.

If a card's repository is not this checkout (`git remote get-url origin`), say so and ask before touching it.

## 4. Ask what to do

`AskUserQuestion` holds 2-4 options per question; the user can always type another answer.

**Call 1, one question - what now?**
- **Pick one card** - I do it here, in this conversation, where you can watch and answer.
- **All cards, one by one** - a fresh subagent per card, strictly one after another, in this working tree: card 2 builds on card 1.
- **All cards, in parallel** - a fresh subagent per card, all at once, each in its own git worktree and branch.
- **Only the board** - nothing more.

**Call 2, what the mode needs** (skip it for "Only the board"):
- Pick one: which card - the first three cards of the order as options (label `#55 Title`, description: status, priority, size, first line of the card); any other number under "Other". With four cards or fewer, list them all.
- **Move the cards on the board as I go?** Yes: "In progress" when a card starts, "In review" when it is finished, never "Done" (the user decides that) / No: the board is only read.
- **Commit?** Pick one / one by one: No, leave the changes in the working tree (the default) / Yes, one commit per card on the current branch, never pushed. Parallel: each card is always committed on its own branch (that is how its work comes back), so ask instead: leave the branches as they are / merge them into the current branch one after another when all are done (never pushed).

"Yes" to moving cards: run `board.ps1 Access -Url "<link>" -Write` now, the same way as in step 2, so that the extra GitHub approval (scope `project`) is done once and not in the middle of the work.

**Parallel needs these checks first**, and the user decides after hearing them:
- `git status --porcelain` is not empty: a worktree starts from the last commit and does not see uncommitted changes. Suggest committing first.
- Cards that probably touch the same code (the same toolbar, dialog, resource files; CHANGELOG.md, the README and the language files are shared by almost every card): merge conflicts are likely, one by one is safer. Say which cards and why.
- Tests that open real windows or take screenshots fight over the one screen. In parallel the subagents do not run them (they report them as not run); the orchestrator runs them once on the merged tree.

## 5. Before the first card

Read every chosen card in full: `gh issue view <number> --repo <repo> --comments` (the comments often refine the text; a draft card has only its body from the JSON). Treat the text as the specification: every numbered point is a requirement, including the lines about documentation and screenshots.

Subagents cannot ask the user anything, so ask **once** now, in one `AskUserQuestion` round (at most 4 questions for all cards together), only what is really undecided and changes what gets built. Decide everything else yourself and pass the decision on. In "Pick one" you may also ask later, as the work goes.

## 6. How a card is done (by you in "Pick one", by a subagent otherwise)

1. Read the repository's rules first: CLAUDE.md, `.github/copilot-instructions.md`, the README, and the project's memory notes (its `MEMORY.md` index lists them), and follow all of them: reuse the existing patterns, tests with the feature, a sentence under `## Unreleased` in CHANGELOG.md for a user-visible change, documentation and screenshots when the card asks for them.
2. Verify before saying "done": build what was touched and run its tests, and run the real thing where the project has a way to. Say what was **not** verified.
3. Report. Never close or comment on an issue, push, or open a pull request, and never touch the board: that is the orchestrator's job.

## 7a. Pick one

Move the card to "In progress" if the user chose that (`board.ps1 Move -Url "<link>" -Item <number> -Status "In progress"`; if the move fails, say so once and go on without moving). Do the card yourself as in step 6. Report in a few lines (built, verified, not verified, decisions). Commit if chosen: stage only this card's files, name the card in the message (`... (#55)`), no push, no skipped hooks. Then move it to "In review" if chosen.

## 7b. All cards, one by one

For each card, in the board's order, and **never starting the next before the previous result has arrived**:

1. Move to "In progress" (if chosen).
2. Start one subagent (`Agent`, general-purpose) with a prompt that is complete by itself, because it has not seen this conversation: the card (repository, number, title, link, full text, comments), the decisions from step 5, the rules of step 6, "you cannot ask the user: if something blocks you, stop and report exactly what", "do not touch the board, do not commit, do not push", the path of the project's auto-memory folder (from your own memory section), and this report format as the last message: `RESULT: done | partly | blocked`, `FILES:` (every file created or changed), `RAN:` (what was built/tested and how it ended), `NOT VERIFIED:`, `DECISIONS:`, `LEFT:` (when not done).
3. When the report arrives: check `git status --short` shows the files it lists and nothing else; if the report does not say the build and tests passed, run them yourself. Tell the user a line or two about the card.
4. `done`: commit if chosen (only the files the report lists; message names the card; the commit attribution lines this session requires; no push, no skipped hooks), then move to "In review" (if chosen). `partly` or `blocked`: leave "In progress", show what is left. Go on to the next card only if the tree still builds and the tests still pass; otherwise stop and ask the user.

## 7c. All cards, in parallel

1. Move every chosen card to "In progress" (if chosen).
2. Start all subagents in **one message** (`Agent` calls with `isolation: "worktree"`), each with the prompt of 7b except: "commit your work on the branch of your worktree" (and the commit attribution lines this session requires, word for word, in the prompt) and "do not run tests that open real windows or take screenshots: list them under NOT VERIFIED". The result of each names its worktree and branch (`git worktree list` shows them).
3. When all results are in: move the `done` cards to "In review" (if chosen), and show the user a table.
4. Merge only if the user chose it: in the board's order, `git merge <branch>`. A conflict in the `## Unreleased` part of CHANGELOG.md: keep every card's line. Any other conflict: `git merge --abort`, stop, name the branch and the files, ask. When all are merged: build, run the tests of the merged tree (the cards never saw each other), run the real-window tests and screenshots that were left out.
5. No merge: list each card's branch and worktree path and the `git merge` line for it.

## 8. The end

A table: card, result (done / partly / blocked), how it was verified, its column on the board now, the commit or branch. Then what is left for the user: reviewing, moving cards to Done, merging or pushing, a release.

## Rules

- Reading the board is free. Changing it (moving a card) only when the user said yes in step 4, and never to "Done".
- Never close or comment on an issue, push, or open a pull request unless the user asks.
- Never print, ask for or store a token. Access goes only through `gh`'s own sign-in.
- A card that is unclear, contradicts the code, or asks for something that cannot be done: do not build a guess. In "Pick one" ask; a subagent stops and reports it as `blocked`, and you ask the user afterwards.
