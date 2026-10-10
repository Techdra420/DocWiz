---
id: project-settings
title: Project settings
audiences:
- author
- admin
---

# Project settings

**Project settings**, at the bottom of the sidebar, holds what applies to the whole project: its name and version, its audiences, numbering, history, and how the editor looks on your computer. The settings open over the editor; **Esc**, the **×** or a click beside them closes them again.

Changes to the project's settings are kept when you click **Save** at the top. If you close the settings with unsaved changes, DocWiz asks first.

{#settings-pages widths="24% 76%" caption="The pages of Project settings"}
| Page | What you set there |
|------|--------------------|
| General | **Project name**, **Project code**, **Customer**, **Version** and **Language**. The project folder is shown for reference, with **Open folder**. |
| Audiences | Who reads the documentation — see [Readers and audiences](dw:readers#audiences). |
| Handover output | **Automatic numbering** of chapters and headings, on or off. |
| History | How much space history uses, and cleaning up old entries — see [Saving, undo and history](dw:history#clean-up). |
| Application | **Window mode** and **Appearance** — these belong to your computer, not the project, and are saved instantly. |
| Raw YAML | The settings file itself, for administrators. |

## Where the project details show up {#where-details-show}

- The **project name** heads the handover and names its main file and the exported PDFs.
- The **project code** appears under the name in the title bar, in the handover's header and in the footer of every slide.
- The **customer** appears on the handover's home page and on the cover of the PDF manual.
- The **version** appears in the handover's header and on the PDF cover. Use three numbers, such as *1.2.0*.

## Window mode and appearance {#application}

- **Window mode** — **DocWiz window** opens the editor in its own window; **Browser tab** opens it in your web browser instead. The change applies the next time you start DocWiz.
- **Appearance** — **Follow Windows** switches between light and dark together with Windows; **Light** and **Dark** fix one.

::: audience admin
**Raw YAML** edits `docwiz.yaml` directly. A mistake there can stop the project from opening, so prefer the other pages; if you do edit it, run **Check** afterwards.
:::
