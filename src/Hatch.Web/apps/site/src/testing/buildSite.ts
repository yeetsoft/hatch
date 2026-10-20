/// <reference types="node" />
import { build } from 'vite'
import { readFileSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const configFile = resolve(__dirname, '../../vite.config.ts')

const ENV_KEYS_TO_CLEAR = ['base_url', 'base_path', 'GITHUB_SERVER_URL', 'GITHUB_REPOSITORY']

export async function buildSite(
  env?: Record<string, string | undefined>,
): Promise<{ dir: string; read(file: string): string }> {
  const savedEnv = { ...process.env }
  try {
    for (const key of ENV_KEYS_TO_CLEAR) delete process.env[key]
    for (const [key, value] of Object.entries(env ?? {})) {
      if (value === undefined) delete process.env[key]
      else process.env[key] = value
    }

    const dir = mkdtempSync(join(tmpdir(), 'hatch-site-'))
    await build({
      configFile,
      logLevel: 'silent',
      build: { outDir: dir },
    })

    return { dir, read: (file: string) => readFileSync(join(dir, file), 'utf8') }
  } finally {
    for (const key of Object.keys(process.env)) delete process.env[key]
    Object.assign(process.env, savedEnv)
  }
}
