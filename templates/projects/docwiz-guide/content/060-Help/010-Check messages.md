---
id: check-messages
title: Check messages
audiences:
- author
- trainer
- admin
---

# Check messages

**Check** reads the whole project and reports what might go wrong in the handover. *Errors* stop **Build handover** until they are fixed; *warnings* don't, but each one is something a reader would notice. Every message names the chapter or output it is about.

## Common messages {#common-messages}

{#message-table widths="40% 60%" caption="Check messages and what to do"}
| The message says | What to do |
|------------------|------------|
| link …: no chapter with id … | A link points at a chapter that no longer exists. Open the chapter named in the message, click the link and choose **Change…**, or **Remove link**. |
| link …: chapter … has no anchor … | The link points at a section that was deleted or never existed. Link to another section, or to the whole chapter. |
| include …: no chapter with id … | A slide or document section shows a chapter that no longer exists. Open the output and use **Change source** on the linked section, or delete the slide. |
| image … not found in assets — placeholder will be rendered | The picture file is missing. Insert the picture again, or use **Replace…** on the placeholder. |
| attachment … not found in attachments | The attached file is missing. Upload it again in **Files** › **Manage attachments…**, or remove the link. |
| heading … starts with a typed number | Remove the number from the heading — DocWiz numbers headings itself. |
| include …: chapter … is not tagged for audience … | An output shows a chapter that isn't meant for its audience. Add the audience to the chapter's readers, or show something else on the slide. |
| frontmatter 'audiences' is empty | Nobody is set as a reader of the chapter. Choose its readers in the document bar. |
| layout … region … is empty | A slide has an empty area. Add content, choose a layout with fewer areas, or delete the slide. |
| a group has no `{#id}` | A group can't be shown on slides yet. Open the chapter in the editor; it adds the missing ID when it saves. |
| unknown container … | A building block's name is misspelled in the chapter's text. Open **Source** and correct it. |
| table widths attribute has … value(s) for … column(s) | The column widths no longer match the columns. Choose **Auto column widths** in the table's **⋯** menu, then drag the widths again if you need them. |

## When something looks wrong {#problems}

::: details A picture shows "image not available yet"
The picture file is missing from the project — usually because the chapter was copied without its pictures. Click the placeholder and use **Replace…** to pick or upload the picture again. **Check** lists every missing picture.
:::

::: details "Not saved: the chapter changed on disk"
Someone or something changed the chapter's file after you opened it — a colleague, a sync tool, or a text editor. DocWiz won't overwrite their change. Open the chapter again from the sidebar and redo your last edit.
:::

::: details Numbers appear twice in a heading, like "3.1 3.1 Cleaning"
The number was typed into the heading, often by a Word import of an old document. Delete the typed number; DocWiz adds the right one by itself.
:::

::: details A slide is split into several "(cont.)" slides
The slide has more content than fits. Show a smaller section of the chapter, make a group with just the essentials, or split the content over two slides yourself.
:::

::: details "Word import is not available on this machine"
The **pandoc** folder next to DocWiz.exe is missing. Copy the complete DocWiz folder again, not just DocWiz.exe.
:::

::: details "Microsoft Edge was not found" when exporting a PDF
PDF export uses Microsoft Edge. Ask your IT department to install or repair Edge, or export a **Single HTML file** instead and print that from your browser.
:::

::: details The welcome screen shows no templates
DocWiz looks for templates in the **Templates** folder next to DocWiz.exe. If DocWiz sits in a folder you can't write to, such as *Program Files*, move the whole DocWiz folder somewhere you can — see [Installing and running DocWiz](dw:install#setup).
:::
