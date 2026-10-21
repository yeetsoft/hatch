/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { docPage, DOC_PAGES } from './docs';

const repoRoot = '../../../../../../';

const compose = readFileSync(new URL(`${repoRoot}compose.yaml`, import.meta.url), 'utf8');
const authOptions = readFileSync(
  new URL(`${repoRoot}src/Hatch.Api/Services/Auth/AuthOptions.cs`, import.meta.url),
  'utf8',
);

const environmentVariables = docPage(DOC_PAGES, 'environment-variables');
const deploymentOverview = docPage(DOC_PAGES, 'deployment-overview');

/** Every distinct HATCH_* name in `compose.yaml`, word-bounded so `HATCH_RUNNER`
    is not found inside `HATCH_RUNNER_NAME`. */
const hatchVariables = [...new Set(compose.match(/\bHATCH_[A-Z][A-Z0-9_]*\b/g) ?? [])].sort();

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

describe('environment-variables names every HATCH_* in compose.yaml', () => {
  it.each(hatchVariables)('%s', (name) => {
    expect(environmentVariables).toBeDefined();
    expect(new RegExp(`\\b${name}\\b`).test(environmentVariables!.body)).toBe(true);
  });
});

describe('deployment-overview names every Auth__* key AuthOptions.cs binds', () => {
  it.each(authKeys())('%s', (key) => {
    expect(deploymentOverview).toBeDefined();
    expect(new RegExp(`\\b${key}\\b`).test(deploymentOverview!.body)).toBe(true);
  });
});
