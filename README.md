# DocWiz

**One Markdown source of truth → audience-specific interactive documentation for
delivered software and systems.**

DocWiz turns a folder of Markdown chapters into a self-contained, offline, interactive
HTML handover — with generated views per audience (user training slides, support quick
reference, administrator guide with attachments and embedded external documentation
packages) — plus a Word-like editor app so non-programmers can author content without
touching raw Markdown.

No more rebuilding training slide decks from the PDF manual by hand.

## Project Status

**Current Stage:** Stage 5 — pilot on a real customer project. Stages 1, 2, and 4 are
complete; Stage 3 (editor MVP) is functionally complete — the live editor shipped (the one
live surface IS the page) and DocWiz publishes as a self-contained single-file
`dist\DocWiz.exe` verified on a clean machine.

**V1.0:** both agreed V1.0 features are **built and merged to `main`**:

- **PDF & HTML export** — `docwiz export-pdf <project> [-o <dir>] [--all]` plus an editor
  "Export PDF or HTML…" action (the project menu → dialog with native folder picker, default
  `<project>/output/Export/`). Pick the **full manual, individual chapters (grouped by section),
  and/or each view**, and a **format**: **PDF** or a **single self-contained HTML file** (CSS/JS
  inlined, images base64 — HTML mode needs no Edge). It runs as a non-blocking background job with a
  completion toast. Chapters export as one combined file in book order; the manual is A4 portrait
  with a cover, "Page X of Y" footers, PDF bookmarks, and a page break per chapter; slides views are
  16:9 (one page per slide); document views portrait, with each title printed once; attached PDFs (which single-file HTML
  can't embed) become a "see attachment" note. The engine is another rendering of the ONE Markdig
  pipeline (ADR-004/008) — a shared `ChapterDocumentAssembler` feeds both the native Chromium print
  path (`Page.printToPDF`, paginates in C++ so it stays fast at any scale) and the single-file HTML
  exporter.
- **Content-only zoom** — Ctrl+wheel / Ctrl +/-/0 reflow-zoom only the document content (chapter
  edit surface, reading view, document-mode views, handover article; 50%–300%, transient %-badge,
  persisted) while the chrome stays fixed-pixel; slides excluded. One shared `site.js` module for
  both surfaces (Rule 21).

Remaining for the v1.0 tag: close the pilot loop (deliver a training from the user view, have a
non-programmer colleague edit unaided, collect feedback) + release hygiene (tag, prune). See
[docs/ROADMAP.md](docs/ROADMAP.md).

**Stage 6 — editor redesign** was approved on 2026-10-10 after a user-side UX audit and runs
alongside the pilot: trust fixes first, then an editable structure (folder name = section name),
history + autosave, a calmer shell, first-class audiences, the outputs editor, and a
consolidation pass — followed by a toolbar design pass (one-row formatting toolbar with Link,
Insert and Align menus; named groups and a ⋯ menu in the context bars; scroll bars in the
theme). Decisions and steps are in [docs/ROADMAP.md](docs/ROADMAP.md).

| Stage | Scope | Status |
|-------|-------|--------|
| 0 | Vision, content model, architecture/ADRs, sample project, viewer mockup | Complete |
| 1 | Build pipeline core: `docwiz build` → offline handover (manual, search, PDFs) | Complete |
| 2 | Audience views engine: transclusion, filtering, slides & document modes | Complete |
| 3 | Editor app MVP: live-surface chapters, images, links, templates, single exe | Complete (single exe verified on a clean machine) |
| 4 | View manager in editor, single-file export, packaging | Complete |
| 5 | Pilot on a real customer project (incl. Word import: editor Import… + New project from Word) | In progress (pilot loop) |
| 6 | Editor redesign: trust fixes, editable structure, history + autosave, shell, audiences, outputs, consolidation, toolbar design pass | Steps 1–7 done (plus the toolbar design pass); awaiting the stakeholder's check of the success criteria |
| — | Live editor: one surface that IS the page (M0–M5 ✓ — flipped 2026-06-12 after stakeholder sign-off; ADRs in ARCHITECTURE.md) | Shipped |

## Start here

1. [docs/VISION.md](docs/VISION.md) — the problem and the idea
2. [docs/CONTENT_MODEL.md](docs/CONTENT_MODEL.md) — how a DocWiz project is structured
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — components and key decisions (ADRs)
4. [docs/ROADMAP.md](docs/ROADMAP.md) — stages, checklists, success criteria
5. [samples/stockroom-demo/](samples/stockroom-demo/) — a small example DocWiz project
   (a fictional inventory application with user / support / admin views)
6. [templates/projects/docwiz-guide/](templates/projects/docwiz-guide/) — the **DocWiz Guide**:
   the user manual and tutorial, written in DocWiz. Open it from the welcome screen
   (Start from template › DocWiz Guide); every chapter uses the feature it explains

## Quick start

```powershell
# once: publish the CLI as dist/DocWiz.exe
dotnet publish src/DocWiz.Cli -c Release -o dist

# start your own documentation project (optionally from a built-in skeleton)
.\dist\DocWiz.exe new c:\path\to\my-project
.\dist\DocWiz.exe new c:\path\to\my-project --template software-handbook

# author it in the editor app — or just double-click DocWiz.exe:
# a welcome screen opens with your recent projects, Open project, New blank
# project, and Start from template — real on-disk folders under Templates\.
# Click a template to copy it into a new project (the master is untouched), or
# Edit it to maintain the master. From a project: Save as… / Save as template.
# No terminal involved. Stop it with Quit.
.\dist\DocWiz.exe edit c:\path\to\my-project

# or work file-based in any text editor
.\dist\DocWiz.exe check c:\path\to\my-project
.\dist\DocWiz.exe build c:\path\to\my-project -o c:\path\to\my-project\output\handover
# then open the project-named .html in .../output/handover/ (the only file there; pages live in _site/)

# migrate existing Word manuals (experimental; needs pandoc — see tools/README.md)
.\dist\DocWiz.exe import-docx c:\docs\manual.docx c:\path\to\my-project --section 020-setup

# convert a project's Word vector images (EMF/WMF) to PNG so they display
# (originals kept in _emf-originals/); also runs automatically during import
.\dist\DocWiz.exe convert-emf c:\path\to\my-project

# export the manual (and all views with --all) as PDF (needs Edge; output/PDF_Export/)
.\dist\DocWiz.exe export-pdf c:\path\to\my-project --all
```

The editor opens each chapter as **the real page** (the live surface — same
stylesheet as the handover): click into text and type; click a table or image and
its tools appear in a fixed context bar; saves are byte-surgical (untouched lines
keep their exact bytes); changes save by themselves and every save is kept in History. Around
it: a sidebar with **Manual · Outputs · Files** tabs (the chapter tree — numbered like the
handover, renamed, reordered and moved in place; the slide decks and documents; the pictures
and attachments, a click puts one into the page), a document bar (Section / Title, who reads
it, save state, Edit · Source · Read, History, Properties), a toolbar with guided insert
dialogs for images / elements / chapter links / audience blocks / attachments (image paste
lands in `assets/` automatically), "Start from" chapter templates, a visual output editor
(slide miniatures to reorder, add between or re-layout; duplication for template-driven
trainings), a Read view rendered by the exact build
pipeline, Readers on any chapter, paragraph or step with **Preview as** an audience to check
the result, Project settings as a sheet, and Check / Build handover (the built handover is
served at `/handover`). The project's name opens the project menu. **Import Word
document…** turns a Word `.docx` into chapters: pick which sections to
bring in, then optionally set who reads each chapter — the converter
(bundled Pandoc + DocWiz post-processing) reports anything that needs a human in a
deep-linked review worklist. Word vector images (EMF/WMF, which no browser can render)
are converted to PNG automatically on import; for projects imported before this, a
**Convert N Word images to PNG…** project-menu action (or `docwiz convert-emf <project>`) does the same
on demand — originals are moved to a kept-but-unshipped `_emf-originals/` folder.
Authoring rules live in [docs/CONTENT_MODEL.md](docs/CONTENT_MODEL.md);
[samples/stockroom-demo/](samples/stockroom-demo/) is the development fixture, and the DocWiz
Guide template is the user manual that demonstrates every feature.

## Repository layout

```
docs/        vision, architecture, content model, roadmap (governance)
samples/     example DocWiz content projects used for development and tests
src/         DocWiz.Core, DocWiz.Build, DocWiz.App (editor), DocWiz.Cli
templates/   handover site templates (embedded into DocWiz.Build) + project templates
tests/       unit tests
tools/       authoring-time conversion tools and the fidelity/probe harness
output/      generated handovers — git-ignored
dist/        published DocWiz.exe — git-ignored
```
