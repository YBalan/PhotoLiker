---
name: ready
description: Takes the one card that is in the Ready column of the board used last, moves it to In progress, and does it here in this conversation as a single task. The changes stay uncommitted. With several Ready cards it asks which one.
argument-hint: <optional board link - empty: the board that was used last>
disable-model-invocation: true
---

# /ready - do the card that is Ready

The link the user gave (usually none): `$ARGUMENTS`

This is the short, one-card cousin of `/board` (`.claude/skills/board/`). It uses the same helper, `.claude/skills/board/board.ps1`; run it from the repository root as `pwsh -NoProfile -File .claude/skills/board/board.ps1 <Action> -Url "<link>" ...`. Its messages go to the error stream; `List -Json` writes one JSON document to the output.

The card is done **here**, by you, in this conversation, as one task: no subagent, no worktree, no branch. The changes stay **uncommitted** in the working tree.

## 1. The board

- A link in the arguments: use it.
- No link: run `board.ps1 Last`. It prints the board that was used last (it is saved after every successful access, so this is the last board that worked). Use it without asking.
- Nothing came out: ask for the board link in plain text and wait. Never guess a board.

## 2. Access

Run `board.ps1 Access -Url "<link>" -Write` as a **background** command (`run_in_background`). `-Write` is needed because the card is moved. When the token already has the `read:project` and `project` scopes (the normal case) it finishes in a few seconds. Otherwise `gh` starts its device sign-in: a few seconds after starting, read the command's output file once; if it shows a one-time code, tell the user the code and `https://github.com/login/device`, and wait for the completion notification. Do not poll. You cannot approve it for them.

Exit code 0: go on. Anything else: show the script's message as it is and stop.

## 3. The card

Run `board.ps1 List -Url "<link>" -Status Ready -Json`. The cards come ordered by priority (P0 first), then by the board's own order.

- **No card**: say that nothing is in Ready and stop. Do not take a card from another column.
- **One card**: that is the card. Do not ask which.
- **Several cards**: show them as a short list (`#number title`, priority, size) and ask which with `AskUserQuestion`: the first four as options (the first one marked "(Recommended)" - it is the top of the order), any other number under "Other". Do not start before the answer.

If the card's repository is not this checkout (`git remote get-url origin`), say so and ask before touching it.

## 4. Read it in full

`gh issue view <number> --repo <repository> --json title,body,comments,labels,state,url` (not `--comments`: that fails on this account with a Projects (classic) error). A draft card has only its body from the JSON.

Treat the text as the specification: every numbered point is a requirement, including the lines about documentation and screenshots; the comments often refine the text. If the card is unclear, contradicts the code, or asks for something that cannot be done, **ask the user** (you are in the conversation) and do not build a guess. If the issue is closed, say so and ask.

## 5. Move it to In progress

Record `git status --short` first (the baseline: the working tree may already hold other uncommitted work, and your report must name only the files this card changed).

Then `board.ps1 Move -Url "<link>" -Item <number> -Status "In progress"`. If the move fails, say so once and go on. Tell the user in one line which card you took and that it is In progress.

## 6. Do it

1. Read the repository's rules first: CLAUDE.md, `.github/copilot-instructions.md`, the README, and the project's memory notes (its `MEMORY.md` index lists them), and follow all of them: reuse the existing patterns, tests together with the feature, a sentence under `## Unreleased` in CHANGELOG.md for a user-visible change, documentation and screenshots when the card asks for them.
2. Verify before saying "done": build what was touched and run its tests, and run the real thing where the project has a way to. Say what was **not** verified.
3. When the work is finished, save a memory note for the project as its other features have (the card, the decisions, what is unverified) and add its line to `MEMORY.md`.

## 7. What stays untouched

- **No commit**: no `git add`, `git commit`, `git stash`, no new branch. The user reviews the changes in the working tree.
- No push, no pull request, no closing or commenting on the issue.
- The card stays in **In progress**. Do not move it to In review or Done: the user does that (say so at the end).
- Never print, ask for or store a token.

## 8. The end

Report in a few lines: the card and its column now (In progress); what was built (the files this card changed: `git status --short` against the baseline); how it was verified (built, tests, real run); **what was not verified**; the decisions you took. Then what is left for the user: reviewing the uncommitted changes, committing, moving the card to In review / Done.
