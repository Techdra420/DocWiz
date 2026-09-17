# DocWiz — Architecture

> Governance document. Do not modify without the human stakeholder's explicit approval.
> Status: **ACCEPTED** — ADR-001..006 approved 2026-06-10; ADR-007 approved 2026-06-12;
> ADR-008 approved 2026-06-17.

---

## Goals

1. **Single source of truth** — Markdown chapters; every output is generated, nothing duplicated.
2. **Audience views** — user / support / administrator outputs from the same source.
3. **Non-programmer authoring** — a Word-like editor app; authors never see raw Markdown,
   git, or a terminal.
4. **Offline, self-contained handover** — runs from a folder on any machine with a browser;
   no internet, no CDN, no installation at the customer.
5. **Integrations** — PDF attachments rendered in-browser; external HTML documentation
   packages (e.g. a generated API reference) embedded as their own tabs.

## Non-goals

Hosting, accounts, databases, real-time collaboration, content versioning UI (the file
system / git handles versioning), authoring the external documentation packages
themselves (DocWiz integrates their output).

## System overview

```
┌─────────────────────────┐     reads/writes      ┌──────────────────────────┐
│ DocWiz.App               │◄────────────────────►│ DocWiz project (folder)   │
│ local web app (editor)   │                       │ docwiz.yaml content/      │
│ • chapter tree           │                       │ views/ assets/ attachments│
│ • WYSIWYG Markdown editor│                       └────────────┬─────────────┘
│ • templates              │                                    │
│ • view manager           │                                    │ docwiz build
│ • live preview           │                                    ▼
└────────────┬────────────┘                       ┌──────────────────────────┐
             │ hosts UI for                       │ output/handover/          │
             └─────────────────────────────────── │ index.html + assets       │
                                                  │ full manual + views       │
                                                  │ pdf viewer, search,       │
                                                  │ embedded doc packages     │
                                                  └──────────────────────────┘
```

## Components (planned solution layout)

| Project | Purpose |
|---------|---------|
| `DocWiz.Core` | Content model: manifest, chapter, view; Markdown pipeline (Markdig + custom extensions: audience containers, `dw:` links, includes); project loader/validator. No UI, no I/O policy. |
| `DocWiz.Build` | Handover generator: resolves views/transclusions/audience filters, renders templates, copies assets, emits `output/handover/`. Independently re-runnable. |
| `DocWiz.App` | Local editor: ASP.NET Core minimal API serving the editor UI (vendored JS, no Node build chain) with file APIs (load/save chapter, asset upload, view CRUD, build trigger, preview). |
| `DocWiz.Cli` | `docwiz new | build | edit | check` — automation and CI entry point. |
| `templates/` | Chapter templates per discipline + handover HTML/CSS/JS templates. |

## Key decisions (ADRs)

| # | Decision | Rationale | Status |
|---|----------|-----------|--------|
| ADR-001 | **C# / .NET 8+ (LTS), published as a self-contained single exe** | Packaged Python (PyInstaller/Nuitka) was considered and would also give users a zero-install app; C# won on corporate AV/EDR friction (PyInstaller bundles are the classic false-positive source; a self-contained .NET exe is a boring binary to IT) and team maintainability (the team's language is C#). Markdig is a first-class Markdown engine. Trade-off accepted: an earlier Python/Jinja site-builder design carries over, not its code. | Accepted 2026-06-10 |
| ADR-002 | **Editor UI = browser app served by the local exe; WYSIWYG via a vendored open-source Markdown editor (candidate: Toast UI Editor, MIT)** | Word-like editing of Markdown is a solved problem; do not build an editor from scratch. Browser UI avoids WPF/Electron weight. Vendored = works offline, no npm toolchain in the repo. Candidate must be validated in Stage 3 spike (image paste, tables, custom containers). | Accepted 2026-06-10; **superseded in part by ADR-007** — the live editing surface is a vendored ProseMirror view layer; Toast UI was evaluated and removed. |
| ADR-003 | **Handover = static folder (`index.html` + assets); optional single-file export later** | Embedding multi-MB PDF drawings base64 into one HTML balloons it past practical limits. A folder is still a single zip to hand over. Single-file export stays on the roadmap for small/training-only packages. | Accepted 2026-06-10 |
| ADR-004 (amended) | **Markdown = CommonMark + pinned extension set; one Markdig pipeline is the single parser and renderer of record** | Authors get one guaranteed flavor. DocWizMarkdown.CreatePipeline() remains the only permitted pipeline construction; builder, Reading view, and the editor-document segmentation all consume it. Amendment (2026-06-12, with ADR-007): the live editing surface displays pipeline-rendered HTML ingested into an editor view layer; the view layer is not a Markdown parser and never re-renders ship output. "Preview == output" is enforced by: (1) the surface being built from pipeline HTML, (2) every save re-rendering the saved bytes through the pipeline back onto the surface, (3) the on-demand Reading view rendering pure pipeline output, and (4) screenshot-parity gates between the editing surface and the built handover. Any visual divergence between the editing surface and pipeline output is, by definition, a bug. | Accepted 2026-06-10; amended 2026-06-12 |
| ADR-005 | **References by stable ID, never by heading text or file path** | People rename headings and files constantly; transclusion must survive it. IDs are immutable after creation; the editor enforces this. | Accepted 2026-06-10 |
| ADR-006 | **External HTML documentation packages integrated as-is** (copied into the handover, iframed/linked from their own tab) | A generated documentation site is already self-contained; re-rendering its content would duplicate a pipeline that exists. | Accepted 2026-06-10 |
| ADR-007 | **Live editing surface = vendored ProseMirror view layer over a Markdig-segmented document; Markdig remains the only Markdown parser; saves are byte-surgical** | The chapter editor renders one editable surface that is visually the handover page (same tokens/site.css, same generated markup). The server segments each chapter body via the one Markdig pipeline into gap/block segments with byte spans and per-block pipeline HTML; the browser builds a ProseMirror document from that HTML (ProseMirror is vendored as plain upstream ESM + import map — no bundler, no Node, offline). No Markdown is ever parsed in the browser. On save, untouched segments are written back as byte copies; only edited blocks (tables: edited rows) are re-emitted by DocWiz's own serializer under a versioned normalization whitelist (docs/fidelity/NORMALIZATION.md), guarded by a base-hash conflict check. Content the schema cannot model round-trips as opaque raw blocks (source-editable, never rewritten). A fidelity harness (segmentation partition invariants, serializer idempotence over the full pilot + sample corpus, semantic identity via Markdig re-render) gates every release. Alternatives evaluated and rejected: Milkdown/TipTap/Vditor (engine-owned second parser + normalizing serializers break round-trip fidelity and ADR-004), extending Toast UI (dormant since 2024; demonstrated serialization corruption), CKEditor 5 (GPL/commercial licensing + Markdown-as-conversion-layer). | Accepted 2026-06-12 |
| ADR-008 | **No parallel implementations: one definition derived everywhere, or a parity gate (generalizes ADR-004 beyond the Markdig pipeline)** | A user-facing concept (toolbar / menu / badge / label) or a domain rule (serialization grammar, numbering, slug, link/anchor/asset-path handling, frontmatter emission) that must exist in 2+ places — especially across the JS editor (`wwwroot`) and the C# Core/Build, or across sibling node-types — MUST have ONE source of truth the others derive from, OR a parity test/gate that fails on drift. A "keep in sync" / "mirrors X" code comment is explicitly NOT an acceptable parity mechanism; new code that adds a second copy without one of those is a defect regardless of current agreement. Motivated by the 2026-06-17 divergence audit, which found the same drift shape in ~10 places (two picture toolbars drifted on Rotate/badge; 4-way chapter-frontmatter emission; the NATIVE/modeled/Classify block-type triple synced only by comment; ~7-site asset-path rewriting). Enforcement reuses existing infrastructure: vocabulary/label parity via the embedded-asset xunit pattern, byte/grammar parity via the fidelity harness + run-edit-scenario.ps1, visual parity via the probe's compareEditVsShip. | Accepted 2026-06-17 |



## Design language

Follow `docs/DESIGN_SYSTEM.md`: design tokens as the only color source, light/dark via a
`data-theme` attribute with no-flash inline script, sober engineering aesthetic — no
gradients, photos-as-decoration, glow, or emoji; tabular numerals; identifiers in mono.
DocWiz defines its own `--dw-*` token file; the handover and the editor UI share it.

## Constraints

- **Fully offline.** No CDN links anywhere — corporate and customer machines may have
  no internet. All JS/CSS/fonts vendored or system.
- **Windows-first** (authors' machines), but nothing platform-specific in Core/Build.
- **The handover must work from `file://`** where feasible; where browser security
  makes that impractical (search index, PDF.js), the package ships with a tiny
  `start.bat`/embedded static server fallback — decision detail for Stage 1.
- Plain-text source files only in `content/` and `views/` — diff-able, merge-able.

## Coding standards

Nullable enabled, warnings as errors, no NuGet package without a
justification comment in the `.csproj`, exceptions logged and rethrown as domain
exceptions — never swallowed. Builder stages must be independently re-runnable.
