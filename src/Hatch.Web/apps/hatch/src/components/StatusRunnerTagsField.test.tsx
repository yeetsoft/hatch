import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { StatusRunnerTagsField } from './StatusRunnerTagsField';
import type { Status } from '../types';

const status = (opts: Partial<Status> = {}): Status => ({
  id: 1,
  name: 'In Review',
  sortOrder: 0,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

describe('StatusRunnerTagsField', () => {
  it('renders the tags one per line and a visually hidden label naming the column', () => {
    const html = renderToStaticMarkup(
      <StatusRunnerTagsField status={status({ runnerTags: ['breakdown', 'macos'] })} busy={false} onSave={() => {}} />,
    );

    expect(html).toContain('breakdown\nmacos');
    expect(html).toContain('<span class="hatch-visually-hidden">In Review runner tags</span>');
  });

  it('renders an empty textarea for a column with no tags', () => {
    const html = renderToStaticMarkup(<StatusRunnerTagsField status={status()} busy={false} onSave={() => {}} />);

    expect(html).toMatch(/<textarea[^>]*><\/textarea>/);
  });

  it('renders a refusal as role="alert" and still shows the typed value', () => {
    const html = renderToStaticMarkup(
      <StatusRunnerTagsField
        status={status({ runnerTags: ['macos'] })}
        busy={false}
        refusal="a tag has no space or comma inside"
        onSave={() => {}}
      />,
    );

    expect(html).toContain('<span class="hatch-field__error" role="alert">a tag has no space or comma inside</span>');
    expect(html).toContain('macos');
  });

  it('disables the textarea when busy', () => {
    const html = renderToStaticMarkup(<StatusRunnerTagsField status={status()} busy onSave={() => {}} />);

    expect(html).toMatch(/<textarea[^>]*disabled=""/);
  });
});
