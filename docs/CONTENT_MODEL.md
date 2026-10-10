# DocWiz — Content Model

How a DocWiz project is structured on disk and how audience filtering and
transclusion work. This is the contract between the editor, the builder, and humans
who edit files directly.

---

## A DocWiz project is a folder

```
my-product-docs/
├── docwiz.yaml              # project manifest
├── content/                 # the source of truth — Markdown chapters
│   ├── 010-overview/
│   │   ├── 010-system-overview.md
│   │   └── 020-security-summary.md
│   ├── 020-setup/
│   ├── 030-operations/
│   ├── 040-troubleshooting/
│   ├── 050-reference/
│   └── 100-appendix/
├── views/                   # audience views ("screens") — YAML definitions
│   ├── user-training.yaml
│   ├── support-quickref.yaml
│   └── admin-guide.yaml
├── assets/                  # images, diagrams, screenshots (referenced from Markdown)
└── attachments/             # PDFs and other binary documents (data sheets, release notes)
```

- **Numeric prefixes** (`010-`, `020-`) define order. Gaps of 10 leave room for
  insertions. The editor manages renumbering.
- Folder = section, file = chapter. One `H1` per chapter.
- **The folder name is the section name** (Stage 6). A section folder is named
  `NNN-<exact section name>` — e.g. `020-Instalace a síť` — and everything after the prefix is
  shown verbatim (accents, spaces, capitals). Renaming, moving, or deleting a section in the
  editor acts on the folder itself; names containing `\ / : * ? " < > |` are refused, and a
  section that still holds chapters can't be deleted. Legacy slug folders (`020-setup`,
  `050-api-reference`) keep their derived titles ("Setup", "API Reference"). Empty sections
  are kept (and shown in the editor) until deleted.
- **A chapter file follows its title**: `NNN-<title>.md`, with characters Windows forbids
  turned into spaces (the title itself keeps them). Renaming a chapter — or changing its H1 —
  renames the file. Links never depend on file or folder names; they use the chapter `id`.
- **Planned (Stage 6, step 3): `.docwiz/history/`** holds the editor's change history —
  complete copies of earlier file versions, kept until deleted. Nothing reads it except
  the History panel, so deleting any part of it is safe. Git-ignored; never shipped.

## Manifest — `docwiz.yaml`

```yaml
project: Stockroom Inventory System
code: SR-DEMO-001
customer: Example Customer
version: 0.1.0
language: en

audiences:
  - id: user
    title: User
  - id: support
    title: Support
  - id: admin
    title: Administrator

external:
  # Optional: paths to self-contained HTML documentation folders, each embedded as a tab
  api_reference: null
  release_notes: null
  integration_guide: null
```

`audiences` is project-configurable, not hardcoded — a project can add `trainer`
or a customer-specific audience without engine changes.

## Chapter frontmatter

```markdown
---
id: system-overview          # stable ID — never changes after creation; used by links/views
title: System Overview
discipline: overview         # optional: the skeleton the chapter started from; not used by the build
audiences: [user, support, admin]   # who sees this chapter at all
---

# System Overview

...content...
```

**`id` is the stability anchor.** Links and view transclusions reference `id`, not file
paths or heading text, so renaming files, retitling headings, or reordering never breaks
references. The editor generates IDs from the title (accented letters keep their base
letter: "Čištění skeneru" → `cisteni-skeneru`) and keeps them immutable.

**The title is the H1.** `title` and the chapter's `# heading` are one thing: the editor's
save sets `title` from the H1, and renaming a chapter rewrites both. `discipline` records
the skeleton a chapter started from ("Start from" in the New chapter dialog: `blank`,
`overview`, `setup`, `operations`, `troubleshooting`, `reference`, `integration`); the
editor no longer changes it after creation.

## Block-level audience filtering

Inside a chapter, fenced containers scope content to audiences:

```markdown
The system tracks every item from receipt to dispatch.

::: audience user
Press the green **START** button to begin a receiving task.
:::

::: audience admin
Receiving posts to the ledger through the `movements` endpoint; retries are logged.
:::
```

Unfenced content is visible to **all** audiences listed in the chapter frontmatter.
The full manual always shows everything (it is the source of truth).

## Stable section anchors

Headings can carry explicit anchors for fine-grained transclusion:

```markdown
## Daily startup {#daily-startup}
```

Without an explicit anchor, a slug is derived from the heading text — fine for ad-hoc
links, but **views should reference explicit anchors** so retitling doesn't break them.
**The editor persists an explicit `{#slug}` for every `##`+ heading automatically on
save** (the H1 is the chapter itself: `dw:chapter-id`), so in practice all sections
are addressable without manual anchor work.

## Automatic element anchors & captions

Every table, standalone image, and attachment link is addressable and captioned
automatically (no syntax needed):

- tables get `#table-1`, `#table-2`, … and render with a "Table N" caption;
- standalone images (an image alone in a paragraph) get `#figure-N` and render with a
  "Figure N — *alt text*" caption — write meaningful image alt text, it is the caption;
- links into `attachments/` get `#attachment-N`.

These generated anchors are **positional** — adding a table above shifts the numbers.
For long-lived references prefer heading anchors; `docwiz check` flags any reference
that broke. An explicit `{#my-table}` attribute line above a table overrides the
generated id (caption numbering is unaffected).

## Table column widths

An attribute line above a table sizes its columns:

```markdown
{widths="30% 70%"}
| Component | Description |
|-----------|-------------|
```

Space-separated CSS lengths (`30%`, `240px`, `auto`), one per column, rendered as a
`<colgroup>`; the table itself stays within the page width. `docwiz check` warns when
the count doesn't match the column count or a value isn't a plain CSS length. In the
live editor, dragging a column edge writes percentages here and "Auto widths" removes
the attribute. Combine with an explicit id as `{#my-table widths="30% 70%"}`.

A separate `width=` on the same attribute line sizes the **whole table box** narrower
than the column — a single CSS length (`50%`, `480px`, `auto`); `%` is relative to the
available width (the page column, or a slide zone), so it stays correct across page
sizes:

```markdown
{width=50% widths="30% 70%"}
| Component | Description |
|-----------|-------------|
```

Height always follows content (no fixed-height tables). In the live editor, dragging the
table's right/bottom edge writes `width=` here. `docwiz check` warns when the value isn't
a single CSS length. Omitting `width=` keeps the default full-column table.

A sized table can be placed with `align=center` or `align=right` on the same line
(`{width=60% align=center}`); left is the default (no token). It renders as a `dw-box-*`
class on the figure (auto margins), never a deprecated `<table align>` attribute. The live
editor's table "Place" buttons write it.

## Table captions

A `caption="…"` attribute on the same attribute line adds a human-readable title to the
`figcaption`, appended after the auto-number with an em dash (U+2014):

```markdown
{caption="Security functions"}
| Function | Trigger | Reaction |
|----------|---------|----------|
| Session lock | Idle timeout | Sign-in required |
```

Renders as `<figcaption>Table 1 — Security functions</figcaption>`. The caption
is editable in the live editor's "Caption" input on the table's context bar (same
pattern as the figure alt-text input). It combines freely with `widths=`, `width=`,
`align=`, and explicit ids:

```markdown
{#security-table widths="30% 30% 40%" caption="Security functions"}
```

An empty or absent `caption=` emits only `Table N` (no dash, no trailing space).

## Image sizing

`![Dashboard](../../assets/dashboard.png){width=640 .img-center}` renders the image
640 px wide and centered (`.img-right` aligns right; captions follow the image).
The editor's *Insert image* dialog offers Size (Original / 960 / 640 / 320) and
Alignment (Left / Centered / Right).

### Hiding the caption (`.no-caption`)

By default every standalone image renders with a `<figcaption>Figure N — alt text</figcaption>`
below it. Add `.no-caption` to suppress the visible caption while keeping the `alt`
attribute on the `<img>` element for screen readers and tooltips:

```markdown
![Architecture diagram](../../assets/architecture.png){.no-caption}
![Sized, hidden caption](../../assets/x.png){width=640 .img-center .no-caption}
```

The class is a figure-level instruction — it never appears on the rendered `<img>`.
In the live editor the "Caption" toggle button (eye icon) in the image context bar
writes and removes this class.

## Content elements (the `:::` library)

Designed building blocks, written as fenced containers and rendered identically in the
manual, document views, slides, and the editor preview (one pipeline). The editor's
**Insert element** button offers all of them with ready-to-edit templates.

```markdown
::: warning Unsaved changes      # note | tip | warning | danger — optional custom label
Sign out before restarting the station or the open task is lost.
:::

::: steps                        # the ordered list renders with big numbered markers
1. Switch the station on.
2. Sign in with your badge.
:::

::: cards                        # the bullet list becomes a navigation card grid
- [System Overview](dw:system-overview) — what the system does
- [Security](dw:security-summary) — access rules and audit trail
:::

::: buttons                      # links render as a button row
[Open the manual](dw:system-overview)
:::

::: details Supported scanners   # collapsible; the argument is the summary text
Hidden until the reader expands it.
:::

::: center                       # center | right — text alignment for the content inside
Centered content (text, tables, images).
:::

::: group Retention limits {#retention-limits desc="Author note — not shown to readers"}
Any run of blocks — prose, tables, images, callouts — becomes one named unit.
:::
```

`docwiz check` warns about container names outside the library (typo protection).
Elements may be nested inside `::: audience` blocks.

### `group` — named, addressable wrapper

`group` wraps a run of blocks into a single named unit that is **invisible to readers**
(rendered as a plain `<div class="dw-group" id="…">` with no box or label) but is
**addressable** for embedding and linking:

```markdown
::: group Retention limits {#retention-limits desc="Admin note — not reader-facing"}
This retention table applies to the audit trail.

| Record | Kept for |
|--------|----------|
| Movement | 24 months |
:::
```

- **Syntax**: `::: group <Name> {#id desc="optional author note"}` … `:::`
- **Name** — the friendly label shown in the embed/section picker; pick something
  meaningful to authors, not displayed to readers.
- **`{#id}`** — a stable anchor (ADR-005); use it to embed or link the group from a
  slide view: `::: embed dw:<chapter-id>#<group-id> :::` or `dw:<chapter-id>#<group-id>`.
  Renaming the group never changes its id. A group with no id gets a validation Warning.
  The editor auto-stamps an id from the name on first save when absent.
- **`desc=`** — an author-only note (never rendered in the handover or in slides).
- **Editor display** — shown as a labelled frame in the live editing surface so authors
  can see the grouping boundary; invisible in the built handover.
- **Use case** — pull just the prose/bullets of a chapter section onto a slide (e.g.
  figure on the left, the grouped text on the right) without copy-paste divergence.

## Automatic chapter numbering

The manual numbers itself Word-style with **flat chapter numbers**: chapters are
`1`, `2`, `3`, … in document order across the whole project; inside a chapter the
headings continue the number (`6`, `6.1`, `6.1.1`). Section folders are unnumbered
group labels (like Word parts) — the section level does not consume a numbering
level, matching how conventional PDF manuals are numbered. Numbers appear in the
navigation, the contents page, prev/next, the editor's chapter tree, the live
editing surface (live, while typing), the Reading view, and on the pages. They
come from document order, never from the markdown, so everything renumbers
automatically when chapters move. Views and slides are never numbered.
Opt out with `numbering: false` in `docwiz.yaml`.

**Do not type numbers into headings.** Content migrated from Word/PDF often
carries `1.1 Modification`-style prefixes — with numbering on they would show
twice, and the typed numbers rot when chapters are reordered. `docwiz check`
warns about every heading that starts with a typed number.

## Migrating existing documents

**Triage first**: only living prose the team will keep editing becomes Markdown.
Data sheets, certificates, and supplier manuals go into `attachments/`
unconverted — the handover embeds PDFs natively.

`docwiz import-docx <file.docx> <project> --section <folder> [--discipline <d>]`
(experimental; needs pandoc — see `tools/README.md`) converts a Word document:
splits at Heading 1 into one chapter per heading, strips typed numbering
prefixes, adds stable heading anchors, converts `NOTE:` / `WARNING:` /
`CAUTION:` / `DANGER:` / `TIP:` paragraphs into callout elements, routes
embedded images into `assets/<chapter>/`, and prints a review report (complex
tables, EMF/WMF vector images, front-matter/TOC leftovers). PDF-only sources go
through `tools/pdf2docx.ps1` (Word's reflow conversion) first.

## External documentation packages

Each non-null entry under `external:` is a path to a self-contained HTML
documentation folder (its `index.html` — or a navigable `handover.html` — is the
entry point). The builder copies it into the handover and adds a tab for it. The
three keys above are the default convention (`api_reference` is where a generated
API site goes); any key works — the tab title derives from the key.

## Views — `views/*.yaml`

A view is an ordered composition of transcluded source sections plus minimal glue.
Views contain **no duplicated content** — only references and small connective text.

```yaml
id: user-training
title: User Training
audience: user               # applies block-level filtering to everything transcluded
mode: slides                 # slides | document
slides:
  - include: system-overview             # whole chapter (filtered for the audience)
  - include: security-summary#access-rules   # one section of a chapter
  - title: Daily Operation               # explicit slide title override
    include: daily-operations#user-screens
  - markdown: |                          # glue slide — keep these rare and short
      ## Questions?
      Contact your team lead or the support desk.
```

- `mode: slides` renders as an in-browser presentation (keyboard navigation,
  fullscreen) — the slide-deck replacement.
- `mode: document` renders as a filtered, tidy single page — the support quick
  reference style.
- The same `include` syntax is also valid inline in chapters for cross-chapter reuse
  (e.g., the security summary transcluded into the troubleshooting chapter).
- Inside a slide region's markdown, a linked chapter element is transcluded inline with the
  two-line directive `::: embed dw:chapter#anchor` / `:::`. An optional `width=` on the info
  line (`::: embed dw:chapter#anchor width=60%`) sizes the linked box to a single CSS length
  (`60%`, `480px`, `auto`; `%` is relative to the slide zone or page column) and the
  transcluded content **reflows** to that width — it is not scaled/zoomed. Omitting `width=`
  keeps the box full width. `align=center` / `align=right` on the same info line places a
  sized box (left is the default). In the live editor, dragging the linked block's right edge
  writes `width=` and its Left/Center/Right buttons write `align=`.

## Markdown flavor

CommonMark plus the extensions the engine guarantees (see ADR-004 in
[ARCHITECTURE.md](ARCHITECTURE.md)):

- tables, task lists
- heading attributes `{#anchor}`
- fenced containers `::: audience ...` / `::: include ...`
- images with relative paths into `assets/`
- links: `[text](dw:system-overview#daily-startup)` — the `dw:` scheme links by stable ID;
  plain relative links also work but are second-class
- attachment links: `[Release notes](../attachments/release-notes.pdf)` — rendered
  in the handover with an embedded PDF viewer
