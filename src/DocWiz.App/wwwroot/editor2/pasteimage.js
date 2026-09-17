/* Shared paste/drop image routing. Goal: a pasted or dropped image becomes a saved project
   asset, never an inline `data:` URI.

   `routePastedImage(data, onFile)` returns true when it has taken the image (the caller must
   then return true from handlePaste so ProseMirror does not also handle it). It finds the
   image from:
     1. a real image file on the clipboard/drop (screenshot tools, a copied image file);
     2. a `data:` URI <img> in the pasted HTML (web pages, some Word copies).

   It deliberately does NOT try to rescue a Word "Copy image" that exposes the picture only
   as an OS bitmap — that bitmap is in no web-accessible clipboard format (neither the sync
   paste event nor navigator.clipboard.read() can see it). ProseMirror's fallback inlines it
   as a data: URI; the server decodes it to a real asset at save time (DataImageRescue, the
   one place the bytes are reachable), and dataImageGuard (below) catches it sooner in the
   editor and offers the folder/name dialog. */

import { Plugin, PluginKey } from 'prosemirror-state';

export function routePastedImage(data, onFile) {
  const file = imageFileFromPaste(data);
  if (file) { onFile(file); return true; }
  return false;
}

/* A Word "Copy image" exposes the picture to NO web clipboard API, so routePastedImage
   can't catch it — ProseMirror's fallback inlines it as a `data:` URI <img> (an inline
   image node). This plugin catches that node the instant it lands and routes it through
   the project-assets dialog (pick a subfolder + name), then swaps the node's src for the
   saved asset. Cancel removes the pasted image (cancel = no insert). Add it to every editor
   that accepts pastes; the server-side DataImageRescue remains a save-time backstop. */
export function dataImageGuard() {
  let busy = false; // one dialog at a time across re-entrant updates
  const findDataImage = (doc) => {
    let hit = null;
    doc.descendants((node, pos) => {
      if (hit) return false;
      if (node.type.name === 'image' && /^data:image\//i.test(node.attrs.src || '')) hit = { pos, node };
    });
    return hit;
  };
  return new Plugin({
    key: new PluginKey('dwDataImageGuard'),
    view() {
      const handle = (view) => {
        if (busy) return;
        const bridge = window.DocWizBridge;
        if (!bridge || !bridge.pasteImage) return;
        const hit = findDataImage(view.state.doc);
        if (!hit) return;
        const file = dataUriToFile(hit.node.attrs.src);
        if (!file) return; // malformed — the save-time rescue will handle it
        busy = true;
        const dataUri = hit.node.attrs.src;
        // Re-resolve the node by its data: src on completion (its position may have moved).
        const apply = (mut) => {
          let p = -1; let n = null;
          view.state.doc.descendants((node, pos) => {
            if (p >= 0) return false;
            if (node.type.name === 'image' && node.attrs.src === dataUri) { p = pos; n = node; }
          });
          if (p >= 0) mut(p, n);
        };
        bridge.pasteImage(file,
          (res) => { busy = false; const src = (res.src || '').replace(/^(\.\.\/)+assets\//, '/assets/');
            apply((p, n) => view.dispatch(view.state.tr.setNodeMarkup(p, null, { ...n.attrs, src }))); },
          () => { busy = false; // cancel → remove the pasted image
            apply((p, n) => view.dispatch(view.state.tr.delete(p, p + n.nodeSize))); });
      };
      return { update: (view) => handle(view) };
    },
  });
}

export function imageFileFromPaste(data) {
  const files = data && data.files;
  if (files && files.length) {
    const f = Array.prototype.slice.call(files).find((x) => /^image\//.test(x.type));
    if (f) return f;
  }
  const html = data && data.getData && data.getData('text/html');
  if (html) {
    try {
      const doc = new DOMParser().parseFromString(html, 'text/html');
      const img = doc.querySelector('img[src^="data:image/"]');
      if (img) return dataUriToFile(img.getAttribute('src'));
    } catch (e) {
      // unparseable HTML — fall through
    }
  }
  return null;
}

function dataUriToFile(uri) {
  try {
    const comma = uri.indexOf(',');
    if (comma < 0) return null;
    const meta = uri.slice(5, comma);                 // e.g. image/png;base64
    const type = meta.split(';')[0] || 'image/png';
    const isBase64 = /;base64/i.test(meta);
    const payload = uri.slice(comma + 1).replace(/\s+/g, ''); // some sources wrap the base64
    const bin = isBase64 ? atob(payload) : decodeURIComponent(payload);
    const arr = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
    const ext = (type.split('/')[1] || 'png').replace('jpeg', 'jpg').replace('svg+xml', 'svg');
    return new File([arr], 'pasted.' + ext, { type });
  } catch (e) {
    return null;
  }
}
