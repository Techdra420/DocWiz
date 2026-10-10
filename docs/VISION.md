# DocWiz — Vision

> The "why" document. The "how" lives in [ARCHITECTURE.md](ARCHITECTURE.md); the "when"
> in [ROADMAP.md](ROADMAP.md).

---

## The problem

Vendors who deliver a system to a customer — a software product, an installed
application, a configured platform — hand over documentation with it. That
documentation is usually **one comprehensive PDF manual**.

Two pains:

1. **One artifact, many audiences.** End users, support staff, and administrators need
   different depth, orientation, and even language. The full manual is too much for a
   user and not navigable enough for an administrator.
2. **Manual re-packaging.** Producing user training today means taking the PDF and
   hand-building a slide deck from it. Every content update means redoing that work.
   This is the concrete pain that triggered the project.

## The idea

**One Markdown source of truth → many generated outputs. Nothing is ever duplicated.**

```
                       ┌──────────────────────────────────────────────┐
  Product team ─────┐  │  DocWiz project (folder of Markdown + assets)│
  Support team ─────┼─►│  content/   views/   assets/   attachments/  │
  Integration    ───┤  └──────────────────┬───────────────────────────┘
  Project mgmt   ───┘                     │  docwiz build
        ▲                                 ▼
        │ edits via            ┌─────────────────────────┐
  ┌─────┴──────┐               │  Interactive HTML        │
  │ DocWiz App │               │  handover                │
  │ (WYSIWYG   │               │  ├─ Full manual (truth)  │
  │  editor)   │               │  ├─ User training        │  ← slide deck, simple language
  └────────────┘               │  ├─ Support view         │  ← procedures, troubleshooting
                               │  └─ Administrator view   │  ← attachments (PDF), API reference
                               └─────────────────────────┘
```

### The handover artifact

A self-contained, **offline-capable interactive HTML documentation package**:

- Full manual: every chapter, fully cross-linked, searchable, with a navigation tree —
  the same role today's PDF plays, but actually navigable. Clean, sober look, light and
  dark, no fluff.
- **Audience views ("screens")** generated from the same source:
  - **User training** — a slide-style presentation. Doubles as the script for
    in-person training. Content (screenshots, step-by-step procedures) is pulled live
    from the manual chapters, so updating the manual updates the training.
  - **Support** — tidy procedures: how to perform routine tasks, how to troubleshoot,
    where to find more detail in the manual.
  - **Administrator** — deep technical material: attachments (PDF data sheets and
    release notes rendered in-browser) and **external HTML documentation packages**
    such as a generated API reference integrated alongside.

Views are **defined once per product type and reused across projects** — the user
training structure barely changes between customers; only the transcluded content does.

### The editor

Product, support, and project-management colleagues are not programmers and will not
edit raw Markdown in a code editor. They get a **DocWiz app** (a native window, launched
from a single exe) that:

- opens a DocWiz project and shows its chapter tree;
- edits chapters **like Word**: headings, bullets, lists, tables, images (paste/insert),
  and — critically — **links** between chapters and to attachments;
- offers **templates per chapter type/discipline**, so the structure guides the author —
  for most projects, authors mostly swap screenshots and adjust details;
- lets users create, organize, reorder, and delete the audience views without touching
  the engine.

Under the hood it reads/writes plain Markdown files — the source of truth stays
diffable, mergeable, and tool-agnostic.

## What success looks like

1. A real customer project handed over as a DocWiz HTML package instead of (or alongside) the PDF.
2. User training delivered by clicking through the generated user view — zero slide-deck rebuilding.
3. A non-programmer colleague updates a chapter (new screenshot, changed settings table) in the
   editor without help, rebuilds, and all views update.
4. The administrator view shows attachments and the external API reference in one place.

## Non-goals (for now)

- Replacing the contractual PDF immediately — DocWiz can generate alongside it; PDF
  export from the same source shipped as a V1.0 feature (`docwiz export-pdf` + an editor
  "Export…" action).
- Multi-user real-time collaboration, web hosting, databases, accounts. A DocWiz project
  is a folder; teams share it the way they share any project folder (or via git, opt-in).
- Authoring the external documentation packages themselves (API references, release
  notes sites) — DocWiz integrates their output.

## Audience: templates first, filtering second

Stakeholder position (2026-06-11): the user presentation will be a reusable
*template* — a stable skeleton of slides whose content is links to chapters and
anchors; per project only the images and a few chapters change. That selection
mechanism is **inclusion** (the view picks what's in), and it needs no audience
tagging at all.

That position is largely right, and it changes the ranking of the mechanisms:

1. **Primary model: views as transclusion playlists from templates.** Selection by
   inclusion is legible — "what does the user see?" = open the view and read the
   slide list. The audience knowledge lives in the template's structure, not in tags
   scattered through chapters. The product supports this today: project templates
   ship with pre-built views, and views can be duplicated with all slides to stamp
   out the next training from a proven structure.
2. **Residual value of `::: audience` blocks: shared procedures with one divergent
   note.** The case playlists cannot cover: a 10-step procedure used by users
   AND support where step 6 needs a support-only warning. Without block
   filtering that means either showing everyone everything or maintaining two copies
   of the procedure — drift again. This is real but rare; it does not justify making
   audiences prominent in the authoring UI.
3. Chapter-level audience checkboxes and the view's audience binding stay as cheap
   metadata (they label views and gate gross mismatches), but they are not the
   workflow.

Plan: the pilot runs template-first — build the user view as a playlist, no audience
blocks unless a shared procedure genuinely diverges. Count how often `::: audience` is
actually needed. If it stays at zero, demote it: keep the pipeline support (cheap,
tested), drop the toolbar button and the new-chapter audience prompts. No new audience
machinery either way.

**Update 2026-10-10 (stakeholder decision, supersedes the plan above):** real projects tag
audiences *inside* chapters too — single paragraphs and steps, not just whole chapters — and
may have many audiences. Audiences become first-class in the editor: one Readers picker per
chapter and per block (search, "Everyone" shortcut, scales to many audiences), a quiet margin
marker on tagged blocks, a manager with a one-line description and usage counts, and
"Preview as" an audience to check the result. Views as playlists stay the primary way to
build an output; audience tags are how one source serves several readers without copies.
See Stage 6 in [ROADMAP.md](ROADMAP.md).

## Open wishlist

Items captured from dogfooding that are still open (everything shipped is recorded in
[ROADMAP.md](ROADMAP.md)):

- Ctrl+K shortcut for the full-text search box (the box is click/Tab-focused only).
- Different display sizes — on a wide monitor the slides stretch to the full width;
  showing the whole slide is the priority.
- Working with images inside note/warning/danger callouts.
- Items library — a right-hand sidebar listing all assets, attachments, and reusable
  elements (custom notes with icons).
- Adjusting the layout of the slide presets.
- Subchapter display in the handover navigation.
- A "white to alpha" button for pictures with a white background.
- Free positioning ("drag it where I want it") of linked blocks on slides — deferred to
  a later free-canvas stage.
