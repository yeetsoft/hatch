import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { StatusSteps } from './StatusSteps';
import type { Status } from '../types';

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
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

const HERE = status(1, 0);
const OPEN = status(2, 1);

describe('StatusSteps', () => {
  it('disables a blocked column and carries the reason as its title', () => {
    const html = renderToStaticMarkup(
      <StatusSteps
        statuses={[HERE, OPEN]}
        statusId={HERE.id}
        onMove={() => {}}
        blocked={(target) => (target.id === OPEN.id ? 'Only an owner or approver may move work in here' : null)}
      />,
    );

    expect(html).toContain('title="Only an owner or approver may move work in here"');
    expect(html).toMatch(/status-2[^<]*<\/button>/);
    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).toContain('disabled=""');
    expect(openButton).toContain('blocked');
  });

  it('leaves an unblocked column enabled, with no title', () => {
    const html = renderToStaticMarkup(
      <StatusSteps statuses={[HERE, OPEN]} statusId={HERE.id} onMove={() => {}} blocked={() => null} />,
    );

    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).not.toContain('disabled');
    expect(openButton).not.toContain('title=');
    expect(openButton).not.toContain('blocked');
  });

  it('leaves every column enabled when blocked is omitted', () => {
    const html = renderToStaticMarkup(<StatusSteps statuses={[HERE, OPEN]} statusId={HERE.id} onMove={() => {}} />);

    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).not.toContain('disabled');
  });

  describe('the remand press', () => {
    const SHELF = status(3, 2, { isDeferred: true, name: 'Dreaming' });
    const SECOND = status(4, 3, { isDeferred: true, name: 'Someday' });
    const presses = (html: string) => html.match(/<button[^>]*hatch-status-remand[^>]*>to a dream<\/button>/g) ?? [];

    it('draws one press per deferred column, and none beside a lane', () => {
      const html = renderToStaticMarkup(
        <StatusSteps
          statuses={[HERE, OPEN, SHELF, SECOND]}
          statusId={HERE.id}
          issueKey="HA-1"
          onMove={() => {}}
          onRemand={() => {}}
        />,
      );

      const found = presses(html);
      expect(found).toHaveLength(2);
      expect(found[0]).toContain('aria-label="Remand HA-1 to a dream in Dreaming"');
      expect(found[1]).toContain('aria-label="Remand HA-1 to a dream in Someday"');
      // Each follows its own column's press.
      expect(html).toMatch(/>Dreaming<\/button><button[^>]*Remand HA-1 to a dream in Dreaming/);
    });

    it('is drawn when the issue is already in the deferred column', () => {
      const html = renderToStaticMarkup(
        <StatusSteps statuses={[HERE, SHELF]} statusId={SHELF.id} issueKey="HA-1" onMove={() => {}} onRemand={() => {}} />,
      );

      expect(presses(html)[0]).not.toContain('disabled');
    });

    it('is not drawn without onRemand', () => {
      const html = renderToStaticMarkup(
        <StatusSteps statuses={[HERE, OPEN, SHELF]} statusId={HERE.id} onMove={() => {}} />,
      );

      expect(html).not.toContain('to a dream');
    });

    it('is not drawn on a board with no deferred column', () => {
      const html = renderToStaticMarkup(
        <StatusSteps statuses={[HERE, OPEN]} statusId={HERE.id} onMove={() => {}} onRemand={() => {}} />,
      );

      expect(html).not.toContain('to a dream');
    });

    it('is disabled, with the same reason as its title, where the column is blocked', () => {
      const html = renderToStaticMarkup(
        <StatusSteps
          statuses={[HERE, SHELF, SECOND]}
          statusId={HERE.id}
          issueKey="HA-1"
          onMove={() => {}}
          onRemand={() => {}}
          blocked={(target) => (target.id === SHELF.id ? 'Only an owner may park work here' : null)}
        />,
      );

      const [first, second] = presses(html);
      expect(first).toContain('disabled=""');
      expect(first).toContain('title="Only an owner may park work here"');
      expect(second).not.toContain('disabled');
    });
  });
});
