import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { CredentialCard, HomeScreenCard, TokenBudgetCard } from './SettingsPage';
import { defaultTokensOf } from '../lib/hatchSettings';
import type { DefaultTokenField } from '../lib/hatchSettings';
import type { Home } from '../lib/homeScreen';
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
  canSaveSecrets: true,
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

describe('CredentialCard', () => {
  const credential = {
    field: 'codexApiKey' as const,
    title: 'Codex API key',
    noun: 'API key',
    label: 'API key',
    hint: 'A hint.',
    steps: <li>A step.</li>,
  };

  const renderCard = (over: { settings?: HatchSettings; draft?: string; busy?: 'codexApiKey' | null } = {}): string =>
    renderToStaticMarkup(
      <MemoryRouter>
        <CredentialCard
          credential={credential}
          settings={over.settings ?? settingsOf()}
          draft={over.draft ?? ''}
          busy={over.busy ?? null}
          onDraft={() => {}}
          onSave={() => {}}
          onClear={() => {}}
        />
      </MemoryRouter>,
    );

  /** The opening tag of the button whose text is `label`. */
  const buttonOf = (html: string, label: string): string => {
    const at = html.indexOf(`${label}</button>`);
    return html.slice(html.lastIndexOf('<button', at), at);
  };

  it('with a key set, says nothing about one and enables Save once something is typed', () => {
    const html = renderCard({ draft: 'sk-new' });

    expect(html).not.toContain('HATCH_SECRET_KEY');
    expect(html).not.toContain('/docs/environment-variables');
    expect(buttonOf(html, 'Save API key')).not.toContain('disabled=""');
  });

  it('with no key, says so beside the field and links the environment variables page', () => {
    const html = renderCard({ settings: settingsOf({ canSaveSecrets: false }) });

    expect(html).toContain('Secrets cannot be saved until HATCH_SECRET_KEY is set.');
    expect(html).toContain('href="/docs/environment-variables"');
  });

  it('with no key, keeps Save off even with something typed', () => {
    const html = renderCard({ settings: settingsOf({ canSaveSecrets: false }), draft: 'sk-new' });

    expect(buttonOf(html, 'Save API key')).toContain('disabled=""');
  });

  it('with no key, leaves Remove on for a credential that is set', () => {
    const html = renderCard({ settings: settingsOf({ canSaveSecrets: false, codexApiKey: '••••••••' }) });

    expect(buttonOf(html, 'Remove API key')).not.toContain('disabled=""');
  });
});

describe('HomeScreenCard', () => {
  const renderHome = (over: { checked?: Home; busy?: boolean; error?: string | null } = {}): string =>
    renderToStaticMarkup(
      <HomeScreenCard
        checked={over.checked ?? 'board'}
        busy={over.busy ?? false}
        error={over.error ?? null}
        onChoose={() => {}}
      />,
    );

  /** The label whose radio is checked, by splitting on the <label> wrapper
      each choice renders inside. */
  const checkedLabel = (html: string): string | undefined =>
    html.split('<label>').slice(1).find((l) => l.includes('checked=""'));

  it('checks Ambitions alone when that is saved', () => {
    const html = renderHome({ checked: 'ambitions' });

    expect(html.match(/checked=""/g)).toHaveLength(1);
    expect(checkedLabel(html)).toContain('Ambitions');
  });

  it('checks Board alone when that is saved', () => {
    const html = renderHome({ checked: 'board' });

    expect(html.match(/checked=""/g)).toHaveLength(1);
    expect(checkedLabel(html)).toContain('Board');
  });

  it('shows the refusal sentence and leaves the saved choice checked', () => {
    const html = renderHome({ checked: 'board', error: 'Nobody has signed in.' });

    expect(html).toContain('Nobody has signed in.');
    expect(checkedLabel(html)).toContain('Board');
  });

  it('disables both radios while a save is in flight', () => {
    const html = renderHome({ busy: true });

    expect(html.match(/disabled=""/g)).toHaveLength(2);
  });

  it('names the root address, every device and the default in the hint', () => {
    const html = renderHome();

    expect(html).toContain('root address');
    expect(html).toContain('every device');
    expect(html).toContain('Board is the default');
  });
});
