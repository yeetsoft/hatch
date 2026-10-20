import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const __dirname = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  // A placeholder until the canonical-origin task makes this environment-driven.
  base: '/',
  appType: 'mpa',
  build: {
    outDir: resolve(__dirname, 'dist'),
    emptyOutDir: true,
    rolldownOptions: {
      input: {
        index: resolve(__dirname, 'index.html'),
        404: resolve(__dirname, '404.html'),
      },
    },
  },
  test: {
    testTimeout: 60_000,
  },
})
