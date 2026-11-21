import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CreateRunnerDialog } from './CreateRunnerDialog';

// The useLoaded(getPeople) call and the open effect's suggestRunnerName/
// getAuthMe calls fire inside useEffect, which renderToStaticMarkup never
// runs - the same reason ProjectEditDialog.test.tsx needs no network stub.

const HINT =
  'Whose tickets this key&#x27;s --mine reaches. The key sees only the projects its owner sees; with nobody, it sees every project.';

describe('CreateRunnerDialog', () => {
  it('draws the Name hint, Cancel before Create, and Create disabled on first render', () => {
    const html = renderToStaticMarkup(<CreateRunnerDialog open onClose={() => {}} onCreated={() => {}} chooseOwner />);

    expect(html).toContain('A name from the cast list; yours to change. It is the runner&#x27;s identity and cannot be renamed.');
    expect(html.indexOf('>Cancel</button>')).toBeLessThan(html.indexOf('>Create</button>'));
    expect(html).toMatch(/<button[^>]*\bdisabled\b[^>]*>Create<\/button>/);
  });

  it('with chooseOwner, draws the Owner select with nobody, and the scope hint', () => {
    const html = renderToStaticMarkup(<CreateRunnerDialog open onClose={() => {}} onCreated={() => {}} chooseOwner />);

    expect(html).toContain('<select');
    expect(html).toContain('>nobody</option>');
    expect(html).toContain('Owner');
    expect(html).toContain(HINT);
    expect(html).not.toContain('will be yours');
  });

  it('without chooseOwner, draws no select, says the runner will be theirs, and keeps the scope hint', () => {
    const html = renderToStaticMarkup(
      <CreateRunnerDialog open onClose={() => {}} onCreated={() => {}} chooseOwner={false} />,
    );

    expect(html).not.toContain('<select');
    expect(html).not.toContain('>nobody</option>');
    expect(html).toContain('The runner will be yours.');
    expect(html).toContain(HINT);
    expect(html).toContain('Suggest another');
  });

  it('without chooseOwner, Create is not held waiting for an owner to load', () => {
    const html = renderToStaticMarkup(
      <CreateRunnerDialog open onClose={() => {}} onCreated={() => {}} chooseOwner={false} />,
    );

    // The name is empty on first render, so Create is disabled for that reason alone.
    expect(html).toMatch(/<button[^>]*\bdisabled\b[^>]*>Create<\/button>/);
  });

  it('draws nothing while closed', () => {
    expect(
      renderToStaticMarkup(<CreateRunnerDialog open={false} onClose={() => {}} onCreated={() => {}} chooseOwner />),
    ).toBe('');
  });
});
