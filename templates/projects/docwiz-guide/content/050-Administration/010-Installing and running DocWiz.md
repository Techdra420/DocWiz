---
id: install
title: Installing and running DocWiz
audiences:
- admin
---

# Installing and running DocWiz

DocWiz is one program file, **DocWiz.exe**, with a few folders next to it. There is no installer and nothing to set up: copy the folder, double-click the program.

## What you need {#requirements}

{#requirements-table widths="30% 70%" caption="Requirements"}
| Item | Notes |
|------|-------|
| Windows 10 or 11 | DocWiz runs on Windows. |
| Microsoft Edge | Comes with Windows. DocWiz uses it to show its window and to make PDFs. |
| Disk space | About 400 MB for DocWiz and the Word importer, plus your projects. |
| Internet | Not needed. DocWiz never connects to the internet, and neither does the handover. |

No .NET or other runtime has to be installed — everything DocWiz needs is inside DocWiz.exe.

## Put DocWiz in place {#setup}

Copy the DocWiz folder to a place where you are allowed to write, for example `C:\Tools\DocWiz` or a folder in your documents. The folder holds:

- **DocWiz.exe** — the program;
- **Templates** — the project templates offered on the welcome screen, this guide among them (see [Project templates](dw:templates));
- **pandoc** — the converter used by **Import Word document…**.

::: warning Not in Program Files
DocWiz keeps its templates in the Templates folder next to the program. If that folder can't be written to — as in *Program Files* — DocWiz falls back to an empty templates folder in your user profile, and the welcome screen shows no templates.
:::

## Start and stop {#start-and-stop}

Double-click **DocWiz.exe**. The welcome screen offers **Open project…**, **New blank project…**, **New project from Word…**, the templates, and your recent projects.

The editor opens in its own window. To use your web browser instead, choose **Browser tab** under **Open the editor in** on the welcome screen, or in **Project settings** › **Application**; it applies from the next start.

To stop DocWiz, click the power button at the top right (or close the window). Everything is saved first; the page then confirms *Editor stopped*.

## Where DocWiz keeps things {#where-things-are}

Everything that belongs to a project is inside the project folder — chapters, outputs, pictures, attachments, history and built handovers. Copy the folder and you have copied the project.

Two small files belong to you rather than to a project, in `%APPDATA%\DocWiz`: `recent.json` (the recent projects on the welcome screen) and `settings.json` (the window mode). Deleting them only empties the recent list and resets the window mode. The light or dark appearance is remembered by the editor window itself.

## Sharing projects with colleagues {#sharing}

A DocWiz project is an ordinary folder, so share it the way you share other folders — on a file share, in a synchronized cloud folder, or in a git repository. Two rules keep everyone happy:

- One person edits a given chapter at a time. If a chapter changed on disk after you opened it, DocWiz refuses to save over it and asks you to reload.
- The `.docwiz` folder (history) and the `output` folder (built handovers and exports) belong to each person's working copy; they don't need to be shared or committed.

## Running several editors {#several-editors}

The editor runs a small local web server on port 5210, reachable only from your own computer. Starting DocWiz a second time while it is running simply opens the running editor. To work on two projects at once, start the second from the command line with another port, for example `DocWiz.exe edit D:\Docs\Second --port 5211`.
