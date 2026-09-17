/* DocWiz live-surface schema (ADR-007). Nodes mirror the ONE pipeline's HTML output
   (DocWizContainerRenderer / DocWizFigures markup), so site.css styles the surface
   exactly like the built page. This is the editing model only — Markdig remains the
   only markdown parser; the browser ingests pipeline HTML, never markdown. */
import { Schema } from 'prosemirror-model';
import { IMG_PLACEHOLDER_CLASS, MISSING_IMAGE_TEXT } from './constants.js';

export const CALLOUTS = ['note', 'tip', 'warning', 'danger'];
export const DEFAULT_LABEL = { note: 'Note', tip: 'Tip', warning: 'Warning', danger: 'Danger' };

function calloutAttrs(dom) {
  const type = CALLOUTS.find((t) => dom.classList.contains('dw-' + t));
  if (!type) return false;
  return { type, label: dom.getAttribute('data-label') || null };
}

const headingRules = [1, 2, 3, 4, 5, 6].map((level) => ({
  tag: 'h' + level,
  getAttrs: (dom) => ({ level, anchor: dom.getAttribute('id') }),
}));

export function createSchema() {
  return new Schema({
    nodes: {
      doc: { content: 'block+' },

      paragraph: {
        attrs: { srcId: { default: null } },
        content: 'inline*',
        group: 'block',
        parseDOM: [{ tag: 'p' }],
        toDOM: () => ['p', 0],
      },

      heading: {
        attrs: { level: { default: 2 }, anchor: { default: null }, srcId: { default: null } },
        content: 'inline*',
        group: 'block',
        defining: true,
        parseDOM: headingRules,
        toDOM: (node) => ['h' + node.attrs.level, node.attrs.anchor ? { id: node.attrs.anchor } : {}, 0],
      },

      // tight: pipeline renders tight list items without <p>; ProseMirror always
      // wraps — the attr records the original style for the M2 serializer.
      bullet_list: {
        attrs: { tight: { default: true }, srcId: { default: null } },
        content: 'list_item+',
        group: 'block',
        parseDOM: [{ tag: 'ul', getAttrs: (dom) => ({ tight: !dom.querySelector(':scope > li > p') }) }],
        toDOM: () => ['ul', 0],
      },

      ordered_list: {
        attrs: { start: { default: 1 }, tight: { default: true }, srcId: { default: null } },
        content: 'list_item+',
        group: 'block',
        parseDOM: [{
          tag: 'ol',
          getAttrs: (dom) => ({
            start: dom.hasAttribute('start') ? Number(dom.getAttribute('start')) : 1,
            tight: !dom.querySelector(':scope > li > p'),
          }),
        }],
        toDOM: (node) => ['ol', node.attrs.start === 1 ? {} : { start: node.attrs.start }, 0],
      },

      list_item: {
        // checked: null = ordinary item; false/true = task-list item (Phase 3).
        attrs: { checked: { default: null } },
        content: 'block+',
        defining: true,
        parseDOM: [{ tag: 'li', getAttrs: (dom) => {
          // Clipboard round-trip carries data-checked; pipeline / GitHub HTML carries a
          // leading <input type=checkbox> (ingest.js strips it to data-checked first).
          if (dom.hasAttribute('data-checked')) return { checked: dom.getAttribute('data-checked') === 'true' };
          const cb = dom.querySelector(':scope > input[type=checkbox], :scope > p > input[type=checkbox]');
          return { checked: cb ? cb.hasAttribute('checked') : null };
        } }],
        // The node view (taskitemview.js) draws the real checkbox in the editor. toDOM cannot
        // put a sibling before the content hole, so checked rides as a class + data-checked attr
        // that parseDOM reads back (clipboard / fallback rendering).
        toDOM: (node) => node.attrs.checked === null
          ? ['li', 0]
          : ['li', { class: 'task-list-item', 'data-checked': node.attrs.checked ? 'true' : 'false' }, 0],
      },

      blockquote: {
        attrs: { srcId: { default: null } },
        content: 'block+',
        group: 'block',
        parseDOM: [{ tag: 'blockquote' }],
        toDOM: () => ['blockquote', 0],
      },

      code_block: {
        attrs: { info: { default: null }, srcId: { default: null } },
        content: 'text*',
        marks: '',
        group: 'block',
        code: true,
        defining: true,
        parseDOM: [{
          tag: 'pre',
          preserveWhitespace: 'full',
          getAttrs: (dom) => {
            const code = dom.querySelector('code');
            const m = code && /language-(\S+)/.exec(code.className || '');
            return { info: m ? m[1] : null };
          },
        }],
        toDOM: (node) => ['pre', ['code', node.attrs.info ? { class: 'language-' + node.attrs.info } : {}, 0]],
      },

      horizontal_rule: {
        attrs: { srcId: { default: null } },
        group: 'block',
        parseDOM: [{ tag: 'hr' }],
        toDOM: () => ['hr'],
      },

      // The ::: element library + audience blocks. Chrome (tag pill / summary) is
      // NodeView-rendered from attrs; ingest.js strips it from the parsed HTML and
      // stashes the label in data-label first (see stripContainerChrome).
      container: {
        attrs: { type: {}, args: { default: '' }, label: { default: null }, desc: { default: null }, anchor: { default: null }, srcId: { default: null } },
        content: 'block+',
        group: 'block',
        defining: true,
        parseDOM: [
          { tag: 'div.dw-callout', getAttrs: calloutAttrs },
          { tag: 'div.aud', getAttrs: (dom) => ({ type: 'audience', args: dom.getAttribute('data-aud') || '' }) },
          { tag: 'details.dw-details', getAttrs: (dom) => ({ type: 'details', label: dom.getAttribute('data-label') || null }) },
          { tag: 'div.dw-steps', getAttrs: () => ({ type: 'steps' }) },
          { tag: 'div.dw-cards', getAttrs: () => ({ type: 'cards' }) },
          { tag: 'div.dw-buttons', getAttrs: () => ({ type: 'buttons' }) },
          { tag: 'div.dw-align-center', getAttrs: () => ({ type: 'center' }) },
          { tag: 'div.dw-align-right', getAttrs: () => ({ type: 'right' }) },
          { tag: 'div.dw-group', getAttrs: (dom) => ({ type: 'group', label: dom.getAttribute('data-dw-name') || null, desc: dom.getAttribute('data-dw-desc') || null, anchor: dom.id || null }) },
        ],
        toDOM: (node) => node.attrs.type === 'group'
          ? ['div', { class: 'dw-group', id: node.attrs.anchor || undefined, 'data-dw-name': node.attrs.label || undefined, 'data-dw-desc': node.attrs.desc || undefined }, 0]
          : ['div', { class: 'dw-container-' + node.attrs.type }, 0], // clipboard fallback; NodeView renders the real chrome
      },

      // Atoms: pipeline HTML displayed verbatim (captions, numbering, placeholders
      // included). src = verbatim markdown source; meta (when present) carries the
      // structured model the FigureView/TableView node views edit.
      figure: {
        attrs: { html: {}, src: { default: '' }, srcId: { default: null }, meta: { default: null } },
        group: 'block',
        atom: true,
        selectable: true,
        parseDOM: [{ tag: 'figure.dw-image-figure', getAttrs: (dom) => ({ html: dom.outerHTML, src: '' }) }],
        toDOM: () => ['figure', { class: 'dw-figure dw-image-figure' }],
      },

      table: {
        attrs: { html: {}, src: { default: '' }, srcId: { default: null }, meta: { default: null } },
        group: 'block',
        atom: true,
        selectable: true,
        parseDOM: [{ tag: 'figure.dw-table-figure', getAttrs: (dom) => ({ html: dom.outerHTML, src: '' }) }],
        toDOM: () => ['figure', { class: 'dw-figure dw-table-figure' }],
      },

      raw: {
        attrs: { html: {}, src: { default: '' }, srcId: { default: null }, meta: { default: null } },
        group: 'block',
        atom: true,
        selectable: true,
        toDOM: () => ['div', { class: 'dw-raw-block' }],
      },

      // A live-linked block (Slides v2, Task 9): a chapter section transcluded into a
      // slide region via the canonical '::: embed dw:ref :::' directive. ref (bare
      // "chapter#anchor") is the serialization source of truth — serialize.js re-emits
      // the directive from it; html is the resolved render (display only, like the atoms
      // above). EmbedView (views.js) renders html under a LINKED chrome pill with
      // Edit source / Detach. Only produced on the slide-zone path (ingest maps
      // blockType==='embed'); chapters never emit it unless they contain an embed.
      embed: {
        // width (L205): a CSS length the linked box reflows to (e.g. "60%"); null = full size.
        // align (L205): "center" | "right" places the sized box; null = left (default).
        attrs: { ref: {}, html: { default: '' }, srcId: { default: null }, meta: { default: null }, width: { default: null }, align: { default: null } },
        group: 'block',
        atom: true,
        selectable: true,
        toDOM: () => ['div', { class: 'dw-embed' }], // clipboard fallback; EmbedView renders the real chrome
      },

      text: { group: 'inline' },

      // Inline image (an image INSIDE text — standalone images are figure atoms).
      // Rare in practice, but without a node PM would silently drop it on edit.
      image: {
        inline: true,
        attrs: { src: {}, alt: { default: '' }, width: { default: null }, align: { default: null }, missing: { default: false } },
        group: 'inline',
        draggable: false,
        parseDOM: [{
          tag: 'img[src]',
          getAttrs: (dom) => ({
            src: dom.getAttribute('src') || '',
            alt: dom.getAttribute('alt') || '',
            width: dom.getAttribute('width'),
            align: dom.classList.contains('img-center') ? 'center' : dom.classList.contains('img-right') ? 'right' : null,
          }),
        }, {
          // The server's missing-asset placeholder (EditorDoc.PlaceholderSpan):
          // still an IMAGE in the markdown — model it as one so the cell editor
          // keeps it 1:1 instead of locking. Bare placeholders (no data-src =
          // unmodelable attributes) intentionally stay unparsed → faithfulness lock.
          tag: `span.${IMG_PLACEHOLDER_CLASS}[data-src]`,
          getAttrs: (dom) => ({
            src: dom.getAttribute('data-src') || '',
            alt: dom.getAttribute('data-alt') || '',
            width: dom.getAttribute('data-width'),
            align: dom.getAttribute('data-align'),
            missing: true,
          }),
        }],
        toDOM: (node) => {
          if (node.attrs.missing) {
            const attrs = { class: IMG_PLACEHOLDER_CLASS, 'data-src': node.attrs.src, 'data-alt': node.attrs.alt };
            if (node.attrs.width) attrs['data-width'] = node.attrs.width;
            if (node.attrs.align) attrs['data-align'] = node.attrs.align;
            const name = node.attrs.src.startsWith('/assets/') ? node.attrs.src.slice('/assets/'.length) : node.attrs.src;
            return ['span', attrs, MISSING_IMAGE_TEXT + name];
          }
          const attrs = { src: node.attrs.src, alt: node.attrs.alt };
          if (node.attrs.width) attrs.width = node.attrs.width;
          if (node.attrs.align) attrs.class = 'img-' + node.attrs.align;
          return ['img', attrs];
        },
      },

      hard_break: {
        inline: true,
        group: 'inline',
        selectable: false,
        parseDOM: [{ tag: 'br' }],
        toDOM: () => ['br'],
      },
    },

    marks: {
      link: {
        attrs: { href: {}, title: { default: null } },
        inclusive: false,
        parseDOM: [{ tag: 'a[href]', getAttrs: (dom) => ({ href: dom.getAttribute('href'), title: dom.getAttribute('title') }) }],
        toDOM: (node) => ['a', node.attrs.title ? node.attrs : { href: node.attrs.href }, 0],
      },
      em: {
        parseDOM: [{ tag: 'em' }, { tag: 'i' }],
        toDOM: () => ['em', 0],
      },
      strong: {
        parseDOM: [{ tag: 'strong' }, { tag: 'b' }],
        toDOM: () => ['strong', 0],
      },
      code: {
        parseDOM: [{ tag: 'code' }],
        toDOM: () => ['code', 0],
      },
      strike: {
        parseDOM: [{ tag: 'del' }, { tag: 's' }],
        toDOM: () => ['del', 0],
      },
    },
  });
}
