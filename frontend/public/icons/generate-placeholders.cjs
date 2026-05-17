/* eslint-disable */
/**
 * Phase 1 placeholder icon generator.
 *
 * Produces three solid-color PNGs (icon-192, icon-512, badge-72) using
 * only Node built-ins (zlib + crc32). The icons are intentionally
 * minimal — a flat MenuNest indigo (#4f46e5) square — so the manifest
 * and notifications don't 404. Replace with a real logo when design
 * lands.
 *
 * Usage:
 *   node frontend/public/icons/generate-placeholders.cjs
 *
 * Re-runs are idempotent: existing files are overwritten with the same
 * bytes (no diff churn on rebuilds).
 */
const fs = require('node:fs')
const path = require('node:path')
const zlib = require('node:zlib')

// Indigo-500 from Tailwind to match the manifest theme_color.
const COLOR = { r: 0x4f, g: 0x46, b: 0xe5, a: 0xff }

function crc32(buf) {
  let c
  const table = []
  for (let n = 0; n < 256; n++) {
    c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[n] = c >>> 0
  }
  let crc = 0xffffffff
  for (let i = 0; i < buf.length; i++) crc = table[(crc ^ buf[i]) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const len = Buffer.alloc(4)
  len.writeUInt32BE(data.length, 0)
  const typeBuf = Buffer.from(type, 'ascii')
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0)
  return Buffer.concat([len, typeBuf, data, crc])
}

function makePng(size) {
  // PNG signature.
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])

  // IHDR — 8-bit RGBA.
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(size, 0) // width
  ihdr.writeUInt32BE(size, 4) // height
  ihdr.writeUInt8(8, 8) // bit depth
  ihdr.writeUInt8(6, 9) // color type 6 = RGBA
  ihdr.writeUInt8(0, 10) // compression
  ihdr.writeUInt8(0, 11) // filter
  ihdr.writeUInt8(0, 12) // interlace

  // IDAT — one filter byte (0) per scanline, then size px * 4 bytes.
  const rowSize = 1 + size * 4
  const raw = Buffer.alloc(rowSize * size)
  for (let y = 0; y < size; y++) {
    const off = y * rowSize
    raw[off] = 0 // filter: none
    for (let x = 0; x < size; x++) {
      const p = off + 1 + x * 4
      raw[p] = COLOR.r
      raw[p + 1] = COLOR.g
      raw[p + 2] = COLOR.b
      raw[p + 3] = COLOR.a
    }
  }
  const idat = zlib.deflateSync(raw)

  return Buffer.concat([
    sig,
    chunk('IHDR', ihdr),
    chunk('IDAT', idat),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

const outDir = __dirname
const targets = [
  { size: 192, name: 'icon-192.png' },
  { size: 512, name: 'icon-512.png' },
  { size: 72, name: 'badge-72.png' },
]

for (const t of targets) {
  const png = makePng(t.size)
  const dest = path.join(outDir, t.name)
  fs.writeFileSync(dest, png)
  console.log(`wrote ${dest} (${t.size}x${t.size}, ${png.length} bytes)`)
}
