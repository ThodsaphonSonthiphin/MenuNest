/**
 * Renders every raster icon in this folder from `icon.svg` using the
 * Playwright Chromium the repo already depends on for e2e.
 *
 *   node frontend/public/icons/render-icons.mjs
 *
 * Outputs (all written next to this script):
 *   icon-192.png          purpose=any            rounded corners, launchers that do not mask
 *   icon-512.png          purpose=any maskable   full-bleed; art sits inside the safe zone
 *   apple-touch-icon.png  180x180                full-bleed; iOS applies its own mask
 *   badge-72.png          notification badge     monochrome white silhouette, transparent bg
 *
 * Re-runs are deterministic for a given Chromium build, so committing the
 * PNGs is fine; re-run whenever icon.svg changes.
 */
import { chromium } from '@playwright/test'
import { readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const svg = await readFile(join(here, 'icon.svg'), 'utf8')

/** @type {{ file: string; size: number; rounded?: boolean; badge?: boolean }[]} */
const targets = [
  { file: 'icon-192.png', size: 192, rounded: true },
  { file: 'icon-512.png', size: 512 },
  { file: 'apple-touch-icon.png', size: 180 },
  { file: 'badge-72.png', size: 72, badge: true },
]

// The badge is drawn by Android as an alpha mask, so colour is irrelevant:
// drop the background, paint every shape solid white.
const badgeCss = `
  .bg { display: none; }
  .art * { fill: #fff !important; stroke: #fff !important; opacity: 1 !important; }
  .art [fill="none"] { fill: none !important; }
`

const roundedCss = `.bg { rx: 22%; ry: 22%; }`

function page({ size, rounded, badge }) {
  return `<!doctype html>
<html><head><meta charset="utf-8"><style>
  html, body { margin: 0; padding: 0; background: transparent; }
  svg { display: block; width: ${size}px; height: ${size}px; }
  ${rounded ? roundedCss : ''}
  ${badge ? badgeCss : ''}
</style></head><body>${svg}</body></html>`
}

// CHROMIUM_PATH lets an environment with a pre-installed Chromium (no
// `npx playwright install`) point at its own binary.
const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH || undefined,
})
try {
  for (const t of targets) {
    const p = await browser.newPage({
      viewport: { width: t.size, height: t.size },
      deviceScaleFactor: 1,
    })
    await p.setContent(page(t))
    const png = await p.screenshot({ omitBackground: true, type: 'png' })
    await writeFile(join(here, t.file), png)
    console.log(`wrote ${t.file} (${t.size}x${t.size}, ${png.length} bytes)`)
    await p.close()
  }
} finally {
  await browser.close()
}
