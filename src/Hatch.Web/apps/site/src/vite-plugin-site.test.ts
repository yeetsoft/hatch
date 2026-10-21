import { describe, expect, it } from 'vitest'
import { renderTemplate, type SiteHtmlValues } from '../vite-plugin-site'

const FIXTURE = [
  '<!doctype html>',
  '<html>',
  '  <head>',
  '    <title>Hatch</title>',
  '    <!--site:canonical-->',
  '  </head>',
  '  <body>',
  '    <footer>',
  '      <!--site:repository-->',
  '      <a href="{{repository-url}}">{{repository-url}}</a> on GitHub.',
  '      <!--/site:repository-->',
  '    </footer>',
  '    <p><!--site:clone-url--></p>',
  '  </body>',
  '</html>',
  '',
].join('\n')

const NONE: SiteHtmlValues = { canonical: undefined, repositoryUrl: undefined, cloneUrl: undefined }

describe('renderTemplate', () => {
  describe('canonical', () => {
    it('becomes a canonical link and an og:url meta when set', () => {
      const html = renderTemplate(FIXTURE, { ...NONE, canonical: 'https://example.test/' })
      expect(html).toContain(
        '    <link rel="canonical" href="https://example.test/" />\n' +
          '    <meta property="og:url" content="https://example.test/" />\n',
      )
      expect(html).not.toMatch(/site:canonical/)
    })

    it('is removed with no empty tag and no leftover comment when unset', () => {
      const html = renderTemplate(FIXTURE, NONE)
      expect(html).not.toMatch(/site:canonical/)
      expect(html).not.toMatch(/<link rel="canonical"/)
      expect(html).not.toMatch(/og:url/)
      expect(html).toBe(
        [
          '<!doctype html>',
          '<html>',
          '  <head>',
          '    <title>Hatch</title>',
          '  </head>',
          '  <body>',
          '    <footer>',
          '    </footer>',
          `    <p>${'&lt;the repository\'s clone URL&gt;'}</p>`,
          '  </body>',
          '</html>',
          '',
        ].join('\n'),
      )
    })
  })

  describe('repository', () => {
    it('fills in every {{repository-url}} occurrence inside the block when set', () => {
      const html = renderTemplate(FIXTURE, { ...NONE, repositoryUrl: 'https://example.test/owner/repo' })
      expect(html).toContain(
        '      <a href="https://example.test/owner/repo">https://example.test/owner/repo</a> on GitHub.\n',
      )
      expect(html).not.toMatch(/site:repository/)
    })

    it('removes the whole block - markers, element and surrounding whitespace - when unset', () => {
      const html = renderTemplate(FIXTURE, NONE)
      expect(html).not.toMatch(/site:repository/)
      expect(html).not.toMatch(/<a /)
      expect(html).toContain('    <footer>\n    </footer>')
    })
  })

  describe('clone-url', () => {
    it('becomes the value when set', () => {
      const html = renderTemplate(FIXTURE, { ...NONE, cloneUrl: 'git@example.test:owner/repo.git' })
      expect(html).toContain('<p>git@example.test:owner/repo.git</p>')
      expect(html).not.toMatch(/site:clone-url/)
    })

    it("becomes the escaped fallback text matching site/index.md's default when unset", () => {
      const html = renderTemplate(FIXTURE, NONE)
      expect(html).toContain("<p>&lt;the repository's clone URL&gt;</p>")
      expect(html).not.toMatch(/site:clone-url/)
    })
  })

  describe('escaping', () => {
    it('escapes &, <, > and " the same way in an attribute and in text content', () => {
      const dangerous = 'https://example.test/<owner>&name="repo"'
      const escaped = 'https://example.test/&lt;owner&gt;&amp;name=&quot;repo&quot;'

      const html = renderTemplate(FIXTURE, { ...NONE, repositoryUrl: dangerous })

      expect(html).toContain(`<a href="${escaped}">${escaped}</a>`)
      expect(html).not.toContain(dangerous)
    })
  })
})
