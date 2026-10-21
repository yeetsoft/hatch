import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { siteEnv } from './src/siteEnv'
import { siteHtml } from './vite-plugin-site'
import { docsHtml } from './vite-plugin-docs'

const __dirname = dirname(fileURLToPath(import.meta.url))

const env = siteEnv(process.env)

export default defineConfig({
  base: env.base,
  plugins: [siteHtml(env), docsHtml(env.canonical)],
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
      output: {
        // docsDiagrams.ts eagerly imports dompurify (through mermaidDraw.ts),
        // and mermaid depends on a compatible version npm hoists to the same
        // copy. Left alone, Rolldown inlines that shared copy - and the
        // `vite/preload-helper` the dynamic import() of mermaid.ts also pulls
        // in - into the fenced page's own entry chunk, and gives the mermaid
        // chunk an import edge back to it: a real build confirmed both edges
        // (dompurify and the preload helper), so `modulePreload: { polyfill:
        // false }` above does not make the preload-helper group apps/hatch's
        // vite.config.ts carries unneeded here, though it was expected to. A
        // chunk of its own for each breaks both edges: a deploy that touches
        // only that one fenced page no longer renames the mermaid chunk every
        // other fenced page shares.
        codeSplitting: {
          groups: [
            { name: 'vite-preload', test: /vite\/preload-helper/ },
            { name: 'dompurify', test: /node_modules\/dompurify/ },
          ],
        },
      },
    },
  },
  test: {
    testTimeout: 60_000,
    // Matches testTimeout above for the same reason: several suites build the
    // whole site in a `beforeAll`, and mermaid's dependency tree pushed that
    // real build past Vitest's 10s default hook timeout.
    hookTimeout: 60_000,
  },
})
