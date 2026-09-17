# tools/ — authoring-time conversion tools

These tools support **migrating existing Word/PDF documents** into DocWiz
projects. Pandoc is now bundled into the published editor package at release (for the
**Import…** button); `pdf2docx.ps1` remains a developer-only aid. Nothing here ships in
the **customer handover**, and the offline rule for shipped artifacts is unaffected
(Pandoc runs only at authoring time, as a separate process).

## pandoc (docx → Markdown engine)

Word import (the editor's **Import…** button and `docwiz import-docx`) shells out to
[pandoc](https://pandoc.org). At **release** Pandoc is **bundled** into the published
package: the `BundlePandoc` MSBuild target copies `pandoc.exe` + its `COPYING`/`COPYRIGHT`
into `dist/pandoc/` (and `THIRD-PARTY-NOTICES.md` into `dist/`) on `dotnet publish`, so the
shipped `dist\DocWiz.exe` imports on a clean machine with nothing installed. Pandoc is
GPL-2.0-or-later (separate-process invocation = "mere aggregation"; see
`THIRD-PARTY-NOTICES.md`).

The fetch below is the **developer** path only — Pandoc is **not** committed to the repo
(binary, ~210 MB, GPL-2.0+). Fetch it once so dev (`dotnet run`) import and the publish
step work:

```powershell
# pinned version; no admin rights needed
$v = '3.6.3'
Invoke-WebRequest "https://github.com/jgm/pandoc/releases/download/$v/pandoc-$v-windows-x86_64.zip" `
  -OutFile "$env:TEMP\pandoc.zip"
Expand-Archive "$env:TEMP\pandoc.zip" -DestinationPath tools\pandoc
```

Discovery order (`DocxImporter.FindPandoc`): **bundled beside the exe** (`dist/pandoc/`)
first — pins behaviour so a different Pandoc on `PATH` can't change conversion output —
then `PATH`, then the dev `tools/pandoc/**` walk. The publish step **errors** if
`tools\pandoc\pandoc-3.6.3\pandoc.exe` is missing.

## pdf2docx.ps1 (PDF → docx bridge)

For sources that only exist as PDF. Drives the locally installed Microsoft
Word, whose PDF reflow conversion is the best fully-offline option available:

```powershell
.\tools\pdf2docx.ps1 -Path C:\Docs\my-project\_sources   # converts every PDF in the folder
.\tools\pdf2docx.ps1 -Path C:\Docs\manual.pdf             # or a single file
```

The resulting .docx then goes through the normal `docwiz import-docx`
pipeline. Expect more manual cleanup than with a native .docx source —
PDF→Word is a reconstruction, not a conversion.

## Migration workflow (see docs/CONTENT_MODEL.md)

1. **Triage**: only living prose becomes Markdown. Drawings, datasheets,
   certificates, supplier manuals go to `attachments/` unconverted — the
   handover embeds PDFs natively.
2. PDF-only sources: `pdf2docx.ps1` first.
3. **In the editor: Import…** (or "New project from Word…" on the welcome screen) —
   pick the `.docx`, choose which detected sections to bring in, then optionally walk
   through audiences/discipline per chapter. Or, headless: `docwiz import-docx
   <file.docx> <project> --section <folder>` imports every section. Both split at
   Heading 1, strip typed numbering, convert NOTE/WARNING paragraphs to callouts, route
   images into `assets/`, and produce a review report.
4. Work through the review worklist + Check results on the live editing surface.
