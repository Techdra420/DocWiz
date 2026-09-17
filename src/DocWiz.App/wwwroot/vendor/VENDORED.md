# Vendored third-party assets

Vendored per the no-CDN guardrail (CLAUDE.md): everything shipped must work fully
offline. No bundler, no Node: ProseMirror packages ship browser-ready ESM in their
npm tarballs; each `prosemirror/<pkg>/index.js` below is the byte-verbatim ESM dist
from the official tarball (auditable against the recorded SHA-256 of the .tgz), plus
its LICENSE. Modules are wired by the import map in `wwwroot/index.html`.

(Toast UI Editor 3.2.2, the original ADR-002 editor, was REMOVED 2026-06-12 at the
live-editor M5 flip after stakeholder sign-off — the ProseMirror live surface is the
only editor. History: git log on `wwwroot/vendor/toastui-*`.)

## ProseMirror (ADR-007 — live editing surface), vendored 2026-06-12

All MIT. Source: `https://registry.npmjs.org/<pkg>/-/<pkg>-<version>.tgz`,
file `package/dist/index.js` (`package/index.js` for w3c-keyname). SHA-256 is the
hash of the downloaded tarball.

| Package | Version | Tarball SHA-256 |
|---------|---------|-----------------|
| prosemirror-model | 1.25.8 | e83ba186f7e833a55fa8ff83333405f5b9fc1808f70f41aa8fc4a02ed414a922 |
| prosemirror-state | 1.4.4 | ad3eb60bd6db8e74b2576dc0b7c2800d6def362e0e990c9fb108fc4b724ee651 |
| prosemirror-view | 1.41.9 | 405a97cc15327137bfd534d75cbcb8b25c4c4701de0038bb1e25c7a6e88fc03a |
| prosemirror-transform | 1.12.0 | ec7e960ad356cd5b485a85d7a6123935f871f33484172f84cb01bd327bbb8914 |
| prosemirror-commands | 1.7.1 | 0364d5f139d69f8ac958a69ff7bf74501fc7ab03ce248d227c9e5410151a9e95 |
| prosemirror-keymap | 1.2.3 | 3131823ce73eaf3487cada22c85b76349da02c114dee3de696f4c1c3c645ca8c |
| prosemirror-history | 1.5.0 | 5ffc66c053f3e2d4d6c1f7d5f352c4401fee49b075a5b1bb5b03e7b9d84172a7 |
| prosemirror-inputrules | 1.5.1 | 785502d7e5ce0b9617d1aa6c0ef2aa2a0a6b06a5d8efb8942eb7c1b2d3b155aa |
| prosemirror-dropcursor | 1.8.2 | 3afcf5a6726bee8c9146696885f22dfb31d6457438f2b2175641dc53d567125f |
| prosemirror-gapcursor | 1.4.1 | 961e88b82ce7f434cc781ba644a82810461cf9ba3993834f654384c493998eba |
| prosemirror-schema-list | 1.5.1 | c60638c4fd055328cebada5a36a661d454eb87114919bfc2bfbfbcda2b939c2e |
| orderedmap | 2.1.1 | 9ad98abe6ac4156daef2232d7c1d2c8b3681545604c54f7f3754e791f12c168d |
| w3c-keyname | 2.2.8 | 05d21484026d1f2dd842c843181e391e66d60f1d5dff292d562c3f40302dab36 |
| rope-sequence | 1.3.4 | 2a483ee02e5a21b59b63a29bca162e6aa2cd8fafb474261040c3d8538a1f067c |

### Upgrade procedure (no npm required)

For each package: download the tarball over HTTPS, record its SHA-256, extract the
ESM dist, replace `prosemirror/<pkg>/index.js` + `LICENSE`, update this table:

```powershell
$p='prosemirror-model'; $v='<new>'
Invoke-WebRequest "https://registry.npmjs.org/$p/-/$p-$v.tgz" -OutFile "$env:TEMP\$p.tgz"
(Get-FileHash "$env:TEMP\$p.tgz" -Algorithm SHA256).Hash.ToLower()
tar -xzf "$env:TEMP\$p.tgz" -C $env:TEMP\pm
Copy-Item $env:TEMP\pm\package\dist\index.js "wwwroot\vendor\prosemirror\$p\index.js"
```

Then open `/app/editor2/smoke.html` in the running editor (or the headless JS sweep)
— it imports every module and reports `PM-OK` only when the whole graph loads.

<!-- Paged.js (formerly vendored for PDF export) was removed 2026-06-29: it paginated in
     JavaScript and timed out on real manuals. PDF export now uses native Chromium print
     (Page.printToPDF) over CDP — no vendored JS. -->
