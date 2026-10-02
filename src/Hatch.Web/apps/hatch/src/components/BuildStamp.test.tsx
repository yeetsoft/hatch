import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BuildStamp } from './BuildStamp';
import type { ClientRevisionVerdict, Revision, RevisionDrift } from '../types';

const SHA = 'a1b2c3d4e5f60718293a4b5c6d7e8f9011121314';
const NOW = Date.now();
const THREE_HOURS_AGO = new Date(NOW - 3 * 60 * 60 * 1000).toISOString();

const revision = (over: Partial<Revision> = {}): Revision => ({
  revision: SHA,
  sequence: 1,
  builtAt: THREE_HOURS_AGO,
  client: null,
  cluster: null,
  ...over,
});

const client = (drift: RevisionDrift): ClientRevisionVerdict => ({
  revision: SHA,
  sequence: 1,
  drift,
});

describe('BuildStamp', () => {
  it('draws the short sha, the full sha on the title, and a relative phrase', () => {
    const html = renderToStaticMarkup(<BuildStamp revision={revision()} />);

    expect(html).toContain(SHA.slice(0, 7));
    expect(html).toContain(`title="${SHA}"`);
    expect(html).toContain('3 hours ago');
  });

  it('draws "dev build" with no sha and no time for a build nothing stamped', () => {
    const html = renderToStaticMarkup(<BuildStamp revision={revision({ revision: 'dev', builtAt: null })} />);

    expect(html).toContain('dev build');
    expect(html).not.toContain(SHA.slice(0, 7));
    expect(html).not.toContain('title=');
  });

  it('draws the notice and a Reload press when the client is behind', () => {
    const html = renderToStaticMarkup(<BuildStamp revision={revision({ client: client('Behind') })} />);

    expect(html).toContain('a newer build is live');
    expect(html).toContain('Reload');
  });

  it.each(['Ahead', 'Current', 'Unknown'] as const)(
    'draws no notice when the client verdict is %s',
    (drift) => {
      const html = renderToStaticMarkup(<BuildStamp revision={revision({ client: client(drift) })} />);

      expect(html).not.toContain('a newer build is live');
      expect(html).not.toContain('Reload');
    },
  );

  it('draws no notice for a null client', () => {
    const html = renderToStaticMarkup(<BuildStamp revision={revision({ client: null })} />);

    expect(html).not.toContain('a newer build is live');
    expect(html).not.toContain('Reload');
  });

  it('draws the sha with nothing where the time would be when builtAt is null', () => {
    const html = renderToStaticMarkup(<BuildStamp revision={revision({ builtAt: null })} />);

    expect(html).toContain(SHA.slice(0, 7));
    expect(html).not.toContain('·');
  });

  it('renders empty for a null revision', () => {
    expect(renderToStaticMarkup(<BuildStamp revision={null} />)).toBe('');
  });
});
