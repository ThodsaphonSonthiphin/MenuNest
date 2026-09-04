# Icons

`icon.svg` is the single vector source for the Nest app icon (the 🪺 nest +
eggs brand mark on the indigo `theme_color` from `manifest.json`). Every PNG
here is rendered from it:

- `icon-192.png` — 192x192, `purpose: any`; rounded corners of its own for
  launchers that do not mask. Also the notification icon in `sw.js`.
- `icon-512.png` — 512x512, `purpose: any maskable`; full-bleed background,
  artwork inside the maskable safe zone (centre circle, radius 40%).
- `apple-touch-icon.png` — 180x180, linked from `index.html` for iOS.
- `badge-72.png` — 72x72, monochrome white silhouette on transparent; Android
  shows it as an alpha mask in the status bar.

Re-render after editing `icon.svg`:

```bash
cd frontend
node public/icons/render-icons.mjs
# no `npx playwright install`? point at an existing Chromium:
CHROMIUM_PATH=/path/to/chrome node public/icons/render-icons.mjs
```

Note: an already-installed PWA keeps the launcher icon it was installed with.
Remove the home-screen shortcut and add it again to pick up a new icon.
