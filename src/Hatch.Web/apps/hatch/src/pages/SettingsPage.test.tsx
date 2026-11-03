import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TokenBudgetCard } from './SettingsPage';
import { defaultTokensOf } from '../lib/hatchSettings';
import type { DefaultTokenField } from '../lib/hatchSettings';
import type { HatchSettings } from '../types';

/* No DOM runner, so neither useMe nor useLoaded resolves - the card is
   rendered from props, the way CumulativeFlowPage.test.tsx renders
   CumulativeFlowBody. */

const settingsOf = (over: Partial<HatchSettings> = {}): HatchSettings => ({
  claudeSubscriptionToken: '',
  codexApiKey: '',
  xaiApiKey: '',
  localPersonNameApplies: false,
  localPersonName: '',
  defaultMaxTokens: null,
  defaultMaxTokensPerHour: null,
  ...over,
});

const render = (over: {
  settings?: HatchSettings;
  editable?: boolean;
  draft?: Record<DefaultTokenField, string>;
  busy?: 'defaultTokens' | null;
} = {}): string => {
  const settings = over.settings ?? settingsOf();
  return renderToStaticMarkup(
    <TokenBudgetCard
      settings={settings}
      editable={over.editable ?? true}
      draft={over.draft ?? defaultTokensOf(settings)}
      busy={over.busy ?? null}
      onChange={() => {}}
      onSave={() => {}}
    />,
  );
};

describe('TokenBudgetCard', () => {
  it('shows both inputs empty with Save disabled when nothing has changed', () => {
    const html = render();

    expect(html).toContain('<input');
    expect(html).toContain('value=""');
    expect(html).toContain('disabled=""');
  });

  it('enables Save once a box holds something the settings do not', () => {
    const html = render({ draft: { defaultMaxTokens: '500,000', defaultMaxTokensPerHour: '' } });

    expect(html).toContain('value="500,000"');
    expect(html).not.toContain('disabled=""');
  });

  it('disables Save again once a save reseeds the draft to match the settings', () => {
    const settings = settingsOf({ defaultMaxTokens: 500000 });
    const html = render({ settings, draft: defaultTokensOf(settings) });

    expect(html).toContain('value="500,000"');
    expect(html).toContain('disabled=""');
  });

  it('enables Save when a box is cleared against a saved cap', () => {
    const settings = settingsOf({ defaultMaxTokens: 500000 });
    const html = render({ settings, draft: { defaultMaxTokens: '', defaultMaxTokensPerHour: '' } });

    expect(html).not.toContain('disabled=""');
  });

  it('shows the problem and disables Save when a box is unparseable', () => {
    const html = render({ draft: { defaultMaxTokens: 'abc', defaultMaxTokensPerHour: '' } });

    expect(html).toContain('takes a token count');
    expect(html).toContain('disabled=""');
  });

  it('reads as text with no inputs for a non-admin', () => {
    const settings = settingsOf({ defaultMaxTokens: 500000 });
    const html = render({ settings, editable: false });

    expect(html).toContain('No cap');
    expect(html).toContain('500k tokens');
    expect(html).toContain('Admins only.');
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<button');
  });

  it('keeps Save disabled while a save is in flight even with a changed draft', () => {
    const html = render({
      draft: { defaultMaxTokens: '500,000', defaultMaxTokensPerHour: '' },
      busy: 'defaultTokens',
    });

    expect(html).toContain('disabled=""');
  });
});
