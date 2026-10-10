---
id: links
title: Links and attachments
audiences:
- author
- trainer
---

# Links and attachments

Links tie a manual together: from a procedure to the settings it needs, from a symptom to its fix. DocWiz links between chapters keep working when chapters are renamed or moved, and attachments travel with the handover.

## Add a link {#add-a-link}

Select the words that should become the link — or just click where it should go — and open **Link** in the toolbar:

{widths="32% 68%" caption="The Link menu"}
| Choice | Links to |
|--------|----------|
| Web address… | A page on the internet or your intranet |
| Chapter or section… | Another chapter of this manual, a section of it, or a group |
| Attachment… | A PDF or other document that ships with the handover |
| Copy link to this section | Copies the link to the heading you are in, to paste somewhere else |

For a chapter link, pick the **Chapter** (listed by section), then optionally a **Section** of it, and check the **Link text** — it is filled in for you. For example: the section about [captions in tables](dw:tables#captions), the [Text, headings and lists](dw:text) chapter, or the [group of steps and checks](dw:building-blocks#group-example).

A web link works the same, with the address instead — for example the [CommonMark specification](https://commonmark.org), the text format behind every DocWiz chapter.

## Links that survive renaming {#stable-links}

Each chapter has a link ID that never changes, and each heading gets a permanent anchor the first time the chapter is saved. Links point at those, not at titles or file names, so renaming a chapter, retitling a heading or moving a chapter to another section never breaks a link.

If a link does break — say, the section it points to was deleted — **Check** reports it before you build. Deleting a chapter that other chapters or outputs still link to is refused, so you can't break links by accident.

## Change or remove a link {#change-a-link}

Click a link in the page. The context bar offers:

- **Open chapter →** (or **Open ↗** for a web address) to follow it;
- **Change…** to edit where it goes and its text;
- **Remove link** to turn it back into plain text.

You can also edit a link's text directly in the page.

## Copy and paste links {#copy-links}

Every heading, picture, table and group has its own link:

- for a heading, click into it and choose **Link** › **Copy link to this section**;
- for a picture or a table, open its **⋯** menu and choose **Copy link to this picture** or **Copy link to this table**;
- for a chapter, open **Properties** in the document bar and click **Copy link**.

Paste the copied link into the page and DocWiz asks what text readers should see.

## Attachments {#attachments}

Attachments are documents handed over as they are: data sheets, certificates, signed reports, release notes. They are not converted — the handover shows PDFs right inside the browser and offers other files for download.

To link to one, choose **Link** › **Attachment…**. Pick a file from the list, or click **Upload new** to add one from your computer (optionally **Into folder**, for example *datasheets*). This guide comes with two:

- the [printable quick reference](../../attachments/quick-reference.pdf), a PDF made with DocWiz's own export;
- a [practice Word file](../../attachments/practice/coffee-machine-manual.docx) for trying out [Word import](dw:word-import).

To manage attachments — upload several, or delete one — open the **Files** tab in the sidebar and click **Manage attachments…**. In the handover, every attachment is listed on the **Attachments** tab, grouped by folder.

::: tip Convert or attach?
Only turn a document into chapters if you will keep editing it. Supplier manuals, data sheets and certificates are better as attachments: they stay exactly as their author made them.
:::
