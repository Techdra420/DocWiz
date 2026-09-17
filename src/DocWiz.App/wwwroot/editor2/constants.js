/* Shared constants of the live editor surface. The image-width presets exist in
   exactly two scales ON PURPOSE: standalone figures use the page scale, pictures
   inside table cells use the half scale. Everything that shows or snaps an image
   width — the figure tools, the cell-image tools, the insert-image dialog
   (via window.DocWizEditor2.presets), the drag snapping — derives from these
   two tables. */

// Standalone figures: [label, px]. '' (original size) is appended by each consumer.
// XS (50 px) is the micro-icon preset — the drag floor (MIN_IMAGE_WIDTH=80) applies
// only to drag-resize, not to preset buttons, so XS works fine by button click.
export const FIGURE_WIDTHS = [['XS', 50], ['S', 320], ['M', 640], ['L', 960]];
export const FIGURE_SNAPS = FIGURE_WIDTHS.map(([, w]) => w);
// Pictures inside table cells — half scale (inline images read smaller in a grid).
// Cell XS is pinned to 50 (NOT the half-scale 25) — matches the figure XS preset.
export const CELL_IMAGE_WIDTHS = [['XS', 50], ['S', 160], ['M', 320], ['L', 640]];
export const CELL_IMAGE_SNAPS = CELL_IMAGE_WIDTHS.map(([, w]) => w);
export const SNAP_TOLERANCE = 14;   // px of drag slack around a snap stop
export const MIN_IMAGE_WIDTH = 80;  // px floor while drag-resizing a figure
export const HANDLE_OFFSET = 7;     // half the 14px resize-handle box: it sits ON the image corner

export const MIN_COL_DRAG_PX = 40;  // column width floor during a width drag
export const MIN_COL_PCT = 2;       // % floor when the drag math is saved

export const LOCK_FLASH_MS = 1500;  // locked-cell outline flash
export const CTX_FLASH_MS = 5000;   // context-bar flash message

/* Cross-language contract: the missing-image placeholder the editor model emits
   (schema.js image toDOM) must match the server's placeholder byte-for-byte
   (LinkRewriter.MissingImagePlaceholder / EditorDoc.PlaceholderSpan), or the
   cell faithfulness gate locks every cell that holds one. */
export const IMG_PLACEHOLDER_CLASS = 'img-placeholder';
export const MISSING_IMAGE_TEXT = 'image not available yet: ';
