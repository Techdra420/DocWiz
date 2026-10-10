---
id: templates
title: Project templates
audiences:
- author
- admin
---

# Project templates

A template is a complete DocWiz project that serves as the starting point for new ones: its sections, chapter outlines, slide decks and documents are ready, so a new project starts with the right structure and only the content changes. This guide is a template; so is the *Software Handbook*.

## Start a project from a template {#use-a-template}

On the welcome screen, under **Start from template**, click a template and pick an empty folder. DocWiz copies the template into the folder and opens the copy. The template itself is never changed by what you do in the copy.

## Maintain a template {#edit-a-template}

Next to each template on the welcome screen is an **Edit** button. It opens the template itself — the header then shows *Editing template* — and every change is saved straight into it. Every project started from it afterwards gets the change; projects started earlier keep their own copy.

## Make a template from a project {#save-as-template}

When a project has a structure worth reusing — a training deck that works, a good set of chapter outlines — click the project's name and choose **Save as template…**. Give it a **Name** and, optionally, a **Description** of what it is for. The template appears on the welcome screen from then on.

::: tip Build the training once
The training deck barely changes from one customer to the next; only the manual does. Build the deck once in a template, with its slides linked to the template's chapters. Every new project then starts with a training that fills itself as the manual is written.
:::

::: audience admin
Templates are ordinary project folders inside the **Templates** folder next to DocWiz.exe. To share templates across a team, copy template folders into each colleague's Templates folder. A file called `template.yaml` in the folder can set the name and description shown on the welcome screen, and `order` to sort the templates:

```yaml
name: 'DocWiz Guide'
description: 'The DocWiz manual, written in DocWiz.'
order: 1
```

A template that cannot be opened is shown as *Needs fixing*, with the reason; use **Edit** to repair it. Copies made from a template leave out its history and its built handovers.
:::
