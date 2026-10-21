import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { siteEnv } from './src/siteEnv'
import { siteHtml } from './vite-plugin-site'

const __dirname = dirname(fileURLToPath(import.meta.url))

const env = siteEnv(process.env)

export default defineConfig({
  base: env.base,
  plugins: [siteHtml(env)],
  appType: 'mpa',
  build: {
    outDir: resolve(__dirname, 'dist'),
    emptyOutDir: true,
    modulePreload: { polyfill: false },
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
