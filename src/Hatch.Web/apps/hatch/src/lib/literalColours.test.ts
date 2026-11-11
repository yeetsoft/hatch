/* The audit that HA-2260 cleared, turned into a test that refuses the next
   literal. A literal hex, rgb() or hsl() colour anywhere under apps or
   packages - outside a test file - is a value with no answer for the other
   theme: it belongs in packages/ui/src/tokens.css, the one file this walk
   exempts, because that is the one place literals are meant to live. */
/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const ROOT = new URL('../../../../', import.meta.url);

const SKIP_DIRS = new Set(['node_modules', 'dist']);
const EXEMPT_FILES = new Set(['packages/ui/src/tokens.css']);

/** Case-insensitive: a hex literal not already part of a longer run of hex
    digits or word characters (so it does not clip a longer id), and not an
    HTML entity (`&#8595;`) or an id selector (`#header`); a functional
    `rgb(`/`rgba(`/`hsl(`/`hsla(` call. */
const COLOUR = /(?<![&\w])#(?:[0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})(?![\w-])|\b(?:rgb|hsl)a?\(/gi;

interface AllowlistEntry {
  file: string;
  literal: string;
  reason: string;
}

const ALLOWLIST: readonly AllowlistEntry[] = [
  {
    file: 'packages/ui/src/color.ts',
    literal: '#6b7280',
    reason: 'the fallback for contrast arithmetic on operator colours',
  },
  {
    file: 'packages/ui/src/color.ts',
    literal: '#1a1a1a',
    reason: 'the ink pair for contrast arithmetic on operator colours',
  },
  {
    file: 'packages/ui/src/color.ts',
    literal: '#ffffff',
    reason: 'the ink pair for contrast arithmetic on operator colours',
  },
  {
    file: 'apps/hatch/src/lib/color.ts',
    literal: '#6b7280',
    reason: "the status default, the API's own",
  },
  {
    file: 'packages/ui/src/brand/hatchMark.ts',
    literal: '#0066cc',
    reason: 'an export gradient stop, fetched outside any document',
  },
  {
    file: 'packages/ui/src/brand/hatchMark.ts',
    literal: '#4b2fc4',
    reason: 'an export gradient stop, fetched outside any document',
  },
  {
    file: 'packages/ui/src/brand/hatchMark.ts',
    literal: '#0052a3',
    reason: 'an export gradient stop, fetched outside any document',
  },
  {
    file: 'packages/ui/src/brand/hatchMark.ts',
    literal: '#ffffff',
    reason: 'the icon egg, fetched outside any document',
  },
  {
    file: 'apps/design/src/pages/ProjectMarkPage.tsx',
    literal: '#7a5cff',
    reason: 'a sample project colour - operator data',
  },
  {
    file: 'packages/ui/src/theme/palette/oklch.ts',
    literal: 'rgba(',
    reason: 'formatRgba builds the string from its arguments - a pattern, not a literal colour',
  },
];

/** Replaces a match with the same number of newlines it contained, so every
    later line number in the file still points at the line it always did. */
function blank(match: string): string {
  return '\n'.repeat((match.match(/\n/g) ?? []).length);
}

/** Strips comments so a colour written inside one is not flagged: block
    comments from every file, and line comments from .ts/.tsx alone - CSS has
    no `//` comment syntax, and a `//` there is more likely a URL. The `:`
    lookbehind spares `https://` in a string or an attribute. */
function stripComments(text: string, path: string): string {
  let out = text.replace(/\/\*[\s\S]*?\*\//g, blank);
  if (path.endsWith('.ts') || path.endsWith('.tsx')) {
    out = out.replace(/(?<!:)\/\/.*$/gm, '');
  }
  return out;
}

function isSourceFile(name: string): boolean {
  if (name.endsWith('.test.ts') || name.endsWith('.test.tsx')) return false;
  return name.endsWith('.css') || name.endsWith('.ts') || name.endsWith('.tsx');
}

/** Every source file under `apps` and `packages`, as a path relative to the
    repository's Hatch.Web root - sorted, so a failure's file order is
    deterministic. */
function sourceFiles(): string[] {
  const out: string[] = [];

  function walk(dir: string, relative: string): void {
    const entries = readdirSync(new URL(dir, ROOT), { withFileTypes: true }).sort((a, b) =>
      a.name.localeCompare(b.name),
    );
    for (const entry of entries) {
      if (SKIP_DIRS.has(entry.name)) continue;
      const entryRelative = relative === '' ? entry.name : `${relative}/${entry.name}`;
      if (entry.isDirectory()) {
        walk(`${dir}${entry.name}/`, entryRelative);
      } else if (isSourceFile(entry.name) && !EXEMPT_FILES.has(entryRelative)) {
        out.push(entryRelative);
      }
    }
  }

  walk('apps/', 'apps');
  walk('packages/', 'packages');

  return out.sort();
}

interface Hit {
  file: string;
  line: number;
  literal: string;
}

function hitsIn(path: string): Hit[] {
  const raw = readFileSync(new URL(path, ROOT), 'utf8');
  const text = stripComments(raw, path);
  const hits: Hit[] = [];

  for (const match of text.matchAll(COLOUR)) {
    const line = text.slice(0, match.index).split('\n').length;
    hits.push({ file: path, line, literal: match[0].toLowerCase() });
  }

  return hits;
}

describe('literal colours', () => {
  const files = sourceFiles();
  const allHits = files.flatMap(hitsIn);
  const allowedUsed = new Set<AllowlistEntry>();

  const flagged = allHits.filter((hit) => {
    const entry = ALLOWLIST.find((a) => a.file === hit.file && a.literal === hit.literal);
    if (entry) allowedUsed.add(entry);
    return !entry;
  });

  it('writes no literal hex, rgb() or hsl() colour outside the allowlist', () => {
    const report = flagged.map((hit) => `${hit.file}:${hit.line} ${hit.literal}`).join('\n');
    expect(flagged, report).toEqual([]);
  });

  it('keeps no allowlist entry that matches nothing', () => {
    const stale = ALLOWLIST.filter((entry) => !allowedUsed.has(entry));
    const report = stale.map((entry) => `${entry.file} ${entry.literal}`).join('\n');
    expect(stale, report).toEqual([]);
  });

  it('flags hex literals of every licensed length and both colour functions', () => {
    expect([...'#abc #abcd #ab1234 #ab1234cd rgb(1,2,3) rgba(1,2,3,.5) hsl(1,2%,3%) HSLA(1,2%,3%,.5)'.matchAll(COLOUR)])
      .toHaveLength(8);
  });

  it('spares an HTML entity, an id selector and a value inside a comment', () => {
    expect('&#8595;'.match(COLOUR)).toBeNull();
    expect('#header { color: red; }'.match(COLOUR)).toBeNull();
    expect(stripComments('/* #abc123 */', 'x.ts').match(COLOUR)).toBeNull();
    expect(stripComments('// #abc123\ncode', 'x.ts').match(COLOUR)).toBeNull();
  });
});
