---
id: project-folder
title: The project folder
audiences:
- admin
---

# The project folder

Authors never need to look inside a project folder — the editor does everything. This chapter is for administrators who keep projects in git, write scripts around them, or want to fix something by hand. Every file in a project is plain text, except pictures and attachments.

## What is where {#layout}

```text
DocWiz Guide\
├── docwiz.yaml       project settings
├── content\          the manual, a folder per section
│   ├── 010-Getting started\
│   │   ├── 010-Welcome to DocWiz.md
│   │   └── 020-Your first fifteen minutes.md
│   └── 020-Writing chapters\
├── views\            slide decks and documents
├── assets\           pictures
├── attachments\      documents shipped as they are
├── external\         documentation packages
├── .docwiz\          history, never shipped
└── output\           handovers and exports
```

- **Sections** are folders under `content`, **chapters** are files in them. The number in front of each name sets the order; the rest is the name shown in the manual. The editor renames and renumbers them for you.
- **Links never depend on file or folder names.** They use each chapter's ID, so renaming and moving is always safe.
- `.docwiz` and `output` can be deleted at any time; DocWiz recreates what it needs.

## A chapter file {#chapter-file}

Each chapter is a Markdown file that starts with a short header:

```markdown
---
id: cleaning-the-scanner
title: Cleaning the scanner
audiences:
- author
- trainer
---

# Cleaning the scanner

## Before you start {#before-you-start}

Switch the scanner off.
```

{#frontmatter-keys widths="22% 78%" caption="The chapter header"}
| Key | Meaning |
|-----|---------|
| `id` | The chapter's permanent link ID. **Never change it** — links, slides and documents point at it. |
| `title` | The chapter's title; kept equal to the `#` heading by the editor. |
| `audiences` | Who the chapter is for. |

Other keys are kept as they are when the editor saves.

## The text format {#text-format}

Chapters use CommonMark, the standard flavour of Markdown, plus a few additions. The most useful ones:

{#syntax-table widths="38% 62%" caption="Text format at a glance"}
| Write | For |
|-------|-----|
| `## Heading {#anchor}` | A heading with a permanent anchor |
| `[text](dw:chapter-id#anchor)` | A link to a chapter or a section |
| `[text](../../attachments/file.pdf)` | A link to an attachment |
| `![Caption](../../assets/file.png){width=640}` | A picture, 640 pixels wide |
| `{.img-center}` `{.img-right}` `{.no-caption}` | Picture placement; hiding the caption |
| `{widths="30% 70%" caption="Title"}` | Above a table: column widths and caption |
| `{width=60% align=center}` | Above a table: a narrower, centered table |
| `::: note` … `:::` | A callout — also `tip`, `warning`, `danger` |
| `::: warning Hot surface` | A callout with its own label |
| `::: steps`, `::: cards`, `::: buttons` | Procedure steps, navigation cards, a button row |
| `::: details Title` | A collapsible section |
| `::: center`, `::: right` | Centered or right-aligned content |
| `::: group Name {#id desc="note"}` | A named group of blocks |
| `::: audience admin trainer` | Content for some audiences only |
| `- [ ] task` | A checklist item |
| `<br>` | A line break, also inside a table cell |

A block inside another block needs a longer fence on the outside — `::::` around `:::`:

```markdown
:::: steps
1. Switch the station on.
2. ::: audience admin
   Check the overnight sync.
   :::
3. Sign in.
::::
```

HTML tags other than `<br>` are shown as plain text, so a manual can safely mention `<24V>`.

## An output file {#output-file}

Slide decks and documents are files in the `views` folder:

```yaml
id: operator-training
title: Operator Training
audience: user
mode: slides          # or: document
slides:
  - layout: section-header
    title: Operator Training
    subtitle: Daily use of the coffee machine
  - layout: title-content
    title: Making a coffee
    regions:
      body: |
        ::: embed dw:daily-use#making-a-coffee
        :::
  - layout: two-column
    title: Do and don't
    regions:
      left: |
        - Fill the tank before you start
      right: |
        ::: embed dw:cleaning#rinse width=60% align=center
        :::
```

The layouts are `title-content` (area `body`), `two-column` (`left`, `right`), `big-media` (`media`) and `section-header` (no areas). `::: embed` shows a chapter, a section, a group, a table or a figure, and follows every change to it.

## Editing files by hand {#by-hand}

Any text editor works. Afterwards, run **Check** (or `docwiz check`) — it finds broken links, unknown audiences, missing pictures and misspelled block names. If the editor has the chapter open, reopen it there before you continue editing.
