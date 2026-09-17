/* Shared zoom-safe width drag for a resizable box — the table figure (TableView) and the
   linked-block .dw-embed (EmbedView). The slide edit surface is CSS-zoomed (the .slide has
   zoom < 1), so width is driven as a PERCENT computed from VISUAL (getBoundingClientRect)
   ratios: the zoom cancels in the ratio, and a % resolves in layout space, so the box
   tracks the pointer exactly. Setting a px width here would be re-scaled by the zoom and
   the box would jump.

   The caller owns the order-sensitive setup and measures `startVisual`/`avail` ITSELF, then
   hands them in — that ordering is load-bearing (e.g. the table must read startVisual BEFORE
   it adds .dw-table-sized, which drops min-width:100%, or the figure collapses to fit-content
   on press: the "jumps left" bug). This helper only runs the identical drag loop: pin the
   current %, then track the pointer, and commit a width string (or null at the right edge =
   full width).

   el         the element whose style.width is driven (and that hosts the % readout)
   startX     the mousedown clientX
   startVisual the box's current visual width (getBoundingClientRect().width)
   avail      the box's full visual width (its containing-block content width) — the 100% ref
   commit(width) called once on mouseup with the new width string, or null for full width
   probe      optional window-global name for the scenario tests to read the drag result */
export function trackWidthDrag(el, startX, startVisual, avail, commit, probe) {
  if (!avail) return;
  const MIN_PCT = 15;
  const pctAt = (dx) => Math.min(100, Math.max(MIN_PCT, Math.round(((startVisual + dx) / avail) * 100)));
  el.style.width = pctAt(0) + '%'; // pin the current % so the box never jumps on press
  const readout = document.createElement('div');
  readout.className = 'e2-boxreadout';
  readout.contentEditable = 'false';
  el.appendChild(readout);
  const move = (ev) => {
    const pct = pctAt(ev.clientX - startX);
    el.style.width = pct + '%';
    readout.textContent = pct + '%';
  };
  const up = (ev) => {
    document.removeEventListener('mousemove', move);
    document.removeEventListener('mouseup', up);
    readout.remove();
    const pct = pctAt(ev.clientX - startX);
    // Only a drag to/past the right edge (pct clamps to 100) resets to full width (no token).
    // A 98% threshold here ate the 98-99% range — the stakeholder set ~98% and it snapped
    // back to full on save (2026-06-16 dogfood); now 98%/99% stick and only the edge is full.
    const full = pct >= 100;
    if (probe) window[probe] = { startVisual, avail, dx: ev.clientX - startX, pct, full };
    commit(full ? null : pct + '%');
  };
  document.addEventListener('mousemove', move);
  document.addEventListener('mouseup', up);
}
