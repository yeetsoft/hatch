import type { Plugin } from 'vite'

/** The values HA-981's siteEnv() computes, in the shape this plugin consumes. */
export interface SiteHtmlValues {
  canonical: string | undefined
  repositoryUrl: string | undefined
  cloneUrl: string | undefined
}

/** `site/index.md:83`'s own fallback, as a markdown code block renders it: already entity-encoded. */
const CLONE_URL_FALLBACK = "&lt;the repository's clone URL&gt;"

const CANONICAL_RE = /^([ \t]*)<!--site:canonical-->[ \t]*\r?\n?/m
const REPOSITORY_RE = /^[ \t]*<!--site:repository-->[ \t]*\r?\n?([\s\S]*?)^[ \t]*<!--\/site:repository-->[ \t]*\r?\n?/m
const CLONE_URL_RE = /<!--site:clone-url-->/g

export function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

/**
 * Replaces the `site:*` markers in `html` with the given values, or removes
 * them cleanly when a value is undefined - no empty tag, no leftover comment.
 * Pure, so tests can call it on a fixture string with no Vite build involved.
 */
export function renderTemplate(html: string, values: SiteHtmlValues): string {
  let result = html

  result = result.replace(CANONICAL_RE, (_match, indent: string) => {
    if (values.canonical === undefined) return ''
    const href = escapeHtml(values.canonical)
    return `${indent}<link rel="canonical" href="${href}" />\n${indent}<meta property="og:url" content="${href}" />\n`
  })

  result = result.replace(REPOSITORY_RE, (_match, inner: string) => {
    if (values.repositoryUrl === undefined) return ''
    return inner.replace(/\{\{repository-url\}\}/g, escapeHtml(values.repositoryUrl!))
  })

  result = result.replace(
    CLONE_URL_RE,
    values.cloneUrl === undefined ? CLONE_URL_FALLBACK : escapeHtml(values.cloneUrl),
  )

  return result
}

export function siteHtml(values: SiteHtmlValues): Plugin {
  return {
    name: 'site-html',
    transformIndexHtml: {
      order: 'pre',
      handler: (html) => renderTemplate(html, values),
    },
  }
}
