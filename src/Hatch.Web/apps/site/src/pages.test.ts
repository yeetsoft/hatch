/* Both built pages carry the head, the landmarks and the one script the brief
   requires. Regex over the built text: there is no DOM in this runner. */
/// <reference types="node" />
import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'

type Site = Awaited<ReturnType<typeof buildSite>>

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

const withoutComments = (html: string): string => html.replace(/<!--[\s\S]*?-->/g, '')

const tagsOf = (html: string, name: string): string[] => html.match(new RegExp(String.raw`<${name}\b[^>]*>`, 'g')) ?? []

const attr = (tag: string, name: string): string | undefined =>
  tag.match(new RegExp(String.raw`\s${name}="([^"]*)"`))?.[1]

const metaContent = (html: string, key: string, value: string): string | undefined => {
  const tag = tagsOf(html, 'meta').find((t) => attr(t, key) === value)
  return tag === undefined ? undefined : attr(tag, 'content')
}

describe.each(['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html', 'setup/index.html'])('%s', (file: string) => {
  let html: string
  beforeAll(() => {
    html = withoutComments(site.read(file))
  })

  it('has a title, a description, and an OG title and description', () => {
    expect(html).toMatch(/<title>[^<]+<\/title>/)
    expect(metaContent(html, 'name', 'description')).toBeTruthy()
    expect(metaContent(html, 'property', 'og:title')).toBeTruthy()
    expect(metaContent(html, 'property', 'og:description')).toBeTruthy()
  })

  it('links an icon', () => {
    expect(tagsOf(html, 'link').some((t) => attr(t, 'rel') === 'icon' && attr(t, 'href'))).toBe(true)
  })

  it('has a skip link to an id that exists', () => {
    const skip = tagsOf(html, 'a').find((t) => / class="[^"]*\bsite-skip\b/.test(t))
    expect(skip).toBeDefined()
    const target = skip?.match(/href="#(\w+)"/)?.[1]
    expect(target).toBeTruthy()
    expect(html).toContain(`id="${target}"`)
  })

  it('has exactly one main landmark', () => {
    expect(html.match(/<main\b/g)).toHaveLength(1)
  })

  it('has no inline script and no data-theme; every script is a module with a src', () => {
    expect(html).not.toMatch(/data-theme/)
    expect(html).not.toMatch(/<script\b[^>]*>(?!\s*<\/script>)[\s\S]*?<\/script>/)
    const scripts = tagsOf(html, 'script')
    expect(scripts.length).toBeGreaterThan(0)
    for (const tag of scripts) {
      expect(attr(tag, 'type'), tag).toBe('module')
      expect(attr(tag, 'src'), tag).toBeTruthy()
    }
  })

  it('uses one hashed logo for the mark and the icon, and the file exists', () => {
    const icon = tagsOf(html, 'link').find((t) => attr(t, 'rel') === 'icon')
    const mark = tagsOf(html, 'img').find((t) => / class="[^"]*\bsite-mark\b/.test(t))
    const href = icon === undefined ? undefined : attr(icon, 'href')
    expect(href).toMatch(/^\/assets\/.*hatch-logo.*\.png$/)
    expect(mark === undefined ? undefined : attr(mark, 'src')).toBe(href)
    expect(existsSync(join(site.dir, (href ?? '').replace(/^\//, '')))).toBe(true)
  })
})

describe('the build', () => {
  it('holds the self-hosted font', () => {
    expect(readdirSync(join(site.dir, 'assets')).some((f) => f.endsWith('.woff2'))).toBe(true)
  })
})
