---
name: docwiz-design
description: Use when styling, theming, or adding/restyling ANY UI in DocWiz's two web surfaces — the generated handover site (templates/site/) or the editor app (src/DocWiz.App/wwwroot/). Covers the --dw-* token contract, type/space scales, light/dark theming, component recipes, and the live-editing-surface rules. Read before editing tokens.css, site.css, app.css, base.html/slides.html, or adding a new page/component.
---

# DocWiz — design skill

The authoritative rationale and component recipes live in **`docs/DESIGN_SYSTEM.md`**
(produced by a dedicated design pass). This skill is the operational checklist; if it
conflicts with DESIGN_SYSTEM.md, that doc wins.

## Where things live

- **`templates/site/tokens.css`** — the ONE token file (colors, type scale `--dw-fs-*`,
  spacing `--dw-s1..s8`, radii `--dw-r0/r1/r2`, shadows, focus ring). Embedded into
  DocWiz.Build; the editor links the same file via the `/app/tokens.css` route —
  never duplicate palette values in app.css. Tertiary text uses `--dw-faint` (one
  step below `--dw-muted`; paths/timestamps/eyebrow labels only, never interactive-label
  or body text). The dark chrome has `--dw-chrome-2` (raised chips) and `--dw-mark`
  (brand fill / Build button) — both chrome-only, never on light surfaces.
- **Handover** = `templates/site/` (base.html, slides.html, site.css, search.js,
  slides.js). **Editor** = `src/DocWiz.App/wwwroot/` (index.html, app.css, app.js).
  The editor preview iframe inlines tokens.css + site.css — content styling changes
  apply to both automatically.
- Theme = `data-theme` on `<html>` (no-flash script + `_dw/site.js` toggle). The
  chrome `<header>` and `.slides-surface` carry their own `data-theme="dark"` so
  tokens inside them resolve dark in both page themes.
- Slides are authored at **1920×1080** and scaled via transform (`slides.js` `fit()`);
  slide type uses `--dw-slide-*` tokens.
- The chapter editor is the live surface (`#editor2.dw-live-surface`, vendored
  ProseMirror under `wwwroot/vendor/prosemirror/` — never patch vendored files);
  see rule 20 for what app.css may and may not style there.
- Generated-content hooks: `.aud`/`.aud-tag`, `.dw-figure`+`figcaption`,
  `.img-placeholder`, `.sr-item`, `.slide`/`.src`, `#tooltip`, `#errToast`,
  `#issuesPanel` `.sev`, and the `:::` element library (`.dw-callout` +
  `dw-note/tip/warning/danger`, `.dw-steps`, `.dw-cards`, `.dw-buttons`,
  `.dw-details`) — names registered in `DocWizContainerRenderer.KnownContainers`
  (in `src/DocWiz.Core/Markdown/AudienceContainerRenderer.cs`). Reuse these; don't invent
  parallel ones.

## The 22 rules (1–20 from the design pass; 21 from the 2026-06-17 divergence audit; 22 from Stage 6)

1. No color literals outside tokens.css — every color is `var(--dw-*)`. (Sole
   exception: `@media print` may force `#000`/`#fff`.)
2. No webfonts, CDNs, icon fonts, or external assets. `--dw-font-ui` /
   `--dw-font-mono` only. Inline SVG is allowed.
3. Plain CSS with custom properties. No preprocessors, no Tailwind, no frameworks.
4. Never rename an existing `--dw-*` token or component class; extend only.
5. Font sizes come from `--dw-fs-*` (slides: `--dw-slide-*`). Nothing below 12px
   anywhere; nothing below 20px inside `.slide`.
6. Spacing uses `--dw-s1..s8` only — no magic pixel values for padding/margin/gap.
7. Radii: only `--dw-r0/r1/r2`. Shadows: only `--dw-shadow`/`--dw-shadow-2`.
8. Exactly one primary (solid accent) button per view. Destructive actions use the
   danger outline recipe — never solid red at rest, never a primary that destroys.
9. Every focusable element has `:focus-visible { box-shadow: var(--dw-focus-ring); }`;
   never remove an outline without replacing it.
10. Text contrast ≥ 4.5:1 in BOTH themes — check dark too; if a pair isn't listed in
    DESIGN_SYSTEM.md §2, verify before shipping.
11. Disabled = `opacity: .45` + `cursor: default` — never a new gray.
12. ALL-CAPS labels (`.grp`, `.aud-tag`, `.slide-kind`, table `thead th`, `.sev`)
    always pair `text-transform: uppercase` with
    `letter-spacing: var(--dw-track-caps)` and xs/sm size at weight 600.
13. Machine-flavored strings (IDs, file paths, versions, IPs, threshold values, project
    codes) render in `--dw-font-mono`.
14. Tables: CAPS muted header on `--dw-surface-2` with 2px `--dw-border-strong`
    bottom rule, horizontal hairlines only (no vertical rules), zebra rows, and a
    caption in `.dw-figure` below.
15. The chrome bar stays `--dw-chrome` (dark) in both themes and carries
    `data-theme="dark"` on its element; inside it use `--dw-chrome-text/-muted/-border`
    and the accent.
16. The slide **canvas** (`.slide-frame`) follows the page theme (light/dark toggle). The
    letterbox **surround** (`.slides-surface`) stays dark in both themes via the
    theme-independent `--dw-chrome`, and the controls on it (`.slide-bar`, `.hint`) use
    chrome tokens so they stay legible there. Slide controls live outside `.slide-frame`.
17. No emoji, no glassmorphism/blur, no photos as decoration, and no gradients except
    the single sanctioned welcome-hero wash (`--dw-accent-bg` → transparent,
    `body.welcome`).
18. Dark theme values come from the `[data-theme="dark"]` block — never derived by
    opacity tricks on light values; wells (`--dw-surface-2`) must be darker than
    cards in dark mode.
19. Hover states: surfaces use `--dw-surface-2`, accent fills use
    `--dw-accent-strong`, text-on-chrome brightens to `--dw-chrome-text`.
    No transform/scale effects.
20. The editing surface (`#editor2.dw-live-surface`) IS the handover page —
    site.css applies inside it via the @scope wrapper. app.css may style only
    editing CHROME (context bar, pills, grips, focus mode); content look belongs
    in site.css so editor and handover change together. Never let the surface
    clip the table breakout. (Toast UI was removed at the live-editor M5 flip.)
21. One definition per concept across node-types and surfaces (ADR-008). Picture / box /
    table tool strips render from a SHARED tools definition with explicit capability flags
    (`caption`/`rotate`/`link`/`delete`/`adjacency`), never hand-built per node-view; adding
    a control means adding it to the shared definition (or declaring the flag with a one-line
    reason). Context-bar badges and the `:::` container labels come from ONE map per concept.
    A context bar's group words come from contexttools' `CAP_LABEL` (bar and right-click menu);
    its ⋯ opens the same right-click menu (`buildMenu`). What can be inserted — the toolbar's
    Picture / Table / Insert ▾ and the slash menu — is ONE list, `editor2/inserts.js`.
    Never stand up a second toolbar / badge / label / vocabulary list kept in sync only by a
    comment (ADR-008 in `docs/ARCHITECTURE.md`).
22. (Stage 6 step 7) Editor chrome is built from the "Recipes" block at the top of app.css —
    buttons (secondary / primary / danger / sized / icon / chrome / toolbar / add / text
    link), fields, the menu container, the CAPS label, the badge, `.dw-seg`, and icons at one
    1.5px non-scaling stroke. A new component joins a recipe's selector list and adds only
    placement or a named colour variant. Every control is `--dw-ctl-sm` / `-md` / `-lg`
    (28 / 32 / 36px); z-index comes only from the `--dw-z-*` scale.

## Adding a component or page

1. Build from existing recipes (DESIGN_SYSTEM.md §5 and the Recipes block in app.css):
   buttons, fields, menus (`actionMenu()`), badges, CAPS labels, `.dw-seg`,
   `dialog#modal` + `openModal`/`modalWarn`, `.file-list` rows.
2. Wire buttons via `on(id, fn)` in app.js (defensive binding); scope group selectors
   (the `.toolbelt button[data-insert]` lesson). Every interactive element gets a
   `data-tip` written for non-programmers.
3. Verify: `dotnet build`, restart the editor. **The visual probe is the PRIMARY
   check — prefer it over screenshots.** It reports measurable facts (overlap,
   clipping, horizontal overflow, text contrast) that the eye judges unreliably, and
   it now runs on every surface: slides, the chapter live-edit surface, AND the built
   handover (`?probe=1` → `<pre id="dwProbeOut">` + a `PROBE-PASS/FAIL` title; helper
   `tools/fidelity/run-probe.ps1 -Project … (-Chapter <c> | -Handover)`). Its `verdict`
   must be `PASS`, or every `FAIL` individually triaged and consciously accepted
   (cluster repeats/decorative false-positives — don't trust blindly). Take a
   screenshot only as a SECONDARY aid when you need to see something the probe can't
   quantify (e.g. an icon looks wrong) — never as the sole evidence. Also check
   dump-dom for `data-js-error`. Handover changes: `docwiz build samples/stockroom-demo`,
   probe a built page, inspect BOTH themes (headless defaults to dark; light needs an
   interactive check or `--force-prefers-color-scheme=light` if supported).
