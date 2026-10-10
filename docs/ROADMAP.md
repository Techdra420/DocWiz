# DocWiz — Roadmap

**Current Stage:** Stage 5 — pilot on a real customer project. Stages 1, 2, and 4 complete;
Stage 3 (editor MVP) functionally complete (live editor shipped; single exe verified on a
clean machine). Stage 6 (editor redesign) approved 2026-10-10 and in progress alongside the
pilot — all seven steps done (trust fixes, structure you can edit, history then autosave,
shell and document bar, audiences, outputs editor, consolidation); awaiting the stakeholder's
check of the success criteria.
**V1.0:** both agreed V1.0 features — **PDF export** and **content-only zoom** — are built and
merged to `main`. Remaining for the v1.0 tag: close the pilot loop + release hygiene (tag, prune).
ADR-004 (amended), ADR-005, ADR-006, ADR-007, and ADR-008 are all written in
[ARCHITECTURE.md](ARCHITECTURE.md).
**Last Updated:** 2026-10-10 — Stage 6 (editor redesign) steps 1–7 shipped: trust fixes,
structure you can edit, history then autosave, shell and document bar, audiences, outputs
editor, consolidation; approved with its decisions and steps the same day; Claude desktop app
setup; then a toolbar design pass (formatting toolbar, context bars, document bar, slide
toolbar; scroll bars in the theme); then the DocWiz Guide template — the user manual,
written in DocWiz, as a demo of every feature; then a visual-probe fix — text contrast is
measured against the background actually painted (translucent callout tints no longer fail).

Stage transitions require human approval. Success criteria are the checkpoints.

---

## Stage 0 — Vision & Architecture (complete)

- [x] Capture vision ([VISION.md](VISION.md))
- [x] Define content model ([CONTENT_MODEL.md](CONTENT_MODEL.md))
- [x] Propose architecture + ADRs ([ARCHITECTURE.md](ARCHITECTURE.md))
- [x] Sample DocWiz project demonstrating the content model (`samples/stockroom-demo/`)
- [x] Human review: approve/adjust ADR-001..006 — all six accepted 2026-06-10

**Success criteria:** stakeholder signs off on the architecture and the mockup direction. ✔ Met 2026-06-10.

## Stage 1 — Build pipeline core (complete)

- [x] Solution scaffold: `DocWiz.Core`, `DocWiz.Build`, `DocWiz.Cli`, tests (net8.0,
      warnings-as-errors, Markdig + YamlDotNet only)
- [x] Project loader + validator (`docwiz check`): manifest, chapters, frontmatter,
      duplicate/missing IDs, unknown audiences, dangling `dw:` links and view includes,
      missing assets/attachments (warnings)
- [x] Markdown pipeline: Markdig + audience containers (`data-aud` markup) + heading
      anchors (explicit `{#id}` + GitHub auto-ids) + `dw:` link rewriting + missing-image
      placeholders — single pipeline shared with the editor preview (ADR-004)
- [x] `docwiz build`: full manual — nav tree, prev/next, cross-links, assets, light/dark
- [x] Client-side search over the manual: build-time index emitted as
      `_dw/search-index.js` (`window.__dwSearch`, loaded via a script tag so it works
      from `file://` — local `fetch` is browser-blocked), sidebar search box with
      ranked results + highlighted snippets (`search.js`)
- [x] PDF attachments: an Attachments page (chrome tab when any exist) lists every
      file grouped by subfolder; PDFs embed inline via a browser-native `<iframe>` and
      everything has an "open ↗" link. **Decided against vendored PDF.js** — PDF.js
      must `fetch` the PDF bytes, which is blocked from `file://`, whereas the native
      `<iframe>` PDF viewer renders local PDFs in Chromium; also zero dependency.
- [x] `file://` vs embedded-server decision: **static folder, open the entry page in a
      Chromium browser (Edge/Chrome) — no server, no install.** Everything is
      file://-safe: no `fetch` anywhere (search index is a script tag; nav/links are
      plain `<a>`; PDFs use the native viewer). Embedded-server fallback dropped as
      unnecessary (YAGNI).

**Success criteria:** `docwiz build samples/stockroom-demo` produces a navigable offline
handover that a colleague can open and browse without instructions. ✔ Met 2026-06-10.

## Stage 2 — Audience views engine (complete)

- [x] View loader (`views/*.yaml`) + validation (dangling includes fail `docwiz check`)
- [x] Transclusion resolver (chapter / `#anchor` granularity via AST spans; heading-line
      attribute leak covered by a regression test)
- [x] Audience filtering: matching `::: audience` blocks unwrap into the flow,
      non-matching blocks are removed (manual keeps everything, tagged)
- [x] `mode: document` rendering (filtered sections + "open in manual" source links)
- [x] `mode: slides` rendering (auto title slide, per-slide source attribution,
      arrow-key/space nav, fullscreen, hash deep links)
- [x] Audience switcher tabs in the shell on every page
- [x] External HTML documentation packages integrated as-is (ADR-006) — verified
      against a real multi-hundred-file export

**Success criteria:** the user-training view of the demo project works as a
presentation, and editing the source chapter changes the slides on rebuild.
✔ Met 2026-06-10 — slides transclude at build time; nothing is copied.

## Stage 3 — Editor app MVP (functionally complete)

- [x] `DocWiz.App` + `docwiz edit <project> [--port n] [--no-browser]`: localhost
      server, chapter tree, frontmatter form (id immutable per ADR-005, unknown
      frontmatter keys preserved on save), edit/save chapters
- [x] Live editing surface (ADR-007): the one surface IS the rendered page; ProseMirror
      view layer over Markdig-segmented blocks; byte-surgical saves; fidelity harness
- [x] Image insertion → uploaded to `assets/` (name de-duplication); paste/drop lands in
      `assets/` with a subfolder picker
- [x] Link insertion UI: `dw:` chapter/anchor picker, audience-block inserter, anchor
      helper, attachment link dialog
- [x] Chapter templates per discipline (setup/operations/troubleshooting/reference/
      integration/generic); new-chapter flow with section auto-numbering
- [x] Reading view rendered by the real pipeline, same CSS as the handover
- [x] `docwiz check`/`build` from the editor; built handover served at /handover
- [x] Zero-terminal launch: DocWiz.exe is a WinExe (no console window); no-args /
      pathless `edit` opens a native folder picker, offers to scaffold a project into
      an empty folder; Quit button in the UI stops the server
- [x] Welcome screen: recent projects history (%APPDATA%\DocWiz\recent.json), Open
      project (server-side native picker), New blank project, Start from template —
      built-in `blank` and `software-handbook` scaffolds plus a file-based template
      library (real on-disk folders under `Templates\`)
- [x] Native shell: the editor runs inside a DocWiz.exe-owned WebView2 window with a
      custom title bar; startup splash; ReadyToRun publish for fast cold start
- [x] Table/picture context tools (one shared definition), in-cell picture editing,
      drag-to-resize for pictures, tables, and linked blocks; L/C/R block alignment
- [x] Light/dark theme for the editor and the slide canvas; content-only zoom
- [x] Publish as self-contained single exe; smoke test on a clean machine
      (stakeholder verified `dist\DocWiz.exe` runs with no .NET runtime installed)

**Success criteria:** a non-programmer colleague edits a chapter, inserts a picture and
a link, and rebuilds — without help and without seeing raw Markdown.

## Stage 4 — View manager + handover polish (complete)

- [x] View CRUD in the editor (`/api/view`); visual view editor with title/audience/mode,
      ordered slide cards, up/down/remove, "+ New view" in the tree; view duplication
- [x] Slides v2: structured layouts (section-header, title-content, two-column,
      big-media), live on-canvas slide authoring, inline `::: embed` linked blocks with
      detach/relink, draft-from-manual, auto-pagination of too-tall slides
- [x] Anchor auto-creation when a section is first used in a view (`/api/ensure-anchor`)
- [x] Single-file HTML export (ADR-003): `SingleFileExporter` inlines CSS/JS and
      base64-embeds images into one self-contained file
- [x] `docwiz build --zip` packaging; clean handover output root (one project-named
      html + `_site/`)
- [x] External documentation packages: `external:` is a map of named packages; each
      non-null path is embedded under `external/<key>/` with its own tab; validator
      checks folder + entry page; editor "Documentation packages" manager
- [x] Handover focus view (fullscreen lightbox for figures and tables)
- [x] Content element library (`:::` callouts, steps, cards, buttons, details,
      center/right, group)

**Success criteria:** a colleague builds a training view from existing chapters in the
editor and presents it from the handover. ✔ Met.

## Stage 5 — Pilot on a real customer project (in progress)

- [x] Word/PDF migration pipeline: `docwiz import-docx` (bundled Pandoc + DocWiz
      post-processing), editor **Import…** with section selection and a review worklist,
      **New project from Word**; EMF/WMF → PNG conversion on import and on demand
- [x] First real migration: several Word sources → a validated project with 0 errors
- [x] User-training view template shipped inside the file-based template library
- [x] PDF export (manual + views) via native Chromium print; per-chapter selection and a
      PDF | single-file HTML format toggle
- [x] Content-only zoom
- [x] DocWiz Guide template (`templates/projects/docwiz-guide`): the user manual and tutorial,
      written in DocWiz and offered on the welcome screen; demo of every feature; its
      screenshots and PDF card regenerate with `tools/guide/refresh-guide-assets.ps1`
- [ ] Deliver a training from the user view to a real audience
- [ ] A non-programmer colleague edits a chapter unaided; collect feedback
- [ ] Release hygiene: tag v1.0, prune merged branches

**Success criteria:** the pilot project is handed over as a DocWiz package; the training
is delivered from the generated view; feedback from a non-programmer author is collected.

## Stage 6 — Editor redesign (approved 2026-10-10; all steps done, awaiting sign-off)

Source: a user-side UX audit of the editor (38 findings: layout, structure, audiences,
outputs, settings, trust) plus a CSS consistency count. The design system stays; the work
is structure, consolidation, and fixing what breaks trust.

**Decisions (stakeholder, 2026-10-10):**

- **Sections keep their folders, and the folder name is the section name** — `020-Instalace a síť`
  (number, then the exact name; accents, spaces, capitals allowed; the Windows-forbidden
  characters `\ / : * ? " < > |` are refused with a message). Renaming, deleting, and moving a
  section act on the real folder. Legacy slug names (`020-setup`) keep working. Removing folders
  altogether stays a possible later decision.
- **Chapter files follow their title** (same rule); links are unaffected because they use the
  chapter `id` (ADR-005).
- **Audiences are first-class, down to single blocks**: a Readers picker per chapter and per
  block, quiet margin markers, "Preview as" an audience (supersedes VISION.md's plan to demote
  audience tagging).
- **Autosave**, shipped together with **history** — never before it.
- **History** lives in `.docwiz/history/` inside the project (git-ignored), is kept forever, and
  stores each snapshot as a complete copy of one file, so deleting any snapshot or the whole
  folder breaks nothing. Size and "Clean up older than…" in Settings.
- **Sidebar tabs**: Manual · Outputs · Files; settings becomes a sheet, not a third column.
- **"Discipline" leaves the interface**; New chapter offers "Start from: Blank / Procedure /
  Troubleshooting / Reference". Existing `discipline:` frontmatter is still read.
- **Light theme by default**, following Windows dark mode.

**Steps:**

- [x] 1. Trust fixes (no design decisions needed): Source edits lost when switching back to
      Edit; new chapters always tagged user/support/admin; skeletons hardcode
      `::: audience admin`; deleting an audience leaves stale tags; raw-YAML success shown as an
      error; unsaved settings discarded silently; image size "undefined"; Insert element OK does
      nothing; document-mode views show slide controls; slide-editor messages invisible; package
      type silently replaced; toolbar scrolls sideways on laptops; settings rail height; tooltip
      over menus and Esc; Build-label and switch-knob contrast; browser `confirm()`/`prompt()`/
      `alert()` replaced by DocWiz dialogs with action-named buttons
- [x] 2. Structure you can edit: section and chapter rename/move/delete/drag in the tree acting on
      the real folders and files; New chapter in the tree; chapter title = its H1; "Start from"
      replaces discipline; every tree change undoable with Ctrl+Z / Ctrl+Y in one order with
      the page's own edits (added after the stakeholder's first look)
- [x] 3. History, then autosave: snapshots, per-chapter timeline with highlighted changes, restore
      (itself undoable), named project versions, Recently deleted, size + clean-up; then autosave
      of chapters and outputs; an All chapters timeline beside This chapter and Project (added
      after the stakeholder's first look)
- [x] 4. Shell and document bar: sidebar tabs, document bar (section / title, Readers, status,
      Edit · Source · Read, properties, ⋯), properties panel, settings sheet, one primary per
      screen, light default
- [x] 5. Audiences: Readers picker (chapter + block), manager with descriptions and usage counts,
      margin markers, Preview as
- [x] 6. Outputs editor: shared header, thumbnail rail, insert anywhere, change layout later,
      document-mode wording
- [x] 7. Consolidation pass: one recipe each for buttons, inputs, menus, badges, segmented
      controls; one icon set at one stroke; three control heights; a z-index scale — verified
      with the visual probe in both themes; then a toolbar design pass (added after the
      stakeholder's review, designed and approved on a Claude Design canvas): fewer unexplained
      icons, words where they matter, one row at 1280px, scroll bars in the theme

**Success criteria:** a non-programmer renames and reorders sections and chapters, tags a
paragraph for one audience and previews it as that audience, and undoes a mistake from history —
all without help, without a browser dialog, and without seeing a file path or an id.

## Backlog

Open items (everything shipped is in Recent Changes):

- Ctrl+K focuses the full-text search box
- Light theme: the Tip callout label (`--dw-ok` on the `--dw-ok-bg` tint over the page) is
  4.13:1, below AA 4.5:1 — found by the visual probe once it composited translucent backgrounds
- Slide display on wide monitors: show the whole slide rather than stretching
- Images inside note/warning/danger callouts
- Items library sidebar (assets, attachments, reusable elements)
- Adjustable slide preset layouts
- Subchapter display in the handover navigation
- "White to alpha" picture button
- Free positioning of linked blocks on slides (free-canvas stage)
- Multi-language projects and a broader template library (vNext)

## Recent Changes

| Date | Stage | Description |
|------|-------|-------------|
| 2026-10-10 | Stage 5 | Visual probe: text contrast is measured against the background actually painted — translucent `background-color`s (the 8–13% callout tints, the table stripes) are composited over the ancestors' backgrounds down to the first opaque one (the page), instead of the first non-transparent ancestor being taken as solid. Every note/tip/warning/danger callout used to FAIL (the label at 1:1 against its own solid accent colour) in both themes, on the handover and the edit surface; `color-mix()` results (computed as `color(srgb …)`) are read too, so the stripes are no longer skipped. Verified on the DocWiz Guide's Building blocks page (handover and edit surface): dark PASS; light reports one real finding, the Tip label at 4.13:1 (in the Backlog). Every stockroom-demo handover page PASSes in both themes (previously the security summary's danger callout failed) |
| 2026-10-10 | Stage 5 | DocWiz Guide template (`templates/projects/docwiz-guide`, on the welcome screen next to the Software Handbook): the user manual and tutorial for DocWiz, written in DocWiz — 25 chapters in six sections (Getting started with a fifteen-minute tour, Writing chapters, Organizing the manual, Outputs and the handover, Administration, Help), each built with the feature it explains; three audiences (Author, Trainer, Administrator) with tagged blocks and a tagged step; four outputs using every slide layout, sized linked blocks and groups (Getting Started and Training with DocWiz decks, Quick Reference and Administrator Guide documents); a printable quick-reference PDF and a practice Word file as attachments; a Command Line documentation package. 15 screenshots of the real editor and handover, captured headlessly — `tools/guide/refresh-guide-assets.ps1` (a small CDP client in `tools/guide/cdp.ps1`) regenerates them and the PDF on a throw-away copy. `ShippedTemplatesTests` checks every template in `templates/projects` (zero issues, every linked section resolves) |
| 2026-10-10 | Stage 6 | Toolbar design pass (designed on a Claude Design canvas, approved by the stakeholder): the formatting toolbar is one row at a 1280px window — Undo · Redo, Normal text ▾, the text marks, the lists and Quote, one **Align ▾** menu (the button shows the current alignment), **Link ▾** (web address, chapter or section, attachment, copy link to this section), **Picture**, **Table**, **Insert ▾** (picture, table, building block, group, special character, divider — each with a one-line hint) and **Readers**, the inserts and Readers with their names; the words fold away on a narrow window and groups wrap whole; the anchor button and the heading-anchor chip are gone (the Link menu copies a section's link). What can be inserted is one list (`editor2/inserts.js`) shared with the slide zones' slash menu (it said "Image" / "Linked element"). Readers uses the people icon everywhere (the eye is Preview as). The context bar is tinted so it reads as tools for the selection, every selection gets the same badge (PICTURE, TABLE, a callout's name …), groups carry their words (Size, Place, Rows, Columns, Align text — the right-click menu's words), paragraph above / below are icons instead of ¶↑ ¶↓, and **⋯** opens the same menu as right-clicking (where Copy link, Auto column widths and Focus now live, named); the capability audit counts a menu's items. The document bar: Read sits beside Edit · Source (it opens a tab), History and Properties carry their names, and a long title is cut short instead of wrapping the bar. The slide editor's toolbar spans the whole column above the slide list and the canvas, so it is one row (it wrapped to two) and has no Readers. The page title gets the handover's top spacing under the bars. Scroll bars and form controls follow the theme (`color-scheme` in tokens.css — they were light in dark mode, in the editor and the handover). Menus opened from a toolbar are real buttons: arrow keys move, Esc returns to the button |
| 2026-10-10 | Stage 6 | History › All chapters: every chapter's saved changes in one list, newest first by day (`GET /api/history/all`, sharing the per-chapter timeline code; the "before history started" entries left out), 50 at a time; a row opens its chapter and compares that version with now, and the list stays for going through changes one after another |
| 2026-10-10 | Stage 6 | Ctrl+Z for the sidebar: renaming, moving, adding and deleting sections and chapters (menus, drag, Properties, New chapter) records an undo step; Ctrl+Z / Ctrl+Y and the toolbar's Undo / Redo take sidebar changes and page edits newest first (compared by the page's undo depth), a toast names each change with an Undo link, a deleted chapter comes back from Recently deleted (and reopens if it was open). Renaming or moving the open chapter now updates it in place instead of reopening it, so its typing history survives; nothing undoes behind an open dialog. Also fixed: typing on a slide threw an error when refreshing the title slide's miniature |
| 2026-10-10 | Stage 6 | Preview as hides the chapter toolbar instead of fading it (the faded sticky bar let the page show through it); the title keeps its distance from the banner |
| 2026-10-10 | Stage 6 | Step 7 consolidation: a "Recipes" block at the top of app.css defines each concept once — buttons (secondary, primary, danger, sized, icon, chrome, toolbar, add, text link), fields, the menu container (action menus, the block-type list, the slash menu, the table picker), the CAPS label (13 former styles), the badge (8 former styles, variants set only colour) and icons; component rules keep only placement or a named variant; three control heights as tokens (`--dw-ctl-sm/md/lg` = 28/32/36px in tokens.css — every button, field, pill and segmented control in the chrome, bars, sidebar, rail and menus measured at one of them); a z-index scale (`--dw-z-*`, nine layers, no literal left); the last two segmented controls (window mode, appearance) and the welcome screen's toggle became `.dw-seg`; one icon stroke (non-scaling 1.5px for both icon grids) and the leftover glyph icons (× ⧉ ↔ ⇥ • ▣ ↗) replaced by the icon set; literal radii and 8px corners moved to the radius tokens; DESIGN_SYSTEM documents the recipes (stale toolbelt / slide-card / splitter entries removed) and the design skill gains rule 22 |
| 2026-10-10 | Stage 6 | Step 6 outputs editor: the output bar follows the chapter's document bar — "Slide deck / Document" and an inline title, a "For User" pill (single-choice audience menu with descriptions), save state, Slides · Document, Present (deck) or Read (document), ⋯ with Duplicate…, Export as one HTML file and Delete output… (the form row of fields and five buttons is gone; no primary button); the rail shows real miniatures of the slides (cloned from the canvas, drawn by site.css; the automatic title slide first, numbers matching the presentation) or a document's sections, refreshes after edits, reorders by drag, adds a slide or section between any two (the + in a gap), and its ⋯ moves, deletes or changes a slide's layout later (content of a region the new layout lacks moves into its first free region; a section header asks before dropping content); documents say "section" throughout and add sections without a layout gallery; "view" becomes "output" in the dialogs (New output: title, for, kind — no ID field); the visual probe skips rail miniatures when it looks for the slide canvas |
| 2026-10-10 | Stage 6 | Step 5 audiences: one Readers picker for a chapter (the document bar's pill) and for a block (the toolbar's Readers button, or a tagged block's tools) — Everyone, the audiences with a one-line description, a filter box from 7 audiences, Manage audiences…; the block picker tags the paragraph, step or element at the cursor (wraps it in `::: audience`, retags, or unwraps for Everyone), and a single step can be tagged — filtered outputs drop a step left empty and renumber; tagged blocks carry a quiet margin marker "Only for User, Support" (audience titles, from the one pipeline when the project is known; the editor's node view uses the same words — AudienceTagParityTests) instead of a filled box, in the editor and the handover; Settings › Audiences lists name, "who is this?" (new optional `description:` in docwiz.yaml) and usage ("Used by 5 chapters · 3 tagged blocks · 1 output"), reorders by drag or arrow keys, and no longer shows ids; Preview as an audience from the chrome bar dims the chapters it doesn't get, hides other readers' blocks (and steps left empty), pauses editing, and explains itself in a banner; the demo sample gains audience descriptions and a tagged step |
| 2026-10-10 | Stage 6 | Step 4 shell and document bar: the chrome bar keeps only the project menu (the project's name: switch project, open folder, import Word, export, save a copy, save as template, convert Word images, the build date), Check and Build handover — the "More" cluster and the build stamp are gone; the sidebar has Manual · Outputs · Files tabs (outputs grouped into slide decks and documents with their audience, documentation packages under "Bundled with the handover"; Files lists pictures and attachments — clicking one opens the Insert dialog with it already picked) and Project settings at its foot; the document bar is Section / Title · a Readers pill (checkable menu, "Manage audiences…") · save state · Edit · Source · Read · History · Properties · ⋯ (the tree's chapter menu) — the ID chip, Save button and audience toggles are gone and the toolbar fits one row; a Properties panel shows the section (change it to move the chapter), readers, where the chapter is used (outputs with slide numbers, chapters linking to it — the delete guard's finder), its link id (copy) and file; Settings is a sheet over the editor (Esc/×/backdrop close, unsaved changes ask first, the "Coming soon" entries are gone); one primary per screen (output and package screens lose theirs); Appearance offers Follow Windows · Light · Dark and follows Windows live |
| 2026-10-10 | Stage 6 | Step 3 autosave: chapter and output edits save two seconds after typing stops, and before switching chapters, outputs or screens and when the window loses focus — no "unsaved changes" dialog outside Settings; waits while Source mode, a table cell or an IME composition is active; saves of one editor queue instead of overlapping; edits typed while a save is in flight stay pending and are never rebuilt over; a save keeps the editor's undo history (only block attributes refresh); a save finishing after a chapter switch leaves the new chapter alone; the status line shows "Saving…" / "Saved HH:MM"; autosaves of outputs skip the Check pass |
| 2026-10-10 | Stage 6 | Step 3 history: every editor write of a chapter, output or the manifest is kept in `.docwiz/history` inside the project (git-ignored, kept until cleaned up, each snapshot a complete copy of one file); the History panel shows a chapter's timeline by day, compares any version with now (added blocks green, removed red), restores it (itself undoable); named project versions (mark, restore the whole project, delete); Recently deleted restores deleted chapters and outputs; Settings › History shows the size and cleans up older entries (named versions are kept); a successful Build offers to mark a version |
| 2026-10-10 | Stage 6 | Step 2 structure you can edit: the folder name is the section name (accents, spaces, capitals; legacy slug folders keep their titles) and chapter files follow their title; the tree renames sections and chapters inline (double-click or ⋯), adds sections and chapters (+), moves chapters by drag-and-drop or "Move to section", reorders and deletes (empty sections only) — all on the real folders and files; empty sections show in the tree; the chapter title is its H1 (save derives it, rename rewrites it); "Start from" replaces discipline in New chapter (no ID field — ids derive from the title, accented letters folded); the chapter bar shows Section / Title instead of a title field; fidelity harness runs headless Edge on its own profile |
| 2026-10-10 | Stage 6 | Step 1 trust fixes: Source edits are kept when switching back to Edit; new chapters default to the project's audiences and skeletons only carry an admin note for admin-tagged chapters; deleting an audience strips its chapter tags, reports blocks, and is refused while a view uses it; every browser `confirm()`/`prompt()`/`alert()` in the editor replaced by DocWiz dialogs with action-named (danger) buttons, and failed create dialogs keep their input; unsaved settings are guarded and "Back to editor" returns to the open chapter; raw-YAML save reports success; toolbar wraps instead of scrolling on laptops; settings rail fills the window; tooltips no longer cover menus and Esc closes the chapter menu; Build label and settings switch meet contrast (`--dw-mark-text`/`--dw-mark-strong`); image size "Icon" label; Insert element without a dead OK; document views lose slide controls; slide-editor messages visible; package replacement announced; view saves open Check only on errors; menu-label and Quit-button CSS collisions fixed |
| 2026-10-10 | Stage 6 | Editor redesign approved after a user-side UX audit: decisions recorded (folder name = section name, chapter files follow titles, first-class block-level audiences, history + autosave, sidebar tabs, discipline retired from the UI, light default) and seven steps defined |
| 2026-10-10 | Tooling | Claude desktop app setup: `.claude/launch.json` runs the editor on the Stockroom demo (port 5230) for the in-app browser preview; `.claude/settings.json` permission rules made repo-relative so they work on any clone path and in session worktrees; `.claude/worktrees/` and `.claude/settings.local.json` git-ignored |
| 2026-09-17 | Release hygiene | Sample project replaced by the Stockroom demo (user / support / admin audiences; setup / operations / troubleshooting / reference / integration disciplines; `api_reference` / `release_notes` / `integration_guide` external packages); template library reset to the Software Handbook template; consumed planning docs removed; docs consolidated |
| 2026-07-08 | Stage 5 | Chapter export + single-file HTML + print code-wrap fix: Export modal gains per-chapter selection and a PDF/HTML format toggle; printed code blocks wrap instead of baking a scrollbar |
| 2026-06-30 | Stage 5 | External-doc tabs open inside a DocWiz back-strip wrapper; `_blank` links fixed in the native window |
| 2026-06-30 | Quality | Codebase review + remediation (single-source/parity findings dispositioned) |
| 2026-06-29 | Stage 5 | Content-only zoom shipped (second V1.0 feature) |
| 2026-06-29 | Stage 4 | External doc-package tabs prefer a navigable `handover.html` entry over `index.html` |
| 2026-06-29 | Stage 5 | PDF export engine pivoted to native Chromium print (`Page.printToPDF`) |
| 2026-06-26 | Stage 5 | PDF export (manual + views) shipped (first V1.0 feature) |
| 2026-06-26 | Stage 4 | Handover output root cleaned up: one project-named html + `_site/` |
| 2026-06-25 | Stage 3 | Editor top-bar overflow fixed on small screens ("⋯ More" dropdown); handover focus view; light theme for slides + editor light/dark toggle; sidebar heading outline to 3 levels |
| 2026-06-24 | Stage 3 | Project settings UI + Documentation packages manager complete |
| 2026-06-23 | Stage 5 | `:::group` first real use in the template's training deck; view-link validation gap closed |
| 2026-06-22 | Stage 5 | Embeddable group container (`:::group`); blockquote style; new-slide title field; maximized-window DPI fix |
| 2026-06-22 | Workflow | Coding discipline (simplicity first, surgical changes) added to CLAUDE.md |
| 2026-06-20 | Stage 5 | User-training view template shipped; slide auto-pagination; whole-chapter linking |
| 2026-06-18 | Backlog | Markdown-native editing tools: task lists, indent/outdent |
| 2026-06-17 | Backlog | Table/picture contextual-toolbar redesign (one shared definition + right-click menus); divergence audit remediated (ADR-008); EMF/WMF → PNG support; editing-robustness fixes |
| 2026-06-16 | Stage 3 | Native shell (WebView2 window + custom title bar); Word import shipped as an editor feature; file-based template library |
| 2026-06-15 | Stage 3 | Slides v2 merged; resize + alignment for tables and linked blocks; startup speed fix (ReadyToRun) |
| 2026-06-14 | Stage 3 | Native app window, distributable branded exe, startup splash |
| 2026-06-13 | Design | Editor redesign (tokens, welcome canvas, chrome header, chapter bar, toolbars); in-cell picture drag-resize |
| 2026-06-12 | Live editor | Live editor M1–M5 shipped and flipped on: ProseMirror view layer over pipeline HTML, byte-surgical saves, tables + images first-class, toolbar redesign |
| 2026-06-12 | Stage 5 | First real migration of Word sources into a validated project; migration pipeline approved |
| 2026-06-11 | Stage 4+ | Content element library; WYSIWYG view editor; external docs generalized to named packages; auto anchors; numbering scheme fixed; design pass applied |
| 2026-06-10 | Stage 0–4 | Project scaffolded; ADR-001..006 accepted; pipeline core, views engine, editor MVP, welcome screen, guided insert dialogs, native pickers, view manager, single-file export |
