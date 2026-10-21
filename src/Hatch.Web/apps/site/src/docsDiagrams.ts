/* Draws every `.hatch-docs-mermaid` placeholder on a docs page that has one,
   and redraws on a theme change - the site's equivalent of `DocsPage.tsx`'s
   effect, with no React to run it. `vite-plugin-docs.ts` only emits the
   `<script>` that loads this file on a page `renderDocs` found a fence on, so
   there is no `mermaidCount === 0` guard here: `drawMermaid` is already a
   no-op when its root holds no placeholder.

   `watchSystemTheme` is the site's only theme signal - no Auto/Light/Dark
   switch here, `theme.ts` says why - so this answers it directly rather than
   through a `resolved` state the way `ThemeProvider` does for the app. */
import { drawMermaid } from '../../hatch/src/lib/mermaidDraw'
import { watchSystemTheme } from '@hatch/ui/themeStore'

let cancel = drawMermaid(document, (what, e) => console.error(what, e))
watchSystemTheme(() => {
  cancel()
  cancel = drawMermaid(document, (what, e) => console.error(what, e))
})
