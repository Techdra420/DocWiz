# Third-Party Notices — DocWiz authoring tool

> Status: **DRAFT** (2026-06-16) — pending stakeholder/legal review.
> Scope: this file covers third-party components distributed in the **DocWiz authoring
> tool** (the published `dist/` package: `DocWiz.exe` + bundled `pandoc/`). It does **not**
> cover the generated **handover** output, which ships none of these components (it is
> self-contained HTML/CSS/JS produced from project content).

DocWiz is distributed as an authoring tool. The notices
below are provided to satisfy the attribution and license obligations of the bundled
components. Each component is a separate work; DocWiz invokes Pandoc as a separate
process and does not incorporate or link it — see the Pandoc section.

---

## Pandoc — GPL-2.0-or-later (bundled binary)

- **Component:** Pandoc, a universal document converter.
- **Version shipped:** 3.6.3 (Windows x86-64).
- **Copyright:** © 2006–2024 John MacFarlane and contributors.
- **License:** GNU General Public License, version 2 **or (at your option) any later
  version** (GPL-2.0-or-later). Pandoc bundles some components under GPL-compatible
  permissive licenses (e.g. BSD-3-Clause); their individual notices are reproduced in
  the bundled `pandoc/COPYRIGHT` file.
- **License text:** shipped at `pandoc/COPYING` (the full GPL) and `pandoc/COPYRIGHT`
  (Pandoc's copyright + bundled-component notices), beside the executable in the
  DocWiz package.
- **Upstream / project home:** <https://pandoc.org>

### How DocWiz uses Pandoc (relationship statement)

DocWiz ships `pandoc.exe` as a **separate, unmodified executable** alongside `DocWiz.exe`
and invokes it as an independent process over its command-line interface (text in, text
out) during Word import. This is *mere aggregation* on a distribution medium: DocWiz is
**not** a derivative work of Pandoc, is not linked against it, and DocWiz's own code is
not placed under the GPL. The GPL obligations below apply to the **bundled Pandoc binary
only**.

### Written offer for corresponding source (GPL-2 §3)

The complete corresponding source code for the bundled Pandoc 3.6.3 binary is the
official tagged release:

- **Source:** <https://github.com/jgm/pandoc/releases/tag/3.6.3>
  (also the signed/tagged tree at <https://github.com/jgm/pandoc/tree/3.6.3>).

For a period of **at least three (3) years** from the date the DocWiz package containing
this binary was distributed, the distributor will, on request, provide a complete
machine-readable copy of the corresponding source for the exact Pandoc version shipped,
at no charge beyond the cost of physically performing the distribution.

> **Legal review note (to be resolved):** for a fully self-contained §3(a) distribution,
> mirror the Pandoc 3.6.3 source archive into the release artifacts and reference its
> path here, rather than relying on the upstream URL remaining reachable for three years.

---

## Other components shipped in the DocWiz tool

These are permissively licensed and either compiled into `DocWiz.exe` or vendored into
the editor UI. Full per-package license texts: NuGet/managed packages under their package
metadata; vendored browser modules keep a `LICENSE` file beside each module under
`src/DocWiz.App/wwwroot/vendor/`.

| Component | Version | License | Copyright / Notes |
|-----------|---------|---------|-------------------|
| Markdig | 0.37.0 | BSD-2-Clause | © Alexandre Mutel. The single Markdown engine (ADR-004). |
| YamlDotNet | 16.2.1 | MIT | © Antoine Aubry and contributors. Parses `docwiz.yaml`, frontmatter, views. |
| Microsoft.Web.WebView2 | 1.0.4022.49 | Microsoft Software License Terms (WebView2 SDK) | © Microsoft. Embeds the Edge/Chromium engine; the WebView2 Runtime ships with Windows and is not redistributed by DocWiz. |
| ProseMirror — `prosemirror-model` | 1.25.8 | MIT | © Marijn Haverbeke and contributors. Editor-only (live editing surface, ADR-007). |
| ProseMirror — `prosemirror-state` | 1.4.4 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-view` | 1.41.9 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-transform` | 1.12.0 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-commands` | 1.7.1 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-keymap` | 1.2.3 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-history` | 1.5.0 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-inputrules` | 1.5.1 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-dropcursor` | 1.8.2 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-gapcursor` | 1.4.1 | MIT | © Marijn Haverbeke and contributors. |
| ProseMirror — `prosemirror-schema-list` | 1.5.1 | MIT | © Marijn Haverbeke and contributors. |
| orderedmap | 2.1.1 | MIT | © Marijn Haverbeke. ProseMirror dependency. |
| w3c-keyname | 2.2.8 | MIT | © Marijn Haverbeke. ProseMirror dependency. |
| rope-sequence | 1.3.4 | MIT | © Marijn Haverbeke. ProseMirror dependency. |

> ProseMirror modules and WebView2 are part of the **editor** only. The generated
> handover contains none of them.

---

## Maintenance

When a bundled component's version changes, update its row (and, for Pandoc, the version,
the source URL, and the bundled `COPYING`/`COPYRIGHT`). Keep this file in sync with
`src/*/**.csproj` package versions and `src/DocWiz.App/wwwroot/vendor/VENDORED.md`.
