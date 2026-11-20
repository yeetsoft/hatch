import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { LookBackPresses } from './LookBackPresses';

vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const noop = () => undefined;

const render = (ambitionKey: string | null | undefined) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <LookBackPresses
        issueKey="HA-12"
        questionId={3}
        options={null}
        wentAheadOn="Change it"
        ambitionKey={ambitionKey}
        onDone={noop}
      />
    </MemoryRouter>,
  );

describe('LookBackPresses', () => {
  it('draws the promote checkbox when the question has an ambition above it', () => {
    expect(render('HA-9')).toContain('Make this a principle');
  });

  it('draws no checkbox where the question has no ambition above it', () => {
    expect(render(null)).not.toContain('Make this a principle');
    expect(render(undefined)).not.toContain('Make this a principle');
  });

  it('still draws Keep and Overturn', () => {
    const markup = render('HA-9');
    expect(markup).toContain('Keep');
    expect(markup).toContain('Overturn');
  });
});
