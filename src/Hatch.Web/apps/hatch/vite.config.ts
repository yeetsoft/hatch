import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { hatchRevision } from '../../vite-plugin-hatch-revision.mjs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

// Bundles straight into the ASP.NET Core static files folder that Hatch.Api
// serves at /apps/hatch (see Program.cs).
const __dirname = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(__dirname, '../../../..')

export default defineConfig({
  base: '/apps/hatch/',
  plugins: [react(), hatchRevision({ app: 'hatch' })],
  test: {
    // Vitest stubs every CSS import to an empty module by default, raw query
    // or not - fine for a stylesheet nothing reads, wrong for tokens.css,
    // which @hatch/ui's themeStore.ts reads as text via `?raw`. Scoped to
    // that one file (no `$` anchor: the module id still carries the `?raw`
    // suffix) so every other CSS import keeps costing nothing.
    css: {
      include: [/tokens\.css/],
    },
  },
  build: {
    outDir: resolve(projectRoot, 'src/Hatch.Api/wwwroot/apps/hatch'),
    emptyOutDir: true,
    rolldownOptions: {
      output: {
        // Left in the entry chunk, Vite's preload helper ties every lazily
        // loaded chunk's hashed name to the entry's, so a deploy that touches
        // only the entry - which is nearly every deploy - renames chunks (the
        // Monaco chunk, in particular) that did not themselves change. A tab
        // left open across that deploy then asks for a file that is gone. In
        // a chunk of its own, the helper's name changes only when it does.
        codeSplitting: { groups: [{ name: 'vite-preload', test: /vite\/preload-helper/ }] },
      },
    },
  },
})
