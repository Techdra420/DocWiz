# DocWiz Design System

Documentation platform for delivered software and systems. Two surfaces, one visual
language: the **handover site** (static, offline, light+dark) and the **editor app**
(localhost, light, theme-ready). Tone: sober technical documentation — credible in a
professional context, calm to read for hours, presentable on a meeting-room screen.

---

## 1. Principles (6)

1. **Tokens or nothing.** Every color, size, space, radius, and shadow comes from a `--dw-*`
   custom property. A literal hex in `site.css` or `app.css` is a bug.
2. **Reading is the product.** The manual column, the table, and the slide are the heroes;
   chrome recedes. When in doubt, remove decoration and add hierarchy.
3. **Character through typography, not color.** One navy accent, used sparingly for
   interaction. Identity comes from the type system: confident bold headings, ALL-CAPS
   tracked labels, mono for everything machine-flavored (IDs, paths, versions, IPs, threshold values).
4. **Dark is a designed pair, not a dimmed copy.** Same hierarchy logic, re-derived values:
   wells get *darker* than cards in dark mode (in light they get darker too — direction is
   consistent, values are independent).
5. **Hierarchy of action is visible at arm's length.** Exactly one primary button per view.
   Secondary is outlined, ghost is borderless, danger is reserved for destruction.
6. **States are part of the component.** No component recipe is done without
   default / hover / focus-visible / disabled. Focus is always `--dw-focus-ring`, never `outline: none` alone.

---

## 2. Color system

Full values live in `tokens.css`. Rationale and contrast notes:

### Neutrals (light)
| Token | Value | Role | Contrast |
|---|---|---|---|
| `--dw-bg` | `#f3f5f7` | page canvas | — |
| `--dw-surface` | `#ffffff` | cards, content column | — |
| `--dw-surface-2` | `#e9edf1` | wells: code, table heads, inputs | — |
| `--dw-text` | `#1a2530` | primary text | 14.9:1 on surface |
| `--dw-muted` | `#51626f` | secondary text | 6.3:1 on surface — AA at any size |
| `--dw-faint` | `#5f6d78` | tertiary text — paths, timestamps, eyebrow/group labels | 4.9:1 on `--dw-bg` — AA; never on `--dw-surface-2` |
| `--dw-border` | `#d3dae1` | hairlines | decorative |
| `--dw-border-strong` | `#aebac5` | input borders, table frames | ≥3:1 vs surface (non-text UI) |

### Neutrals (dark)
Canvas `#0f141a`, cards `#171e27` *above* it, wells `#11181f` *below* it — three distinct
planes, which the old dark theme lacked (its surface-2 sat between bg and surface, so
nothing read as recessed). Text `#e6ebf0` (13.7:1), muted `#98a7b6` (6.9:1), faint `#8a99a8` (5.8:1 on surface),
borders `#2b3744` / `#3e4d5d`.

### Accent
Navy stays — it is right for the domain — but becomes a **designed pair**:
light `#1f4e79` (8.6:1 on white), dark `#7fb2e0` (7.4:1 on `#171e27`), each with a
`-strong` hover step, a translucent `-bg` fill, and a `-glow` for focus rings.
The old dark accent `#6ea8d8` was a guess at "lighter navy"; the new value is tuned so
accent-on-surface contrast is roughly equal across themes.

### Semantic
`--dw-ok / warn / bad` each gain a `-bg` tint for fills (issue rows, toasts, audience tags).
**`--dw-warn` changed from `#b26a00` (3.6:1 — failed AA as text) to `#8f5400` (6.1:1).**
Dark equivalents are desaturated pastels (`#79c47e`, `#d9a05b`, `#e5736c`) — saturated
darks vibrate on dark surfaces.

### Chrome
The chrome bar stays dark in both themes — it is the product's signature. New
`--dw-chrome-muted` and `--dw-chrome-border` end the practice of hardcoding rgba whites
inside the bar.

`--dw-chrome-2` (`#1b2937` light / `#16222e` dark) is a **raised surface on the dark chrome** —
the project tile and the project menu button's hover sit on it (their definition comes from `--dw-chrome-border`,
not luminance). `--dw-mark` (`#2f6cae`, both themes) is the **brand accent for dark chrome**:
`--dw-accent` (#1f4e79) is too dark to read as a brand fill on the bar, so the brand mark and the Build
button use `--dw-mark` (white text on it = 5.4:1). **Scope: both tokens are chrome-only — never use them
on light `--dw-surface`.** Text on `--dw-mark` is `--dw-mark-text` (white) — not `--dw-accent-contrast`,
which resolves to dark ink inside the always-dark header (3.4:1) — and its hover/pressed step is
`--dw-mark-strong` (#255b94, white text 7.0:1). Both chrome-only, same in both themes.

---

## 3. Type scale

System stack only (offline constraint): `--dw-font-ui` = Segoe UI first;
`--dw-font-mono` = Consolas first.

| Token | Size | Use |
|---|---|---|
| `--dw-fs-xs` | 12px | footers, meta labels, slide page-number, table heads (CAPS) |
| `--dw-fs-sm` | 13px | nav, badges, captions, form labels, table cells |
| `--dw-fs-md` | 15px | body — base reading size everywhere |
| `--dw-fs-lg` | 17px | h3, lead paragraphs |
| `--dw-fs-xl` | 21px | h2 |
| `--dw-fs-2xl` | 27px | h1 (chapter titles) |
| `--dw-fs-3xl` | 34px | welcome hero |

Line heights: `--dw-lh-tight` 1.25 (headings/buttons), `--dw-lh-snug` 1.4 (nav/cells),
`--dw-lh-body` 1.65 (prose). Weights: 400 / 600 (Segoe UI Semibold) / 700.
ALL-CAPS labels always get `letter-spacing: var(--dw-track-caps)` (0.07em) + `--dw-fs-xs`/`-sm`.

**Slide scale** (1920×1080 frame, scaled to fit): title 64px, h2 44px, body 30px,
meta 20px. Nothing on a slide below 20px — it must read from the back of a workshop.

## 4. Space, radius, elevation

- Space: 4px base — `--dw-s1..s8` = 4 / 8 / 12 / 16 / 24 / 32 / 48 / 64. Components use
  s2–s5; page sections s6–s8. No magic numbers.
- Radius: `--dw-r0` 4px (badges, small inputs), `--dw-r1` 6px (buttons, inputs, code),
  `--dw-r2` 10px (cards, dialogs, slides). Nothing rounder.
- Elevation: `--dw-shadow` (resting cards), `--dw-shadow-2` (dialogs, toasts, open menus).
  Two levels only. Borders carry structure; shadows only confirm it.
- Focus: `--dw-focus-ring` on every focusable element, paired with `outline: none`.
- Control heights (Stage 6): every button, field, pill and segmented control is one of three —
  `--dw-ctl-sm` 28px (in-row tools: the tree's and the rail's ⋯ and +, small icon buttons),
  `--dw-ctl-md` 32px (bars, menus, fields: the document bar, the toolbar, menu items, inputs,
  segmented controls), `--dw-ctl-lg` 36px (the chrome bar, dialog and sheet actions).
- Stacking (editor, Stage 6): z-index comes only from the `--dw-z-*` scale at the top of
  app.css — raised 1 · handle 2 · sticky 3 · overlay 4 · focus 5 · sheet 900 · menu 1000
  (menus and tooltips, above the sheet) · toast 2000 · popover 2200 (toolbar pop-ups).

---

## 5. Component recipes

States listed as **D**efault / **H**over / **F**ocus-visible / **Dis**abled.
(Focus = `box-shadow: var(--dw-focus-ring)` unless noted.)

### Handover — `site.css`

**`header` / `.brand` / `.tabs`** — chrome bar, always dark; the `<header>` carries
`data-theme="dark"` so controls inside it resolve dark-theme tokens in both page themes.
D: `background: var(--dw-chrome)`, 48px tall, `border-bottom: 1px solid var(--dw-chrome-border)`.
`.brand` = `--dw-fs-md` bold `--dw-chrome-text`; project meta beside it in `--dw-chrome-muted`
mono `--dw-fs-xs`. Tabs: `--dw-fs-sm` 600, color `--dw-chrome-muted`, padding s2 s3, radius r1.
H: text → `--dw-chrome-text`. Active: `background: var(--dw-accent-bg)` is wrong in chrome —
use solid `var(--dw-accent)` pill with `--dw-accent-contrast` text in light theme; in dark, same.
F: focus ring.

**`#sidebar` / `.nav-group` / `.grp`** — `.grp` group labels: CAPS, `--dw-fs-xs`, 600, tracked,
`--dw-muted`, margin-top s5. Items: `--dw-fs-sm`, snug, padding s2 s3, radius r1, color `--dw-text`.
H: `background: var(--dw-surface-2)`. Active: `background: var(--dw-accent-bg)`,
`color: var(--dw-accent)`, 600, plus inset 3px accent bar (`box-shadow: inset 3px 0 0 var(--dw-accent)`).
F: ring.

**`#searchBox` / `.sr-item`** — input: surface bg, `1px solid var(--dw-border-strong)`, r1,
padding s2 s3. F: border-color accent + ring. `.sr-item`: padding s3, `--dw-fs-sm`, match
text 600 accent; H/selected: `--dw-accent-bg`.

**`article`** — max-width `--dw-measure` (210mm — A4 sheet width, ≈794px @96dpi; changed
from 72ch on stakeholder request 2026-06-12 so chapters read like the standard document
page the handover replaces; the padding plays the page margins), padding s6 s7. h1 `--dw-fs-2xl` bold; h2 `--dw-fs-xl` bold with
`border-bottom: 1px solid var(--dw-border)` + padding-bottom s2; h3 `--dw-fs-lg` 600. Body
`--dw-fs-md`/`--dw-lh-body`. `img`: r1, `1px solid var(--dw-border)`. `pre`/`code` per base.

**Tables (first-class)** — head row: `--dw-surface-2`, CAPS xs 600 tracked `--dw-muted`,
`border-bottom: 2px solid var(--dw-border-strong)`. Cells: `--dw-fs-sm`, padding s2 s3,
row hairlines only (no vertical rules), zebra at 45% surface-2. Numeric/ID columns:
`--dw-font-mono`. Wrap in `.dw-figure`; caption beneath.
**Breakout (2026-06-12):** a table wider than the A4 column (`--dw-measure`) grows with its content —
centered on the column, capped at `min(1200px, 100vw − 360px)` — and the figure scrolls
horizontally beyond that; content is never clipped. Narrow tables stay column-width;
prose always keeps the column; print disables the breakout (full page width already).
In the live editor the active table additionally offers a Focus mode (full-pane,
edit-time only — nothing about the page changes).

**`.aud` / `.aud-tag`** — audience block, a quiet margin marker (Stage 6; was a filled
callout): no fill, no box — `border-left: 2px solid var(--dw-border-strong)`, padding-left s4,
pulled left by s4 + 2px so its text lines up with the paragraphs around it (inside a step,
`li > .aud`, with no vertical margin, so the step keeps its rhythm). `.aud-tag`: "Only for User, Support" — CAPS xs 600 tracked
`--dw-muted`. No colour per audience — the label differentiates; with many tagged blocks a
coloured box per block turned the page into a patchwork.

**`.dw-figure` / `figcaption`** — caption: `--dw-fs-sm` `--dw-muted`, margin-top s2;
the label ("Table 1") in 600: `Table 1 — Device list`. Em dash, not colon.

**`.img-placeholder`** — dashed `--dw-border-strong` 1.5px, r1, `--dw-surface-2`,
centered `--dw-muted` `--dw-fs-sm`, min-height 160px, plus mono filename of the missing asset.

**`.prevnext`** — two secondary buttons (recipe below), justified apart, separated from
content by `border-top: 1px solid var(--dw-border)`, margin-top s7, padding-top s5.

**`.meta-table`** — key column: `--dw-fs-sm` `--dw-muted`, width ~200px; value column:
`--dw-font-mono --dw-fs-sm` for codes/versions. No zebra; hairlines only.

**`.attach` / `.pdf`** — attachment row: surface card, r1, border, padding s3 s4; filename
600 + mono size/type in `--dw-muted`. `.pdf` iframe: `border: 1px solid var(--dw-border)`,
r1, height ~75vh.

**Slides — `.slides-surface` / `.slide-frame` / `.slide` / `.slide-bar` / `.src`**
`.slides-surface`: fills viewport below chrome, `background: var(--dw-chrome)` *in both themes*
— the letterbox surround stays dark (no flashbang in a dark workshop). The controls on it
(`.slide-bar`, `.hint`) use chrome tokens (`--dw-chrome-text/-muted/-border`) so they stay
legible there in both page themes.
`.slide-frame`: fixed 16:9, scaled via transform to fit; `background: var(--dw-surface)`, r2,
`--dw-shadow-2`. The slide **canvas** follows the page theme (light/dark toggle) — a light
slide on the dark surround in light mode, a dark slide in dark mode.
`.slide`: padding 96px; title `--dw-slide-title` bold with a 4px accent rule of fixed width
(120px) beneath — not a full-width underline; body `--dw-slide-body`/1.5; lists max ~6 bullets.
`.slide-bar`: control strip on the dark surround — counter `n / N` (`.ctr`) and `.hint` use
`--dw-chrome-muted`, buttons use `--dw-chrome-text`/`--dw-chrome-border`.
`.src`: per-slide source attribution, bottom-right above the bar, mono `--dw-slide-meta` `--dw-muted`.
Controls (`Prev/Next/Fullscreen`) live *outside* the frame, secondary-button style.

**`footer`** — `--dw-fs-xs` `--dw-muted`, generated-by line; `border-top: 1px solid var(--dw-border)`.

### Editor — `app.css`

**The recipes (Stage 6 step 7).** Each concept below is defined ONCE, in the "Recipes" block
at the top of app.css; a component rule adds only its placement and, where named, a variant.
A new component joins the recipe's selector list instead of restating it (rule 21).

**Buttons** — r1, `--dw-fs-sm` 600, tight.
- *Secondary* (the default `button`): transparent, `1px solid var(--dw-border-strong)`, text
  `--dw-text`. H: `--dw-surface-2`. Dis: 45% opacity, no pointer.
- *Primary* (`button.primary`, `.dwa-btn-primary`, the sheet's Save when dirty — one per
  screen: Build handover in the chrome bar, Save in the Settings sheet; chapters and outputs
  have none, they autosave): `--dw-accent` fill, `--dw-accent-contrast` text, `--dw-shadow`.
  H: `--dw-accent-strong`.
- *Danger* (`dialog#modal button.danger`, `.dwa-btn-danger`): transparent, `1px solid
  var(--dw-border)`, text `--dw-bad`. H: `--dw-bad-bg` + `--dw-bad` border. Never solid red at
  rest.
- *Sized* (`.dwa-btn` lg, `.dwa-btn-sm` / the bar pills md): inline-flex, icon + label, gap s2.
- *Icon* (the document bar's History / Properties / ⋯, the tree's and rail's ⋯, the sheet's ×,
  the audience grip, the recent-row tools, the check panel's ×, Quit): a square of a control
  height, borderless and `--dw-muted` until hovered (`--dw-surface-2` + `--dw-text`).
- *Chrome* (Check, Preview as, Handover, Download on the dark bar): the secondary recipe in
  chrome tokens (`--dw-chrome-border`, `--dw-chrome-text`); Build handover is the chrome
  primary on `--dw-mark`.
- *Toolbar* (`.ib` in the main toolbar, the context bar and menu rows): md, borderless,
  `--dw-muted`, r1.
- *Add* (+ New section, "No chapters yet", + Add slide / section, Add audience, Mark a
  version): a dashed `--dw-border-strong` outline, `--dw-muted`, at least md tall.
- *Text link* (`.hist-link`: Restore, Copy link, Stop preview, Change): accent text, underline
  on hover; `.danger` = `--dw-bad`.

**Fields** — every single-line input and select (Settings, dialogs, the Section select, a
menu's filter box, the context bar's caption and pixel fields, the welcome search): md tall,
`--dw-surface`, `1px solid var(--dw-border-strong)`, r1, sm text; F: accent border + ring.
Textareas share the look at their own height. The output title (`.cb-title-input`) is the one
inline field: it reads as text until hovered.

**Labels and badges** — the CAPS label (rule 12; group headings, panel heads, eyebrows, menu
labels, the context bar's element name): xs 600 tracked `--dw-muted`. The badge (template
state, check severity, a block's type pill, a linked block's pill, the context bar's badge,
package type / status): an xs 600 tracked CAPS chip, r0, 1px `--dw-border` on
`--dw-surface-2`; variants (warn, accent, solid accent, ok / bad, severity) set only colour.

**Icons** — one set at one stroke: icons.js (16-unit grid, stroke 1.5) and the markup's own
24-unit icons draw with a non-scaling 1.5px stroke, so every outline icon has the same weight
at any size. Glyphs are not icons (the toolbar's Ω inserts a character; H2 / H3 are type
labels).

**Chrome bar** (Stage 6) — brand · the project's name as a button (`.hd-proj-btn`, chrome
ghost: H `--dw-chrome-2` + `--dw-chrome-border`) opening the *project menu* (switch project,
folder, import, export, copies; the program's build date as its last label) · Check
(chrome secondary) · **Build handover** (the one primary, `--dw-mark`) · Handover / Download
after a build · quit / window controls. No other buttons live in the bar.

**`.dw-seg`** (Stage 6) — THE segmented control: a `--dw-surface-2` well, 1px `--dw-border`,
r1, 2px inset; buttons xs 600 `--dw-muted`, r0; selected (`aria-selected` for tabs,
`aria-pressed` for a toggle group) = `--dw-surface` + `--dw-text` + `--dw-shadow`; the whole
control is md tall. Used by the sidebar tabs, Edit · Source · Read, Slides · Document,
History's scope, and window mode and appearance (Settings and the welcome screen) — there is
no other segmented control.

**Action menus** — every editor menu (tree ⋯, project menu, the document bar's ⋯, Readers,
Preview as) is app.js `actionMenu()` on the `.e2-menu` recipe: items, CAPS labels,
separators, danger items, an optional one-line `.mhint` (xs muted) under an item's name, and
checkable items (`.mcheckable`: a trailing accent tick when on; the menu stays open while
ticking). Long lists get a `.mfilter` box (the input recipe) and scroll inside the menu. Esc
closes it and returns focus; arrow keys move between items.

**Readers picker** (Stage 6) — the action menu for "who reads this": a CAPS question ("Who
reads this chapter / step / block?"), *Everyone*, the audiences with their description as the
hint, a filter box from 7 audiences, *Manage audiences…*. Neutral throughout — no colour per
audience. The same picker serves the chapter pill and the toolbar's Readers button (a tagged
block's context bar opens it too).

**Audience block marker** (site.css, ships) — a tagged block stays in the text: a 2px
`--dw-border-strong` rule in the left margin (the block pulls left by `s4` + 2px so its text
aligns) and an xs CAPS `--dw-muted` "Only for …" label; inside a step it drops its vertical
margin.

**Preview as** (Stage 6) — a chrome-secondary button "Preview as **Everyone** ▾" before Check
(`.on`: `--dw-mark` border on `--dw-chrome-2`); while on, a `.preview-banner` (`--dw-accent-bg`,
hairline, sm text + a `.hist-link` "Stop preview") sits under the document bar; other readers'
blocks are hidden, the page's markers and tools rest (tools at .45 opacity), a chapter the
audience doesn't get fades to .45, and its tree row dims to .5.

**`.sidebar`** (Stage 6) — 280px `--dw-surface-2` column: `.dw-seg.side-tabs` (Manual ·
Outputs · Files, full width) over one scrolling `.side-pane` per tab, *Project settings* in
`.side-foot` (border-top, ghost). All panes share the tree's row recipe — the handover
`#sidebar` recipe: `.grp` CAPS group labels, `a` rows, active = accent-bg + inset bar,
`.side-meta` (xs `--dw-faint`, right) for a row's audience, `.side-thumb` (32×24, r0, border)
for pictures, dashed `.tree-new-section` buttons for actions, `.side-empty` (xs muted) for
empty groups. Opening something switches to its tab.

**`#tree`** — the Manual pane. Structure editing (Stage 6): section headers carry `+` (add
chapter) and `⋯`, chapter rows carry `⋯` — `.tree-act` buttons, revealed on row hover but
always in the layout and keyboard-reachable; their menus are action menus. Double-click
renames inline (`.tree-rename`, accent border + focus ring). A dragged chapter marks its drop
target with a 2px accent line above a row, or an `--dw-accent-bg` wash on a section.

**`.chapterbar`** — the document bar (Stage 6): `Section / Title` crumb (section
`--dw-faint`, title 600) · `.cb-readers` pill (neutral: surface, `--dw-border-strong`, r1,
people icon + "User, Support +2" / "All readers" / "No readers" + chevron; opens the
Readers picker) · right: save state (amber `.cb-save-dot` while edits are pending,
`#status` xs muted text) · `.dw-seg.cb-mode` Edit · Source · Read · icon buttons
(`.cb-kebab-btn`, 34px, r1; pressed = `--dw-accent-bg` + accent) for History, Properties and
⋯. No ID chip, no Save button, no audience toggles.

**Output bar** (`.chapterbar.output-bar`, Stage 6) — the document bar's pattern for an output:
`Slide deck / <title>` (the title is a `.cb-title-input` — reads as text, a hairline on hover,
the input ring on focus, sized to its content) · "For **User** ▾" (the `.cb-readers` pill, a
single-choice audience menu) · save state · `.dw-seg` Slides · Document · Present (deck) or
Read (document) as a secondary button with an icon · ⋯ (Duplicate…, Export as one HTML
file, Delete output…). No primary button — outputs autosave.

**The rail** (`#vPanel`, Stage 6) — 232px surface column beside the canvas. A deck shows real
miniatures: each canvas slide cloned into a `.vthumb` (176×99, r0, border; the 1920×1080 slide
at zoom 176/1920 inside its own `.dw-live-surface` scope, so site.css draws it; inert), the
automatic title slide first as number 1 (numbers match the presentation's "2 of 12"). A
document lists its sections as one-line labels. Items: mono tabular number · miniature or
label; active = `--dw-accent-bg` (+ accent ring on the miniature); ⋯ (`.vpanel-more`, 24px,
revealed on hover / focus) for Move up / down, Layout (the four layouts, checkable, with their
one-line descriptions) and Delete. Between items, `.vrail-gap`: a 22px accent-outlined + on
hover adds one right there, and a 2px accent line marks the drop point of a drag. "+ Add
slide / section" (dashed) closes the list.

**`.props-panel`** (Stage 6) — Properties: the side-panel recipe of History (300px surface,
left border, `.hist-head` / `.hist-body`), holding a `.props-list` definition list: `dt` CAPS
xs tracked muted, `dd` rows — the Section select (moves the chapter), Readers as neutral
`.props-chip`s (r0, `--dw-surface-2`, border), "Used in" as `.props-used` accent links with an
xs muted detail ("slides 5, 6, 9"), the link id and file in mono `.props-code`
(`.props-path` muted), notes xs muted. History and Properties never show together.

**`.set-sheet` / `#setPane`** (Stage 6) — Settings is a sheet, not a screen: a fixed layer
below the chrome bar on `--dw-backdrop`, z 900 (under tooltips 1000 and toasts 2000), the
panel centred at min(1080px, 100%) × min(760px, 100%), r2, `--dw-shadow-2`; rail + content as
before, the top bar ends with Save (the sheet's one primary) and a ghost × close. Esc, × or a
click on the backdrop closes it (unsaved changes ask first); the editor behind is inert.

**`.hist-panel` / `.hist-compare`** (Stage 6) — History: a 300px `--dw-surface` side panel,
left border; a `.dw-seg` Chapter/Project switch; day labels CAPS xs tracked `--dw-muted`; `.hist-entry` rows = mono
tabular `--dw-faint` time column + label, H `--dw-surface-2`, selected = the tree's active
recipe (`--dw-accent-bg` + inset 3px accent bar); actions are `.hist-link` text buttons
(accent; `.danger` = `--dw-bad`). Comparing shows the older version AS the page (handover
recipe), under a sticky `.hist-banner`; blocks carry the change on themselves — added
`--dw-ok-bg` with a 3px `--dw-ok` bar in the left margin, removed `--dw-bad-bg` + bar +
strike-through — never wrappers.

**Status line** — the document bar's `#status` carries autosave: "Saving…" then
"Saved HH:MM" (calm muted, not green); a failed save stays in `.err` until the next save
succeeds.

**`.fm`** — the YAML editor's header row (path, Save, status): a surface strip with a
hairline under it.

**`.welcome-*`** — hero: `--dw-fs-3xl` bold + one-line `--dw-muted` lead; subtle wash allowed:
`linear-gradient(180deg, var(--dw-accent-bg), transparent 320px)` on the page top — the only
gradient in the system. Action cards: surface, border, r2, shadow; H: border-color
accent + shadow-2 + `cursor: pointer`; title 600 `--dw-accent`, arrow `→` on the right.
Recent rows: project name 600, path in mono `--dw-fs-xs` `--dw-muted`.

**`dialog#modal` / `.mhelp` / `.modal-warn`** — r2, `--dw-shadow-2`, border, padding s5,
max-width 520px; `::backdrop: rgba(10,15,20,0.55)`. Title `--dw-fs-lg` 600. `.mhelp`:
`--dw-fs-sm` `--dw-muted`. `.modal-warn`: `--dw-bad-bg` well, `--dw-bad` 600 text, r1,
padding s3, no icon needed. Footer: primary right, secondary left of it, danger far-left.

**`.file-list` / `.file-row` / `.file-thumb`** — rows: padding s2 s3, r1, hairline between;
H: `--dw-surface-2`; selected: `--dw-accent-bg` + 600. `.file-thumb`: 40px, r0, border,
`--dw-surface-2` with mono extension label when no preview.

**`#tooltip`** — `--dw-chrome` bg, `--dw-chrome-text`, `--dw-fs-sm`, r1, padding s2 s3,
`--dw-shadow-2`, max-width 320px. Same in both themes.

**`#errToast`** — bottom-right card: `--dw-surface` + `--dw-shadow-2` + r1 +
`border-left: 3px solid var(--dw-bad)`; title 600 `--dw-bad`, body `--dw-fs-sm`. Auto-dismiss ≥6s.
`.toast-success` swaps both to `--dw-ok`. A sidebar change's toast ends with one text-link
action (`.hist-link` "Undo"); the toast takes clicks only while shown.

**`#issuesPanel` / `.sev.Error` / `.sev.Warning`** — rows: `--dw-fs-sm`, mono location
(`chapter:line`), hairlines. `.sev`: badge recipe (CAPS xs, r0, 1px border):
Error = `--dw-bad` on `--dw-bad-bg`; Warning = `--dw-warn` on `--dw-warn-bg`.

**The editing surface** (`#editor2.dw-live-surface`, replaced Toast UI at the M5 flip
2026-06-12) — IS the handover page: `site.css` applies inside it via the `@scope`
wrapper served at `/app/site-scoped.css`, tokens from the shared `tokens.css`, so
content styling changes hit editor and handover together by construction. app.css may
style only EDITING CHROME (context bar, pills, grips, cell outlines, focus mode) —
never content; content look belongs in site.css. The surface must never clip the
table breakout (`#editor2 .ProseMirror { overflow: visible }`).

---

## 6. Print (bonus)

`@media print` on handover pages: hide chrome/sidebar/search/prevnext; article max-width
none; black text on white regardless of theme (`color: #000; background: #fff` — the one
sanctioned literal); tables `break-inside: avoid` with repeated `thead`; base 12pt;
captions keep numbering; URLs of external links printed after the anchor text via
`a[href^="http"]::after { content: " (" attr(href) ")"; }`.
