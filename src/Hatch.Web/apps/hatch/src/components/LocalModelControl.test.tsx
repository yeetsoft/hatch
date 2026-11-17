import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { LocalModelBand, LocalModelControl } from './LocalModelControl';
import type { LocalModel } from './LocalModelControl';
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
  over: { localModel?: LocalModel; localModelOwn?: LocalModel; localModelFrom?: string | null; busy?: boolean; kind?: 'person' | 'key'; size?: 'md' | 'lg' } = {},
) =>
  renderToStaticMarkup(
    <LocalModelControl
      issueKey="HA-14"
      localModel={over.localModel ?? 'none'}
      localModelOwn={over.localModelOwn ?? over.localModel ?? 'none'}
      localModelFrom={over.localModelFrom ?? null}
      directory={directory(over.kind)}
      busy={over.busy}
      size={over.size}
      onChange={() => {}}
    />,
  );

const band = (current: LocalModel, from: string | null = null, inheritWord = 'Prefer') =>
  renderToStaticMarkup(
    <LocalModelBand current={current} localModelFrom={from} inheritWord={inheritWord} landing={undefined} onPick={() => {}} />,
  );

describe('LocalModelControl', () => {
  it('draws the closed pill wearing the own prefer level', () => {
    const html = pill({ localModel: 'prefer' });

    expect(html).toContain('Prefer');
    expect(html).toContain('aria-label="Local model: Prefer. Set HA-14&#x27;s local model"');
    expect(html).toContain('title="Local model: Prefer. Set HA-14&#x27;s local model"');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('hatch-localmodel-band');
  });

  it('draws the pill for require and for none', () => {
    expect(pill({ localModel: 'require' })).toContain('aria-label="Local model: Require. Set HA-14&#x27;s local model"');
    expect(pill()).toContain('aria-label="Local model: None. Set HA-14&#x27;s local model"');
  });

  it('names the ancestor on a level this issue inherits', () => {
    const html = pill({ localModel: 'prefer', localModelOwn: 'none', localModelFrom: 'HA-12' });

    expect(html).toContain('aria-label="Local model: Prefer, inherited from HA-12. Set HA-14&#x27;s local model"');
  });

  it('is busy and disabled while a press is out', () => {
    const html = pill({ busy: true });

    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('disabled=""');
  });

  it('draws the word and the ancestor key for a key, with no button', () => {
    const html = pill({ localModel: 'require', localModelOwn: 'none', localModelFrom: 'HA-12', kind: 'key' });

    expect(html).toContain('Require');
    expect(html).toContain('inherited from HA-12');
    expect(html).not.toContain('<button');
  });

  it('draws the word alone for a key holding its own level', () => {
    const html = pill({ kind: 'key' });

    expect(html).toContain('None');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('<button');
  });

  it("draws the status bar's size only when asked for it", () => {
    expect(pill({ localModel: 'prefer', size: 'lg' })).toContain('hatch-localmodel-pill-lg');
    expect(pill({ localModel: 'prefer', size: 'md' })).not.toContain('hatch-localmodel-pill-lg');
    expect(pill({ localModel: 'prefer' })).not.toContain('hatch-localmodel-pill-lg');
  });
});

describe('LocalModelBand', () => {
  it('draws three steps, with the own level marked and disabled', () => {
    const html = band('prefer');

    expect(html.match(/<button/g)).toHaveLength(3);
    expect(html.match(/disabled=""/g)).toHaveLength(1);
    expect(html).toContain('hatch-localmodel-step prefer here');
    expect(html).toContain('aria-pressed="true"');
    expect(html.match(/aria-pressed="false"/g)).toHaveLength(2);
  });

  it('leaves every step open when the own level is none and nothing is inherited', () => {
    const html = band('none');

    expect(html).toContain('hatch-localmodel-step here');
    expect(html).toContain('>None<');
    expect(html).toContain('Prefer');
    expect(html).toContain('Require');
  });

  it('reads the none step as inheriting when the level comes from an ancestor', () => {
    const html = band('none', 'HA-12', 'Prefer');

    expect(html).toContain('None (inherits Prefer from HA-12)');
  });
});
