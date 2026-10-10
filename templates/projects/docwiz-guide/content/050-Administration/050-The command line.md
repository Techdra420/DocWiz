---
id: command-line
title: The command line
audiences:
- admin
---

# The command line

Everything an author does with buttons, DocWiz.exe can also do from a terminal: check a project, build the handover, export PDFs, import Word files. That makes it easy to build handovers in a script or on a build server.

This chapter shows the everyday recipes. Every command and option is listed in the **Command Line** tab of the handover.

## The commands {#commands}

{#command-table widths="32% 68%" caption="DocWiz commands"}
| Command | What it does |
|---------|--------------|
| `edit [<project>]` | Opens the editor — the same as double-clicking DocWiz.exe |
| `new <folder>` | Creates a new, blank project |
| `check <project>` | Checks the project and lists errors and warnings |
| `build <project>` | Builds the handover |
| `export-pdf <project>` | Exports the manual (and every output, with `--all`) as PDF |
| `export-view <project> <output-id>` | Exports one output as a single HTML file |
| `draft-view <project> <chapter-id>` | Drafts a slide deck from one chapter |
| `import-docx <file> <project> --section <name>` | Imports a Word document into a section |
| `convert-emf <project>` | Converts old Word pictures (EMF, WMF) to PNG |

Any command DocWiz doesn't know prints the full list with every option — for example `.\DocWiz.exe help | Out-Host`.

## Recipes {#recipes}

Check a project, and stop a script when there are errors:

```powershell
.\DocWiz.exe check "D:\Docs\Coffee machine" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "The documentation has errors" }
```

Build the handover into a folder of your choice and pack it as a zip:

```powershell
.\DocWiz.exe build "D:\Docs\Coffee machine" -o "D:\Handover\Coffee machine" --zip | Out-Host
```

Export the manual and every output as PDF:

```powershell
.\DocWiz.exe export-pdf "D:\Docs\Coffee machine" -o "D:\Handover\PDF" --all | Out-Host
```

Import a Word manual into a new section:

```powershell
.\DocWiz.exe import-docx "D:\Old\Manual.docx" "D:\Docs\Coffee machine" --section "020-Imported" | Out-Host
```

## Good to know {#good-to-know}

- **Pipe the output in scripts.** DocWiz.exe is a Windows program, so a terminal does not always wait for it to finish. Piping its output — `| Out-Host` in PowerShell — makes the script wait and sets `$LASTEXITCODE`.
- **Options go in the order shown.** Write `build <project> -o <folder> --zip`, not `--zip -o <folder>`; options in another order print the list of commands instead.
- **Exit codes:** `0` means success, `1` means errors (for `check` and `build`: validation errors; warnings alone still give `0`), and `2` means the command was not understood.
- **`build` refuses to build a project with errors**, and it deletes the output folder before writing the new handover.
- **`export-pdf` needs Microsoft Edge** and writes to the project's `output\PDF_Export` folder unless you give `-o`.
- **`new`** only knows the built-in `blank` and `software-handbook` outlines (`--template software-handbook`); the templates on the welcome screen are used from the editor.
