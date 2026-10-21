/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { docPage, DOC_PAGES } from './docs';

const repoRoot = '../../../../../../';

/** Repo-relative paths of the files directly in `dir` that `keep` accepts. Only
    files: `bin/` and `obj/` under `src/Hatch.Cli/` are directories and stay unread. */
function filesIn(dir: string, keep: (name: string) => boolean): string[] {
  return readdirSync(new URL(`${repoRoot}${dir}`, import.meta.url), { withFileTypes: true })
    .filter((entry) => entry.isFile() && keep(entry.name))
    .map((entry) => `${dir}${entry.name}`)
    .sort();
}

const cliSources = filesIn('src/Hatch.Cli/', (name) => name.endsWith('.cs'));
const runnerSources = filesIn('containers/hatch-runner/', () => true);

/** Every file whose HATCH_* the variables page must name, `compose.yaml` first so
    it is the reader named when several read the same variable. */
const sources = ['compose.yaml', ...cliSources, ...runnerSources, 'scripts/hatch.sh', 'scripts/hatch.ps1'];

/** Word-bounded, so `HATCH_RUNNER` is not found inside `HATCH_RUNNER_NAME`. */
const hatchName = /\bHATCH_[A-Z][A-Z0-9_]*\b/g;

const namesBySource = new Map<string, string[]>(
  sources.map((path) => {
    const text = readFileSync(new URL(`${repoRoot}${path}`, import.meta.url), 'utf8');
    return [path, [...new Set(text.match(hatchName) ?? [])]];
  }),
);

/** Each distinct HATCH_* name, and the first source that reads it. */
const readers = new Map<string, string>();
for (const [path, names] of namesBySource) {
  for (const name of names) if (!readers.has(name)) readers.set(name, path);
}

const countIn = (paths: string[]) => paths.reduce((sum, path) => sum + (namesBySource.get(path)?.length ?? 0), 0);

/** The sources a move or rename would silently empty: the CLI as a group, since
    most of its files read no variable, and every other file on its own. */
const groups: [string, number][] = [
  ['compose.yaml', countIn(['compose.yaml'])],
  ['src/Hatch.Cli/*.cs', countIn(cliSources)],
  ...runnerSources.map((path): [string, number] => [path, countIn([path])]),
  ['scripts/hatch.sh', countIn(['scripts/hatch.sh'])],
  ['scripts/hatch.ps1', countIn(['scripts/hatch.ps1'])],
];

const page = docPage(DOC_PAGES, 'environment-variables');

describe('environment-variables names every HATCH_* the stack, CLI, runner container and wrappers read', () => {
  it.each([...readers.keys()].sort())('%s', (name) => {
    expect(page).toBeDefined();
    const named = new RegExp(`\\b${name}\\b`).test(page!.body);
    expect(
      named,
      `environment-variables.md does not name ${name} (read by ${readers.get(name)}): add a row for it`,
    ).toBe(true);
  });
});

describe('every source the variables check reads yields a HATCH_* name', () => {
  it.each(groups)('%s', (source, count) => {
    expect(
      count,
      `${source} yields no HATCH_* name: moved or renamed? update the sources in docsCoverage.variables.test.ts`,
    ).toBeGreaterThan(0);
  });
});
