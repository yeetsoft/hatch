import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WipOverrideDialog } from './WipOverrideDialog';
import type { WipRefusal } from '../types';

const REFUSAL: WipRefusal = { error: 'the WIP section is full - 5 of 5 stories and bugs are in it', load: 5, limit: 5 };
const noop = () => undefined;

describe('WipOverrideDialog', () => {
  it('renders nothing when nothing is being asked', () => {
    const html = renderToStaticMarkup(
      <WipOverrideDialog asking={null} busy={false} error={null} onConfirm={noop} onClose={noop} />,
    );
    expect(html).toBe('');
  });

  it('titles itself after the issue key, and quotes the server sentence', () => {
    const html = renderToStaticMarkup(
      <WipOverrideDialog
        asking={{ key: 'HA-12', refusal: REFUSAL }}
        busy={false}
        error={null}
        onConfirm={noop}
        onClose={noop}
      />,
    );
    expect(html).toContain('Move HA-12 in anyway?');
    expect(html).toContain('The WIP section is full - 5 of 5 stories and bugs are in it.');
  });

  it('offers Leave it and a red Move anyway', () => {
    const html = renderToStaticMarkup(
      <WipOverrideDialog
        asking={{ key: 'HA-12', refusal: REFUSAL }}
        busy={false}
        error={null}
        onConfirm={noop}
        onClose={noop}
      />,
    );
    expect(html).toContain('Leave it');
    expect(html).toContain('Move anyway');

    const leaveIt = /<button[^>]*>Leave it<\/button>/.exec(html)![0];
    const moveAnyway = /<button[^>]*>Move anyway<\/button>/.exec(html)![0];
    expect(leaveIt).not.toContain('hatch-btn--danger');
    expect(moveAnyway).toContain('hatch-btn--danger');
  });

  it('shows the error paragraph when the retry was itself refused', () => {
    const html = renderToStaticMarkup(
      <WipOverrideDialog
        asking={{ key: 'HA-12', refusal: REFUSAL }}
        busy={false}
        error="the WIP section is full - 6 of 5 stories and bugs are in it"
        onConfirm={noop}
        onClose={noop}
      />,
    );
    expect(html).toContain('class="text-danger"');
    expect(html).toContain('the WIP section is full - 6 of 5 stories and bugs are in it');
  });

  it('shows Move anyway as busy and disabled while the retry is in flight', () => {
    const html = renderToStaticMarkup(
      <WipOverrideDialog
        asking={{ key: 'HA-12', refusal: REFUSAL }}
        busy={true}
        error={null}
        onConfirm={noop}
        onClose={noop}
      />,
    );
    const moveAnyway = /<button[^>]*>Move anyway<\/button>/.exec(html)![0];
    expect(moveAnyway).toContain('aria-busy="true"');
    expect(moveAnyway).toContain('disabled=""');
  });
});
