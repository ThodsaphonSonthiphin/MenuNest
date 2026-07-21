/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { execSync } from 'node:child_process'

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string }

function shortSha(): string {
  let full = process.env.GITHUB_SHA ?? ''
  if (!full) {
    try { full = execSync('git rev-parse HEAD').toString().trim() } catch { /* git-less build */ }
  }
  return full ? full.slice(0, 7) : 'local'   // canonical: first 7 of the full sha
}

const sha = shortSha()

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(`${pkg.version}+${sha}`),
    __APP_COMMIT__: JSON.stringify(sha),
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'node',
  },
})
