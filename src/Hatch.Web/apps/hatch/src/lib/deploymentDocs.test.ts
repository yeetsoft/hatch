/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { docPage, DOC_PAGES } from './docs';

const repoRoot = '../../../../../../';

const authOptions = readFileSync(
  new URL(`${repoRoot}src/Hatch.Api/Services/Auth/AuthOptions.cs`, import.meta.url),
  'utf8',
);
const program = readFileSync(new URL(`${repoRoot}src/Hatch.Api/Program.cs`, import.meta.url), 'utf8');
const authGate = readFileSync(
  new URL(`${repoRoot}src/Hatch.Api/Services/Auth/AuthGate.cs`, import.meta.url),
  'utf8',
);

const deploymentOverview = docPage(DOC_PAGES, 'deployment-overview');
const kubernetes = docPage(DOC_PAGES, 'kubernetes');

/** Every distinct path `Program.cs` maps with `MapHealthChecks("...")`. */
const healthPaths = [...new Set([...program.matchAll(/MapHealthChecks\(\s*"([^"]+)"/g)].map(([, path]) => path))].sort();

/** The text of every inline code span in a markdown body. */
function codeSpans(markdown: string): string[] {
  return [...markdown.matchAll(/`([^`\n]+)`/g)].map(([, span]) => span);
}

/** The body of the named class, by brace-counting from its opening `{`. */
function classBody(source: string, className: string): string {
  const header = new RegExp(`class ${className}\\b[^{]*\\{`).exec(source);
  if (!header) throw new Error(`class ${className} not found in AuthOptions.cs`);
  let depth = 1;
  let i = header.index + header[0].length;
  while (depth > 0) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}') depth--;
    i++;
  }
  return source.slice(header.index + header[0].length, i - 1);
}

/** Every `public <type> <Name> { get; set; }` auto-property in a class body - the
    shape config classes bind from, not the `=>` computed properties beside them. */
function autoProperties(body: string): { type: string; name: string }[] {
  return [...body.matchAll(/public\s+([\w<>[\],? ]+?)\s+(\w+)\s*\{\s*get;\s*set;/g)].map(([, type, name]) => ({
    type: type.trim(),
    name,
  }));
}

/** Every `Auth__*` environment key `AuthOptions.cs` binds: a top-level property as
    `Auth__<Name>`, `LocalPerson`/`Google` recursed into their own options class as
    `Auth__LocalPerson__<Name>`/`Auth__Google__<Name>`, and an array property with
    a trailing `__0`. */
function authKeys(): string[] {
  const keys: string[] = [];
  for (const { type, name } of autoProperties(classBody(authOptions, 'AuthOptions'))) {
    if (name === 'LocalPerson' || name === 'Google') continue;
    keys.push(type.endsWith('[]') ? `Auth__${name}__0` : `Auth__${name}`);
  }
  for (const [outer, nestedClass] of [
    ['LocalPerson', 'LocalPersonOptions'],
    ['Google', 'GoogleAuthOptions'],
  ]) {
    for (const { type, name } of autoProperties(classBody(authOptions, nestedClass))) {
      keys.push(type.endsWith('[]') ? `Auth__${outer}__${name}__0` : `Auth__${outer}__${name}`);
    }
  }
  return keys;
}

describe('deployment-overview names every Auth__* key AuthOptions.cs binds', () => {
  it.each(authKeys())('%s', (key) => {
    expect(deploymentOverview).toBeDefined();
    expect(new RegExp(`\\b${key}\\b`).test(deploymentOverview!.body)).toBe(true);
  });
});

describe('Program.cs health paths', () => {
  it('maps at least one, so the blocks below are not vacuous', () => {
    expect(healthPaths.length).toBeGreaterThan(0);
  });
});

describe('kubernetes names every health path Program.cs maps', () => {
  it.each(healthPaths)('%s', (path) => {
    expect(kubernetes).toBeDefined();
    // `GET /health/live` counts; `/health/*` does not.
    expect(codeSpans(kubernetes!.body).some((span) => span.split(/\s+/).includes(path))).toBe(true);
  });
});

describe('AuthGate.cs exempts every health path Program.cs maps', () => {
  it.each(healthPaths)('%s', (path) => {
    expect(authGate.includes(`"${path}"`)).toBe(true);
  });
});
