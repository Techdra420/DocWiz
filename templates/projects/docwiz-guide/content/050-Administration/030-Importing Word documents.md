---
id: word-import
title: Importing Word documents
audiences:
- author
- admin
---

# Importing Word documents

Most projects don't start from nothing: there is an existing manual in Word. DocWiz imports it and splits it into chapters, so you can start from the text you already have.

## Decide what to import {#triage}

Only import what you will keep editing. A good rule:

- **Import** living text that your team writes and updates: procedures, descriptions, troubleshooting.
- **Attach** documents that stay as they are: supplier manuals, data sheets, certificates, signed reports. Attachments are shown in the handover just as they are — see [Links and attachments](dw:links#attachments).

## Import a Word file {#import}

::: steps
1. Click the project's name and choose **Import Word document…**.
2. Choose the **Word file** (*.docx*) and the section to **Add to** — or *— new section —* to create one.
3. DocWiz reads the document and lists the sections it found, with how many paragraphs, tables and pictures each holds. Tick the ones to import; parts that look like a cover page or table of contents are unticked for you.
4. Click **Import … sections**.
5. Choose **Walk me through** to set who reads each new chapter, or **Later** to keep them for everyone.
:::

Afterwards, a list shows anything that needs a human eye — complicated tables, pictures that couldn't be converted — with a link to the place in the chapter.

To start a brand-new project from a Word file, click **New project from Word…** on the welcome screen: DocWiz creates the project and goes straight to the import.

## What the import does for you {#what-it-does}

- Every **Heading 1** in the Word file becomes a chapter.
- Typed numbers such as *2.1* are removed from headings — DocWiz numbers them itself.
- Paragraphs starting with **NOTE:**, **TIP:**, **WARNING:**, **CAUTION:** or **DANGER:** become callouts.
- Pictures are stored in the project, in a folder per chapter.
- Every heading gets a permanent anchor, ready for links and slides.

## Try it {#practice}

This guide comes with a short practice file, a [coffee machine manual](../../attachments/practice/coffee-machine-manual.docx). Save it to your computer and import it into your copy of the guide, into a new section called *Practice*. It has typed chapter numbers, notes and warnings written as plain paragraphs, a table and a numbered procedure — you will see each turned into its DocWiz equivalent.

## Word pictures that browsers can't show {#word-pictures}

Drawings made in Word or Visio are often stored in old vector formats (EMF and WMF) that no browser can show. DocWiz converts them to PNG during the import. In a project imported before this existed, the project menu offers **Convert … Word images to PNG…**; the originals are kept, but not shipped.

::: audience admin
- The importer is the open-source converter Pandoc, which ships in the **pandoc** folder next to DocWiz.exe. Without it, DocWiz reports that Word import is not available on this computer.
- For documents that only exist as PDF, open them in Microsoft Word first (Word converts PDFs into editable documents) and import the result. Expect more tidying up than with an original Word file.
- On the command line: `docwiz import-docx <file.docx> <project> --section <folder>` imports every section of a document, and `docwiz convert-emf <project>` converts old Word pictures.
:::
