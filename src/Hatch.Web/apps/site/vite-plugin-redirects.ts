import type { Plugin } from 'vite'
import { escapeHtml } from './vite-plugin-site'
import { docHref } from './vite-plugin-docs'

/** The five addresses Jekyll served that have no page on this site, each to the
    first page of the section that replaced it. */
export const REDIRECTS: { from: string; to: string }[] = [
  { from: 'how-to-hatch', to: 'the-board-and-its-columns' },
  { from: 'running-the-agent', to: 'the-runner' },
  { from: 'container-runner', to: 'the-container-runner' },
  { from: 'agent-manual', to: 'cli-reference' },
  { from: 'operating-the-stack', to: 'deployment-overview' },
]

/** A retired address's stub: a `<meta http-equiv="refresh">` to `href` and a link
    the reader can press if the refresh is blocked, carrying `<link
    rel="canonical">` at `canonical` only when it is given - the same way
    `renderSitemap` is skipped with no `base_url`. Pure, so a test can call it
    with no build. */
export function renderRedirect(href: string, canonical: string | undefined): string {
  const safeHref = escapeHtml(href)
  const canonicalLink = canonical === undefined ? '' : `\n    <link rel="canonical" href="${escapeHtml(canonical)}" />`
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <title>Hatch: page moved</title>
    <meta http-equiv="refresh" content="0; url=${safeHref}" />${canonicalLink}
  </head>
  <body>
    <p><a href="${safeHref}">This page has moved.</a></p>
  </body>
</html>
`
}

/** Writes the five stubs in `REDIRECTS`, each refreshing to its target's address
    under `base` (`config.base`, read in `configResolved` the way `docsHtml` and
    `contentHtml` both do) and carrying a canonical link at `canonical`'s address
    when it is set. */
export function redirects(canonical: string | undefined): Plugin {
  let base = '/'

  return {
    name: 'redirects',
    configResolved(resolvedConfig) {
      base = resolvedConfig.base
    },
    generateBundle() {
      for (const { from, to } of REDIRECTS) {
        const href = docHref(base, to, '')
        const absolute = canonical === undefined ? undefined : `${canonical}docs/${to}/`
        this.emitFile({ type: 'asset', fileName: `${from}/index.html`, source: renderRedirect(href, absolute) })
      }
    },
  }
}
