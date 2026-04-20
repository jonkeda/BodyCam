# Documentation Process — Ideas & Proposals

## Current Situation

### What exists

| Location | Content | Quality |
|----------|---------|---------|
| `.my/ROADMAP.md` | High-level vision, hardware, tech stack | Good — stable reference |
| `.my/plan/ROADMAP.md` | Remaining milestones (M4–M9+) | Gets stale as milestones complete |
| `.my/plan/m**/` | Per-milestone step plans | Useful during dev, orphaned after |
| `.my/review/` | Architecture review docs | Detailed but one-shot snapshots |
| `docs/` | Polished architecture, services, tools, config | Good — but written retroactively |
| `docs/functionality/` | Numbered feature guides (01–06) | Good format, but generated post-hoc |

### Current workflow

```
Roadmap → Milestones → Steps → Implement → Ask LLM to generate docs
```

### Problems

1. **Docs are afterthoughts** — written after implementation, so they miss the *why* behind decisions and tend to describe the *what* superficially.
2. **Planning artifacts rot** — milestone plans in `.my/plan/` are never updated after implementation diverges from the plan.
3. **No decision trail** — when a design changes mid-milestone, the reasoning is lost in chat history.
4. **Duplicate/conflicting content** — `.my/ROADMAP.md`, `.my/plan/ROADMAP.md`, and `docs/` overlap without clear ownership.
5. **No changelog discipline** — hard to reconstruct what changed between milestones.
6. **LLM-generated docs drift** — the LLM describes code as it is *now*, not what changed or why.

---

## Proposal: Documentation-During-Development

### Core idea

Shift documentation from a post-implementation phase to a **side effect of each development step**. Not more work — just work at the right time.

### The three-layer doc model

```
.my/plan/       → Planning docs    (BEFORE implementation — intent)
.my/devlog/     → Development log  (DURING implementation — decisions & changes)
docs/           → Reference docs   (AFTER milestone — polished, generated from above)
```

---

## Layer 1: Planning (before) — `.my/plan/`

**Already works well.** Keep the current structure with minor additions.

### Proposed structure per milestone

```
.my/plan/m25-feature-name/
├── overview.md          # Goal, scope, acceptance criteria
├── steps.md             # Ordered implementation steps (what you already do)
├── design-decisions.md  # Key choices BEFORE coding (new)
└── api-surface.md       # Public interfaces/contracts planned (optional)
```

### `design-decisions.md` template

```markdown
# Design Decisions — M25 Feature Name

## DD-1: [Short title]
**Date:** 2026-04-20
**Context:** What problem are we solving?
**Options considered:**
1. Option A — pros / cons
2. Option B — pros / cons
**Decision:** Option B
**Rationale:** One sentence why.
```

This is a lightweight ADR (Architecture Decision Record). The key value: when you revisit this in 6 months, you know *why* you chose Option B.

**Effort:** ~2 minutes per decision. You already think through these — just write the conclusion down.

---

## Layer 2: Development Log (during) — `.my/devlog/`

**This is the missing piece.** A chronological log of what actually happened during implementation.

### Proposed structure

```
.my/devlog/
├── 2026-04-20.md    # One file per day (or per session)
├── 2026-04-21.md
└── ...
```

### Daily log entry template

```markdown
# 2026-04-20

## Milestone: M25 — Feature Name

### What I did
- Implemented `IFooService` with two providers
- Added `FooTool` for Realtime API function calling

### Decisions made (or changed)
- Switched from polling to event-driven because [reason]
- Dropped `IBarService` — not needed, `FooService` handles both cases

### Gotchas / things to remember
- Android `AudioRecord` requires `RECORD_AUDIO` permission even in background
- NAudio `WasapiCapture` silently returns zeros if the device is exclusive-mode

### What's next
- Wire up `FooTool` in `ToolDispatcher`
- Add unit tests for edge case X
```

### Why this works

- **Takes 3–5 minutes** at the end of a coding session — you just brain-dump what you did.
- **Captures decisions at the moment they happen** — not reconstructed weeks later.
- **Gotchas are gold** — these are the things that would otherwise be lost forever.
- **Feeds the LLM** — when you ask an LLM to generate polished docs, the devlog gives it far richer context than just reading the code.

### Integration with LLM doc generation

Instead of asking: *"Look at the code and write docs"*

You can now ask: *"Read `.my/devlog/2026-04-*.md` and `.my/plan/m25-*/`, then generate/update `docs/functionality/07-foo-feature.md`"*

The LLM gets intent (plan), context (devlog), and code — producing much better docs.

---

## Layer 3: Reference Docs (after) — `docs/`

**Already in good shape.** The numbered `functionality/` format works well.

### Proposed additions

```
docs/
├── architecture.md              # Existing — keep as-is
├── services.md                  # Existing — keep as-is
├── tools.md                     # Existing — keep as-is
├── configuration.md             # Existing — keep as-is
├── testing.md                   # Existing — keep as-is
├── changelog.md                 # NEW — milestone-level changelog
├── deployment/
│   └── android.md               # Existing
└── functionality/
    ├── 01-overview.md            # Existing
    ├── ...
    └── 07-new-feature.md         # Added per milestone
```

### `changelog.md` format

```markdown
# Changelog

## M28 — UI Frames (2026-04-15)
- Added frame-based navigation with `FrameViewModel`
- New settings categories: Audio, Vision, Provider
- Broke: Old flat settings page removed

## M27 — Settings Overhaul (2026-04-01)
- Migrated from `Preferences` to `SettingsService` with JSON backing
- Added per-provider model configuration
```

This gives a quick "what happened when" view without reading milestone plans.

---

## Proposed Workflow (Step by Step)

### Before a milestone

1. Create `.my/plan/mXX-name/overview.md` — goal, scope, acceptance criteria
2. Create `.my/plan/mXX-name/steps.md` — ordered steps *(you already do this)*
3. Write `design-decisions.md` for any non-obvious choices

### During implementation

4. Work through steps as usual
5. At the end of each session, spend 3–5 min writing a devlog entry in `.my/devlog/YYYY-MM-DD.md`
6. If a design changes, update `design-decisions.md` (add a new DD entry, don't delete old ones)

### After a milestone

7. Ask the LLM to generate/update docs, pointing it at:
   - The plan (`overview.md`, `steps.md`, `design-decisions.md`)
   - The devlog entries for this milestone period
   - The relevant code
8. Add a `changelog.md` entry
9. Review generated docs — fix any inaccuracies
10. Optionally archive the milestone plan: mark as `Status: Done` in `overview.md`

---

## Automation Ideas

### LLM prompt template for doc generation

Save this in `.my/Dev/doc-gen-prompt.md` and reuse it:

```
Read the following sources:
1. Plan: .my/plan/mXX-name/
2. Dev log entries from [date range]: .my/devlog/
3. Code: src/BodyCam/[relevant folders]

Generate or update docs/functionality/NN-feature-name.md following the style
of the existing docs (01-overview.md through 06-settings-and-config.md).

Include: what it does, how it works (with code references), key types,
configuration options, and any platform-specific notes.

Also update docs/changelog.md with a summary of this milestone.
```

### Copilot instructions integration

Add to `.github/copilot-instructions.md`:

```markdown
## Documentation

- When completing a milestone, generate docs from `.my/plan/` + `.my/devlog/` + code.
- Follow the numbered format in `docs/functionality/` (NN-feature-name.md).
- Update `docs/changelog.md` with a milestone summary.
- Never delete decision records — append new decisions instead.
```

### Git hooks (optional, low priority)

A pre-commit hook could check if a devlog entry exists for today when committing milestone-tagged changes. Probably overkill for a solo project but worth considering if the habit doesn't stick naturally.

---

## What NOT to Do

- **Don't write docs during planning** — the plan is the doc at that stage. Don't duplicate.
- **Don't force formal templates** — the devlog should be quick and informal. If it feels like a chore, you'll stop doing it.
- **Don't document every single change** — the devlog captures sessions, not individual lines of code. Keep it high-level.
- **Don't maintain two sources of truth** — `docs/` is the authoritative reference. `.my/plan/` and `.my/devlog/` are inputs to generate it, not competing docs.
- **Don't skip the LLM step** — the whole point is that the LLM does the heavy lifting of turning raw notes into polished docs. You just give it better inputs.

---

## Summary

| Phase | Artifact | Location | Effort | Who writes it |
|-------|----------|----------|--------|---------------|
| Before | Plan + decisions | `.my/plan/mXX/` | ~10 min | You |
| During | Dev log | `.my/devlog/` | ~3–5 min/session | You |
| After | Reference docs | `docs/` | ~5 min review | LLM + you review |
| After | Changelog | `docs/changelog.md` | ~2 min | You or LLM |

**Total added effort per milestone: ~20–30 minutes of note-taking, spread across sessions.**

The payoff: docs that actually reflect what was built and why, a searchable decision history, and much better LLM-generated reference docs because the LLM has real context instead of just code.
