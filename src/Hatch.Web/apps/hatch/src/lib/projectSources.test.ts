/* Every picker, the new-issue default and the `?scope=` filter draw from
   `GET /api/projects`, which the server filters to the caller's scope. The
   guarantee holds only while nothing else becomes a source of projects, so
   this reads the sources: a file that calls `getProjects` must be on the list
   below, and a new one fails here until somebody has looked at it. */
/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const root = new URL('../', import.meta.url);

const sourcesOf = (dir: URL): string[] =>
  readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const next = new URL(entry.name + (entry.isDirectory() ? '/' : ''), dir);
    if (entry.isDirectory()) return sourcesOf(next);
    return /\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name) ? [next.pathname] : [];
  });

/** Where the project list is read, by file. Each reads the one endpoint. */
const KNOWN = [
  'api/client.ts',
  'components/FindingBugDialog.tsx',
  'components/TrunkBuildBugDialog.tsx',
  'lib/useProjects.ts',
  'pages/ActivityReportPage.tsx',
  'pages/BulkPage.tsx',
  'pages/ChangelogReportPage.tsx',
  'pages/ConsumptionReportPage.tsx',
  'pages/CumulativeFlowPage.tsx',
  'pages/PlanPage.tsx',
  'pages/ProjectsPage.tsx',
  'pages/TimelinePage.tsx',
  'pages/UsersPage.tsx',
];

describe('where the app reads its projects', () => {
  const files = sourcesOf(root).map((path) => ({
    path: path.slice(root.pathname.length),
    text: readFileSync(new URL('file://' + path), 'utf8').replace(/\/\*[\s\S]*?\*\/|^\s*\/\/.*$/gm, ''),
  }));

  it('is the one endpoint, from a known list of files', () => {
    const callers = files.filter((file) => /\bgetProjects\b/.test(file.text)).map((file) => file.path);

    expect(callers.sort()).toEqual([...KNOWN].sort());
  });

  it('is never a second route to the project list', () => {
    const others = files
      .filter((file) => /['"`]\/api\/projects['"`]/.test(file.text))
      .map((file) => file.path);

    expect(others).toEqual(['api/client.ts']);
  });

  it('is what the scope filter and the default project are given', () => {
    const useScope = files.find((file) => file.path === 'lib/useScope.tsx')!.text;
    const dialog = files.find((file) => file.path === 'components/NewIssueDialog.tsx')!.text;

    expect(useScope).toMatch(/useProjects\(\)/);
    expect(dialog).not.toMatch(/getProjects/);
  });
});
