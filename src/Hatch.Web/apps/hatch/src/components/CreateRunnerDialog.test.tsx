import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CreateRunnerDialog } from './CreateRunnerDialog';

// The useLoaded(getPeople) call and the open effect's suggestRunnerName/
// getAuthMe calls fire inside useEffect, which renderToStaticMarkup never
// runs - the same reason ProjectEditDialog.test.tsx needs no network stub.

describe('CreateRunnerDialog', () => {
  it('draws the Name hint, Cancel before Create, and Create disabled on first render', () => {
    const html = renderToStaticMarkup(<CreateRunnerDialog open onClose={() => {}} onCreated={() => {}} />);

    expect(html).toContain('A name from the cast list; yours to change. It is the runner&#x27;s identity and cannot be renamed.');
    expect(html.indexOf('>Cancel</button>')).toBeLessThan(html.indexOf('>Create</button>'));
    expect(html).toMatch(/<button[^>]*\bdisabled\b[^>]*>Create<\/button>/);
  });

  it('draws nothing while closed', () => {
    expect(renderToStaticMarkup(<CreateRunnerDialog open={false} onClose={() => {}} onCreated={() => {}} />)).toBe('');
  });
});
