import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { HumanWorkBand, HumanWorkControl } from './HumanWorkControl';
import type { HumanWork } from './HumanWorkControl';
import type { AssigneeDirectory } from '../types';

/* The interactive half - a press folding the band and calling onChange once,
   Escape folding it, focus returning to the pill - needs a DOM this suite does
   not have, so it is seen in the driver. What is drawn is checked here: the
   closed pill, and the band the pill opens onto, rendered directly. */

const directory = (kind: 'person' | 'key' = 'person'): AssigneeDirectory => ({
  me: { kind, id: 'ada', name: 'Ada' },
  assignees: [],
});

const pill = (
  over: { humanWork?: HumanWork; humanWorkOwn?: HumanWork; humanWorkFrom?: string | null; busy?: boolean; kind?: 'person' | 'key'; size?: 'md' | 'lg' } = {},
) =>
  renderToStaticMarkup(
    <HumanWorkControl
      issueKey="HA-14"
      humanWork={over.humanWork ?? 'agent'}
      humanWorkOwn={over.humanWorkOwn ?? 'inherit'}
      humanWorkFrom={over.humanWorkFrom ?? null}
      directory={directory(over.kind)}
      busy={over.busy}
      size={over.size}
      onChange={() => {}}
    />,
  );

const band = (current: HumanWork) =>
  renderToStaticMarkup(<HumanWorkBand current={current} landing={undefined} onPick={() => {}} />);

describe('HumanWorkControl', () => {
  it('draws the closed pill wearing the effective human value', () => {
    const html = pill({ humanWork: 'human' });

    expect(html).toContain('Human');
    expect(html).toContain('aria-label="Human work: Human. Set HA-14&#x27;s human-work override"');
    expect(html).toContain('title="Human work: Human. Set HA-14&#x27;s human-work override"');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('hatch-humanwork-band');
  });

  it('draws the pill for agent', () => {
    expect(pill({ humanWork: 'agent' })).toContain('aria-label="Human work: Agent. Set HA-14&#x27;s human-work override"');
  });

  it('names the ancestor on a value this issue inherits', () => {
    const html = pill({ humanWork: 'human', humanWorkOwn: 'inherit', humanWorkFrom: 'HA-12' });

    expect(html).toContain('aria-label="Human work: Human, inherited from HA-12. Set HA-14&#x27;s human-work override"');
  });

  it('is busy and disabled while a press is out', () => {
    const html = pill({ busy: true });

    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('disabled=""');
  });

  it('draws the word and the ancestor key for a key, with no button', () => {
    const html = pill({ humanWork: 'human', humanWorkOwn: 'inherit', humanWorkFrom: 'HA-12', kind: 'key' });

    expect(html).toContain('Human');
    expect(html).toContain('inherited from HA-12');
    expect(html).not.toContain('<button');
  });

  it('draws the word alone for a key holding its own value', () => {
    const html = pill({ kind: 'key' });

    expect(html).toContain('Agent');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('<button');
  });

  it("draws the status bar's size only when asked for it", () => {
    expect(pill({ humanWork: 'human', size: 'lg' })).toContain('hatch-humanwork-pill-lg');
    expect(pill({ humanWork: 'human', size: 'md' })).not.toContain('hatch-humanwork-pill-lg');
    expect(pill({ humanWork: 'human' })).not.toContain('hatch-humanwork-pill-lg');
  });
});

describe('HumanWorkBand', () => {
  it('draws three steps, with the own value marked and disabled', () => {
    const html = band('inherit');

    expect(html.match(/<button/g)).toHaveLength(3);
    expect(html.match(/disabled=""/g)).toHaveLength(1);
    expect(html).toContain('hatch-humanwork-step here');
    expect(html).toContain('aria-pressed="true"');
    expect(html.match(/aria-pressed="false"/g)).toHaveLength(2);
  });

  it('leaves every step open when the own value is human', () => {
    const html = band('human');

    expect(html).toContain('hatch-humanwork-step human here');
    expect(html).toContain('Inherit');
    expect(html).toContain('>Human<');
    expect(html).toContain('Agent');
  });
});
