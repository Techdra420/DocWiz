/* Shared builders for the editor's context-bar tools, so the figure tools (figureview.js)
   and the in-cell image tools (tableview.js) don't each re-declare identical DOM. */

/* An icon/text button in the toolbar's `.ib` style. mousedown is prevented so a click does
   not steal the selection; the click runs `onclick`. (Used as the factory `adjacencyButtons`
   takes, and anywhere a plain `.ib` button is built.) */
export function makeIbButton(label, tip, onclick) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'ib';
  b.textContent = label;
  b.setAttribute('data-tip', tip);
  b.onmousedown = (e) => e.preventDefault();
  b.onclick = (e) => { e.preventDefault(); onclick(); };
  return b;
}

/* Map a stored, file-relative image src ("../../assets/x.png") to the editor's /assets/ surface
   route, the one form the editor serves. ONE source for the figure tools, the cell-image tools,
   and any other in-editor image display (ADR-008, audit F4); the reverse (surface -> relative on
   save) lives in serialize.js `fileRelative`. */
export function toSurfaceSrc(src) {
  return (src || '').replace(/^(\.\.\/)+assets\//, '/assets/');
}

/* The exact-pixel-width `<input>` shared by the figure and cell-image size tools. `current`
   is the present width string (only a positive integer is shown); `onCommit(value)` receives
   the new width string, or null for "original size" (blank / non-numeric input). */
export function buildPxInput(current, onCommit) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = '1';
  input.placeholder = 'px';
  input.className = 'e2-pxinput';
  input.setAttribute('data-tip', 'Exact width in pixels (leave blank for original size).');
  if (current && /^\d+$/.test(current)) input.value = current;
  input.onchange = () => {
    const v = input.value.trim();
    onCommit(v && /^\d+$/.test(v) ? v : null);
  };
  input.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); input.blur(); } };
  input.onmousedown = (e) => e.stopPropagation();
  return input;
}
