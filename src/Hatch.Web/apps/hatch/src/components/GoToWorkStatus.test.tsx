import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { GoToWorkStatus } from './GoToWorkStatus';
import type { StatusLine } from '../lib/goToWork';

const line = (over: Partial<StatusLine> = {}): StatusLine => ({
  before: 'Working ',
  key: 'FIL-97',
  after: ' · In Progress',
  tone: 'success',
  more: 0,
  ...over,
});

const render = (status: StatusLine) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <GoToWorkStatus status={status} />
    </MemoryRouter>,
  );

describe('GoToWorkStatus', () => {
  it('links the ticket it names', () => {
    const html = render(line());

    expect(html).toContain('href="/issues/FIL-97"');
    expect(html).toContain('Working ');
    expect(html).toContain('In Progress');
  });

  it('says how many other loops it leaves out', () => {
    expect(render(line({ more: 1 }))).toContain(' and 1 more');
    expect(render(line({ more: 3 }))).toContain(' and 3 more');
    expect(render(line())).not.toContain('more');
  });

  it('carries its tone as a class of its own', () => {
    expect(render(line({ tone: 'danger' }))).toContain('hatch-go-to-work-status--danger');
  });

  it('is a status region', () => {
    expect(render(line())).toContain('role="status"');
  });

  it('draws a keyless line as its words alone', () => {
    const html = render(line({ before: 'Paused', key: null, after: '', tone: 'primary' }));

    expect(html).toContain('Paused');
    expect(html).not.toContain('<a');
  });

  it('is nothing for the empty line', () => {
    expect(render(line({ before: '', key: null, after: '', tone: 'muted' }))).toBe('');
  });
});
