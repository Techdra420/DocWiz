# DOCWIZ — Agent Instructions

## Project Mission

Build DocWiz: a documentation platform for delivered software and systems. One Markdown
source of truth in a project folder; a builder that generates a self-contained offline
interactive HTML handover with audience-specific views (user training slides, support
quick reference, administrator guide with PDF attachments and embedded external
documentation packages); and a local Word-like editor app so non-programmers can author
the Markdown without ever seeing it.

---

## Mandatory Reading Before Any Work

1. **[README.md](README.md)** — current stage and stage table. Read first.
2. **[docs/ROADMAP.md](docs/ROADMAP.md)** — task checklists, success criteria, recent changes.
3. **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — components, ADRs, constraints, standards. Governance document.
4. **[docs/CONTENT_MODEL.md](docs/CONTENT_MODEL.md)** — the on-disk project contract (manifest, frontmatter, audience containers, views, stable IDs).
5. **[docs/VISION.md](docs/VISION.md)** — the why; read once for orientation.

---

## Project Skills

- **docwiz-design** (`.claude/skills/docwiz-design/`) — mandatory before styling or
  adding ANY UI in the handover site or the editor app (token contract, theming,
  component conventions, headless verification).

---

## Automatic Update Protocol (every completed task)

Update **both** `README.md` and `docs/ROADMAP.md` in the same commit:

- ROADMAP: tick `[x]`, update `**Current Stage:**` and `**Last Updated:**`, append a
  `Recent Changes` row (`| YYYY-MM-DD | Stage N | description |`).
- README: keep the `Current Stage` line and stage table truthful.

The human stakeholder tracks progress through these two files. Not optional.

---

## Workflow: Human Stakeholder ↔ AI Agents

| Who | Responsibility |
|-----|---------------|
| Human | Approves stage transitions and ADR changes; provides real project content and feedback from colleagues; confirms success criteria |
| AI Agent | Implements within the current stage; keeps ROADMAP/README current; flags blockers with `[BLOCKER]` prefix in commit messages |

---

## Guardrails

- **Never commit to `main`.** Always create a feature branch first (`git checkout -b
  <branch>`) and commit there — this includes spec/doc commits, not just code. `main`
  changes only via an explicit, human-approved merge.
- **Never `git push` without explicit human approval.** Commit locally, then wait.
- **No CDN or internet dependency in anything shipped** — editor UI and handover must
  work fully offline. Vendor JS/CSS into the repo with license + version noted.
- **Do not modify `docs/ARCHITECTURE.md` without explicit human approval** — governance document.
- Never commit `output/` (git-ignored).
- No NuGet package without a justification comment in the `.csproj`.
- Stable IDs are immutable (ADR-005) — no feature may rewrite an existing chapter `id`
  or anchor.
- The editor's preview must use the same Markdown pipeline as the builder — never a
  second renderer.
- **No parallel implementations of one concept (ADR-008).** When a user-facing concept
  (toolbar / menu / badge / label) or a domain rule (serialization grammar, numbering,
  slug, link/anchor/asset-path handling, frontmatter emission) must exist in 2+ places —
  especially across the JS editor (`wwwroot`) and the C# Core/Build, or across sibling
  node-types — make ONE the source of truth and derive the rest, or pin them with a
  parity test. A "keep in sync" / "mirrors X" comment is NOT an acceptable parity
  mechanism.
- Exceptions: log full message, rethrow as domain exception. Never silently swallow.
- If unsure whether a task is in the current stage's scope, check ROADMAP.md and ask
  the human rather than assuming.

---

## Coding Discipline

Two principles that fill real gaps; *think before coding* lives in **Critical Thinking &
Response Standards**, and *goal-driven execution* lives in the **Definition of Done**.
Tradeoff: these bias toward caution over speed; for trivial edits, use judgment.

- **Simplicity first.** The minimum code that solves the problem — nothing speculative.
  No features beyond what was asked, no abstraction for single-use code, no
  "flexibility"/configurability nobody requested, no error handling for impossible
  scenarios. If you wrote 200 lines and it could be 50, rewrite it. The test: would a
  senior engineer call this overcomplicated? (Reinforces ADR-008 — the simplest design
  usually has a single source per concept.)
- **Surgical changes.** Touch only what you must; clean up only your own mess. Don't
  "improve" adjacent code, comments, or formatting; don't refactor what isn't broken;
  match the surrounding style even if you'd do it differently. Spot unrelated dead code?
  Mention it — don't delete it. Remove only the imports/variables/functions YOUR change
  orphaned. The test: every changed line traces directly to the request.

---

## Definition of Done (Any Task)

1. `dotnet build DocWiz.sln` clean (warnings are errors) and `dotnet test` green
2. `docwiz check samples/stockroom-demo` reports 0 errors and `docwiz build samples/stockroom-demo`
   produces a working handover (open a generated page and look at it)
3. The task's ROADMAP success criteria demonstrably met
4. ROADMAP.md and README.md updated per the protocol above
5. No `TODO:`/`FIXME:` left in changed files
6. **When asking the stakeholder to run/test a change, always (re)publish the standalone
   exe first** — `dotnet publish src/DocWiz.Cli -c Release -r win-x64 --self-contained
   -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist`. This
   produces a self-contained single-file `dist\DocWiz.exe` (~150 MB) that runs on a clean
   machine with NO .NET runtime installed (Edge is the only external dep; it ships with
   Windows). The stakeholder always launches their usual `dist\DocWiz.exe` (double-click,
   zero-terminal); a `dotnet run` dev server or a stale `dist` does NOT count as testable.
   Don't say "ready to test" until `dist\DocWiz.exe` is freshly published with the change.
7. **See a web-surface change with your own eyes before the human does.** When a change
   touches a web surface (`templates/site/**` or `src/DocWiz.App/wwwroot/**`), `dotnet test`
   is not enough — it never sees layout, colour, overlap, clipping, or readability, and
   those are the bugs that cost this project the most time. YOU are the first verification
   loop, not the stakeholder. Before claiming it works or is "ready to test":
   - **PRIMARY: run the visual probe — prefer it over screenshots.** (`src/DocWiz.App/wwwroot/probe.js`,
     inert unless the URL carries `?probe=1`; dumps JSON into `<pre id="dwProbeOut">` + a
     `PROBE-PASS/FAIL` title; also `window.DocWizProbe.run()` / `runContent()`.) It reports
     rects, overlap, clipping, horizontal overflow, and text contrast — measurable facts the
     eye judges unreliably — and runs on **every** surface: slides, the chapter live-edit
     surface, AND the built handover. Helper: `tools/fidelity/run-probe.ps1 -Project <p>
     (-Chapter <relpath> | -Handover)`. For editor work probe **both** surfaces — the live
     WYSIWYG edit surface AND the read/handover render — and confirm they match. Its `verdict`
     must be `PASS`, or every `FAIL` individually triaged and consciously accepted (the raw
     contrast count includes repeats and the odd decorative false positive — cluster and
     judge, don't trust blindly).
   - **SECONDARY: screenshot only when the probe can't quantify it** (e.g. an icon or glyph
     looks wrong) — never as the sole evidence —
     `msedge --headless=new --disable-gpu --virtual-time-budget=20000 --screenshot=C:\tmp\x.png "http://127.0.0.1:<port>/?<...>#view=views/<file>"`.
   - `wwwroot` is **embedded into the assembly**, so a `wwwroot` edit needs a rebuild/restart
     before any headless run sees it — a running `dotnet run` server with a stale build keeps
     serving the old JS/CSS. Confirm the served file changed (`curl …/app/<file>`) if unsure.
8. **No new parallel implementation of an existing concept.** If the change touches something
   that already lives in both the editor (`wwwroot`) and the builder/Core — toolbars, the
   `:::` vocabulary/labels, attribute grammar, numbering, asset paths, frontmatter emission —
   confirm there is a single source or a parity gate before claiming done (ADR-008). Do not
   add another copy kept in sync only by a comment.

---

## Key Technical Facts

- **Target:** net8.0 everywhere; `Directory.Build.props` enforces nullable + warnings-as-errors.
- **Solution layout:**
  - `src/DocWiz.Core` — content model (`Models/`), project loader + frontmatter parsing
    (`Loading/`), validator (`Validation/`), and the Markdown pipeline (`Markdown/`).
  - `src/DocWiz.Build` — `HandoverBuilder` generates the static handover site. Site
    templates live in `templates/site/` and are compiled in as embedded resources.
  - `src/DocWiz.Cli` — `docwiz new <folder>`, `docwiz check <project>`,
    `docwiz build <project> [-o dir]`, `docwiz edit [<project>] [--port n] [--no-browser]`.
    `build` refuses to run when validation reports errors. The exe is a **WinExe on
    net8.0-windows** (folder picker on no-args, no console window — the stakeholder's
    zero-terminal requirement; do not change OutputType back); terminal output works
    via `ConsoleInterop.TryAttachParentConsole()`. Published exe:
    `dotnet publish src/DocWiz.Cli -c Release -o dist` (git-ignored).
  - `src/DocWiz.App` — the editor: localhost Kestrel server (`EditorServer`), UI embedded
    from `wwwroot/` (vendored ProseMirror — see `wwwroot/vendor/VENDORED.md`).
    Starts projectless → welcome screen (recents in `%APPDATA%\DocWiz\recent.json`,
    open via server-side native picker delegate, new from `ProjectTemplates` or the
    file-based template library under `templates/projects/`); the project is mounted at
    runtime, so project file routes are dynamic endpoints, not static-file mounts. Every
    project template must pass `docwiz check` with 0 errors. File APIs are guarded:
    chapters only under `content/**/*.md`, YAML only `docwiz.yaml`/`views/*.yaml`,
    chapter `id` never rewritten on save (ADR-005), unknown frontmatter keys preserved.
    The preview endpoint renders through `DocWizMarkdown` + `LinkRewriter` — the
    one-pipeline rule applies.
  - `tests/DocWiz.Core.Tests` — xunit.
- **The one-pipeline rule (ADR-004):** `DocWizMarkdown.CreatePipeline()` in
  `src/DocWiz.Core/Markdown/DocWizMarkdown.cs` is the only place a Markdig pipeline may
  be constructed. Parse and render with the same pipeline instance.
- **Dependencies:** Markdig and YamlDotNet (plus WebView2 for the native shell). Anything
  new needs a justification comment in the `.csproj` (see Guardrails).
- **Dev fixture:** `samples/stockroom-demo/` is the reference project used by development
  and the DoD checks. Its 2 missing-asset warnings are intentional (placeholder rendering);
  0 errors is the required state. Extend it whenever a new content-model feature lands.
- **Stable IDs (ADR-005):** chapter frontmatter `id` + heading `{#anchor}` attributes;
  `LinkRewriter` rewrites `dw:` links, `ProjectValidator` fails dangling ones.
- **`file://` constraint:** the generated site uses no `fetch()` and must stay that way.
- **Builds are destructive:** `HandoverBuilder.Build` deletes and rewrites the output
  directory. Default output: `output/handover/` (git-ignored).

---

## Critical Thinking & Response Standards

Never agree by default. Stress-test what the human says before validating it. No empty
praise — if something is good, say *why*, and lead with what is wrong or missing first.
Do not echo the human's framing; ask what they are not seeing and whether the
counter-argument is right.

For any significant idea or proposal:
1. Rate it 1–10 with reasoning for the specific number (a 9 must explain why not 10).
2. Argue the opposite position with the same rigor — strongest case against, no strawman.

Before implementing (not only when evaluating a proposal): state your assumptions
explicitly and ask when uncertain; if a request has multiple reasonable interpretations,
surface them instead of silently picking one; if something is unclear, stop, name what's
confusing, and ask before writing code.
