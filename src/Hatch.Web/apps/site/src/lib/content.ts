/** One page of `apps/site/content/`: the file's frontmatter, and the markdown
    after it. */
export interface ContentPage {
  slug: string
  title: string
  summary: string
  body: string
}

const CONTENT_KEYS = ['title', 'summary'] as const
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

/** Reads one markdown file into a page. Every way the file can be wrong throws
    and names `path`, so a bad page fails the build at the file to fix rather
    than rendering as a blank. The body is returned LF-only, with the
    frontmatter gone. */
export function parseContentPage(path: string, raw: string): ContentPage {
  const fail = (why: string): never => {
    throw new Error(`${path}: ${why}`)
  }

  const file = path.slice(path.lastIndexOf('/') + 1)
  if (!file.endsWith('.md')) fail('a content page must be a .md file')
  const slug = file.slice(0, -'.md'.length)
  if (!SLUG.test(slug)) fail(`the file name "${slug}" must be lowercase words joined by hyphens`)

  const lines = raw.split(/\r?\n/)
  if (lines[0] !== '---') fail('must open with a --- frontmatter line')
  const close = lines.indexOf('---', 1)
  if (close === -1) fail('frontmatter is not closed by a --- line')

  const fields = new Map<string, string>()
  for (const line of lines.slice(1, close)) {
    const colon = line.indexOf(':')
    if (colon === -1) fail(`frontmatter line "${line}" is not "key: value"`)
    const key = line.slice(0, colon).trim()
    const value = line.slice(colon + 1).trim()
    if (!(CONTENT_KEYS as readonly string[]).includes(key)) fail(`unknown frontmatter key "${key}"`)
    if (fields.has(key)) fail(`frontmatter key "${key}" appears more than once`)
    if (value === '') fail(`frontmatter key "${key}" has no value`)
    fields.set(key, value)
  }
  for (const key of CONTENT_KEYS) {
    if (!fields.has(key)) fail(`frontmatter is missing \`${key}\``)
  }

  return {
    slug,
    title: fields.get('title')!,
    summary: fields.get('summary')!,
    body: lines
      .slice(close + 1)
      .join('\n')
      .replace(/^\n+/, ''),
  }
}
