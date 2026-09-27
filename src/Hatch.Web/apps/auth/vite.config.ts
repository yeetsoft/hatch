import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { hatchRevision } from '../../vite-plugin-hatch-revision.mjs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

// Bundles straight into the ASP.NET Core static files folder that Hatch.Api
// serves at /apps/auth (see Program.cs).
const __dirname = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(__dirname, '../../../..')

export default defineConfig({
  base: '/apps/auth/',
  plugins: [react(), hatchRevision({ app: 'auth' })],
  build: {
    outDir: resolve(projectRoot, 'src/Hatch.Api/wwwroot/apps/auth'),
    emptyOutDir: true,
  },
})
