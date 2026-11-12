import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PlaybookFileChip } from './PlaybookFileChip';
import type { MergeCheck } from '../types';

const check = (over: Partial<MergeCheck> = {}): MergeCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  trunkSha: '1'.repeat(40),
  verdict: 'clean',
  branch: 'aer-12-thing',
  branchSha: '2'.repeat(40),
  files: [],
  holdsTrunk: true,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  playbookFilePath: null,
  dependencyFilePath: null,
  ...over,
});

describe('PlaybookFileChip', () => {
  it('draws the sentence and path when a check carries one', () => {
    const html = renderToStaticMarkup(
      <PlaybookFileChip checks={[check({ playbookFilePath: '.hatch/playbooks.yaml' })]} />,
    );

    expect(html).toContain('this change also rewrites the instructions agents follow - .hatch/playbooks.yaml');
  });

  it('draws nothing when no check names a path', () => {
    const html = renderToStaticMarkup(<PlaybookFileChip checks={[check(), check({ canonical: 'forge.example/owner/two' })]} />);

    expect(html).toBe('');
  });

  it('draws nothing for no checks at all', () => {
    expect(renderToStaticMarkup(<PlaybookFileChip checks={[]} />)).toBe('');
  });
});
