import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const workflow = readFileSync(new URL('../../../../../.github/workflows/pages.yml', import.meta.url), 'utf8')

const TREES = [
  'src/Hatch.Web/apps/site/**',
  'src/Hatch.Web/apps/hatch/docs/**',
  'src/Hatch.Web/apps/hatch/src/lib/**',
  'src/Hatch.Web/packages/ui/**',
  'src/Hatch.Web/package-lock.json',
  '.github/workflows/pages.yml',
]

it('never mentions Jekyll', () => {
  expect(workflow).not.toMatch(/jekyll/i)
})

describe('configure-pages outputs', () => {
  it('names the step id: pages', () => {
    expect(workflow).toMatch(/^\s+id:\s*pages\s*$/m)
  })

  it('maps base_url from the pages step', () => {
    expect(workflow).toMatch(/^\s+base_url:\s*\$\{\{\s*steps\.pages\.outputs\.base_url\s*\}\}\s*$/m)
  })

  it('maps base_path from the pages step', () => {
    expect(workflow).toMatch(/^\s+base_path:\s*\$\{\{\s*steps\.pages\.outputs\.base_path\s*\}\}\s*$/m)
  })
})

it('uploads the Vite build output', () => {
  expect(workflow).toMatch(/^\s+path:\s*src\/Hatch\.Web\/apps\/site\/dist\s*$/m)
})

describe('the paths filter', () => {
  const lines = workflow.split(/\r?\n/)
  const start = lines.findIndex((line) => /^\s*paths:\s*$/.test(line))
  let end = start + 1
  while (end < lines.length && /^\s*-\s/.test(lines[end])) end++
  const block = lines.slice(start, end).join('\n')

  it.each(TREES)('lists %s', (tree) => {
    expect(block).toContain(`"${tree}"`)
  })
})
