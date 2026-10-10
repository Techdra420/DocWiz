/* inserts.js — the ONE insert vocabulary (ADR-008): what an author can put on the page, by
   name, one-line description and icon. The toolbar's Picture / Table buttons and its Insert
   menu (main.js) and the slide zones' slash menu (slashmenu.js) all take their words and
   icons from here; each surface wires the action itself. Add an insert here, never in one
   surface only (pinned by ToolbarParityTests). */

export const INSERTS = {
  picture: { title: 'Picture', desc: 'From the project’s files, or a new one', icon: 'image' },
  table: { title: 'Table', desc: 'Pick its size in a grid', icon: 'table' },
  block: { title: 'Building block', desc: 'Callout, steps, cards, buttons, collapsible', icon: 'blocks' },
  group: { title: 'Group the selected blocks', desc: 'Keep them together to reuse on a slide', icon: 'group' },
  linked: { title: 'Linked section', desc: 'A part of a chapter, shown here and kept in sync with it', icon: 'link' },
  character: { title: 'Special character', desc: '° ± µ Ø → and more', icon: 'omega' },
  divider: { title: 'Divider line', desc: 'A line across the page between parts', icon: 'hrule' },
};
