/* The chunk graph a real build produces, walked for two different guards:
   `docsDiagrams.test.ts` asks which pages reach mermaid at all, and
   `thirdParty.test.ts` asks which chunks are mermaid's own dependencies -
   the one subtree allowed to carry the absolute URLs `isAllowedMermaidUrl`
   names. Both walks are the same walk: mermaid's facade chunk is reached
   only through a dynamic `import()` (from `mermaidDraw.ts`), and each
   diagram-type renderer is a further dynamic `import()` from the facade, so
   a walk over `imports` alone can never find it - `dynamicImports` has to
   come along. */

/** The fields this module reads off a built chunk. Kept local, rather than
    imported from rolldown's own (unlisted, transitive) types, the same
    choice `mermaidChunk.test.ts` makes. */
export interface Chunk {
  type: string
  fileName: string
  moduleIds: string[]
  imports: string[]
  dynamicImports: string[]
}

export function reachable(chunks: Chunk[], startFileNames: Iterable<string>): Set<string> {
  const byFileName = new Map(chunks.map((chunk) => [chunk.fileName, chunk]))
  const seen = new Set<string>()
  const queue = [...startFileNames]

  while (queue.length > 0) {
    const fileName = queue.pop()!
    if (seen.has(fileName)) continue
    seen.add(fileName)

    const chunk = byFileName.get(fileName)
    if (!chunk) continue
    for (const next of [...chunk.imports, ...chunk.dynamicImports]) {
      if (!seen.has(next)) queue.push(next)
    }
  }

  return seen
}

// mermaid's own dependency tree vendors a closed set of non-network absolute
// URLs: XML/SVG/MathML namespace URIs and elkjs's own Ecore namespace
// strings, read as plain strings and never fetched; doc/issue-tracker links
// folded into logged error messages; and marked's bare-www-autolink rule,
// which builds an href by string-prepending a bare scheme literal onto
// diagram text, naming no host of its own - an exact match, not a prefix, so
// it cannot swallow anything that follows it. Each is a pinned dependency's
// own vendor URL, true of every installation, not this one's - ethos.test.ts's
// own "exempt-marker" opt-out (ethos:allow) is what lets them stay plain,
// searchable text here rather than split to dodge that scan.
const ALLOWED_MERMAID_URL_PREFIXES = [
  'http://www.w3.org/', // XML/SVG/MathML namespace URIs - ethos:allow
  'http://www.eclipse.org/', // elkjs's own Ecore namespace strings - ethos:allow
  'http:///org/eclipse/', // elkjs's own Ecore namespace strings - ethos:allow
  'https://chevrotain.io/', // ethos:allow
  'https://github.com/chevrotain/chevrotain/', // ethos:allow
  'https://en.wikipedia.org/wiki/LL_parser', // chevrotain's parser-error strings - ethos:allow
  'https://github.com/markedjs/marked', // mermaid's vendored marked - ethos:allow
  'https://github.com/mermaid-js/mermaid/', // mermaid's own issue tracker/release notes - ethos:allow
  'https://langium.org/', // ethos:allow
  'https://rolldown.rs/', // vendored-dependency error strings - ethos:allow
]

export function isAllowedMermaidUrl(url: string): boolean {
  if (url === 'http://') return true // ethos:allow
  return ALLOWED_MERMAID_URL_PREFIXES.some((prefix) => url.startsWith(prefix))
}
