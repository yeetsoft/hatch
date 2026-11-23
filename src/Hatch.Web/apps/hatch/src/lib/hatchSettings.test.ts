import { describe, expect, it } from 'vitest';
import {
  credentialClear,
  credentialIsRemovable,
  credentialIsUnreadable,
  credentialState,
  credentialIsSet,
  credentialPlaceholder,
  credentialSave,
  defaultTokensChanged,
  defaultTokensOf,
  defaultTokensProblem,
  defaultTokensSave,
  nameSave,
  SECRETS_BLOCKED_WORDS,
  secretsBlocked,
  tokenBudgetEditable,
  UNREADABLE_WORDS,
} from './hatchSettings';
import type { CredentialField, DefaultTokenField } from './hatchSettings';
import type { HatchSettings, Me, SecretState } from '../types';

/** The states follow the dots unless a test says otherwise, so a test that sets a credential need not say twice that it is set. */
const settings = (over: Partial<HatchSettings> = {}): HatchSettings => {
  const base = {
    claudeSubscriptionToken: '',
    codexApiKey: '',
    xaiApiKey: '',
    localPersonNameApplies: true,
    localPersonName: '',
    defaultMaxTokens: null,
    defaultMaxTokensPerHour: null,
    canSaveSecrets: true,
    ...over,
  };
  const stateOf = (dots: string): SecretState => (dots === '' ? 'unset' : 'set');
  return {
    ...base,
    secretStates: {
      claudeSubscriptionToken: stateOf(base.claudeSubscriptionToken),
      codexApiKey: stateOf(base.codexApiKey),
      xaiApiKey: stateOf(base.xaiApiKey),
      ...over.secretStates,
    },
  };
};

const me = (over: Partial<Me> = {}): Me => ({
  kind: 'person',
  name: 'Ada',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: true,
  ...over,
});

const FIELDS: CredentialField[] = ['claudeSubscriptionToken', 'codexApiKey', 'xaiApiKey'];

describe.each(FIELDS)('the %s field', (field) => {
  it('knows a credential is stored from the dots, without ever holding one', () => {
    expect(credentialIsSet(settings({ [field]: '••••••••' }), field)).toBe(true);
    expect(credentialIsSet(settings(), field)).toBe(false);
  });

  /* The sharp one. Putting the dots in the box would mean an operator who
     saved, came back and pressed Save stored eight bullets as their
     credential. */
  it('says a credential is set in the placeholder, so the dots never become a value', () => {
    const hint = credentialPlaceholder(settings({ [field]: '••••••••' }), field);

    expect(hint).not.toContain('•');
    expect(hint).toContain('set');
  });

  it('says so when there is none', () => {
    expect(credentialPlaceholder(settings(), field)).toBe('Not set');
  });

  it('sends nothing for a blank draft - leaving it alone is not clearing it', () => {
    expect(credentialSave(field, '')).toBeNull();
    expect(credentialSave(field, '   ')).toBeNull();
  });

  it('sends the trimmed credential, and nothing else', () => {
    expect(credentialSave(field, '  sk-1 ')).toEqual({ [field]: 'sk-1' });
  });

  /* An omitted field would leave it alone; `''` is what removes it. */
  it('clears with an empty string rather than an omitted field', () => {
    expect(credentialClear(field)).toEqual({ [field]: '' });
    expect(credentialClear(field)[field]).not.toBeUndefined();
  });

  /* The criterion that no section can send another's field - and it covers
     the name as well, so one form cannot wipe another. */
  it('names only itself, so one form cannot wipe another', () => {
    expect(Object.keys(credentialSave(field, 'x')!)).toEqual([field]);
    expect(Object.keys(credentialClear(field))).toEqual([field]);
  });
});

describe('the credentials together', () => {
  it('does not report the others set because one is', () => {
    const only = settings({ codexApiKey: '••••••••' });

    expect(credentialIsSet(only, 'codexApiKey')).toBe(true);
    expect(credentialIsSet(only, 'claudeSubscriptionToken')).toBe(false);
    expect(credentialIsSet(only, 'xaiApiKey')).toBe(false);
  });
});

describe('the name field', () => {
  it('sends the trimmed name', () => {
    expect(nameSave('  Ada  ')).toEqual({ localPersonName: 'Ada' });
  });

  /* Unlike the token, this field shows what is stored - so an empty box is
     somebody looking at nothing and asking for nothing. */
  it('treats an emptied box as a clear', () => {
    expect(nameSave('')).toEqual({ localPersonName: '' });
  });

  it('never names a credential', () => {
    for (const field of FIELDS) expect(nameSave('Ada')).not.toHaveProperty(field);
  });
});

describe('tokenBudgetEditable', () => {
  it('is permissive while Me has not answered yet', () => {
    expect(tokenBudgetEditable(null, false)).toBe(true);
  });

  it('is editable for an admin, whatever Me says the role is', () => {
    expect(tokenBudgetEditable(me({ role: 'user' }), true)).toBe(true);
  });

  it('is editable for a local Me with no role to gate on', () => {
    expect(tokenBudgetEditable(me({ role: null }), false)).toBe(true);
  });

  it('is closed to a signed-in user who is not an admin', () => {
    expect(tokenBudgetEditable(me({ role: 'user' }), false)).toBe(false);
  });
});

describe('defaultTokensOf', () => {
  it('reads a cap grouped with commas', () => {
    expect(defaultTokensOf(settings({ defaultMaxTokens: 500_000, defaultMaxTokensPerHour: 2_000_000 }))).toEqual({
      defaultMaxTokens: '500,000',
      defaultMaxTokensPerHour: '2,000,000',
    });
  });

  it('reads no cap as an empty box', () => {
    expect(defaultTokensOf(settings())).toEqual({ defaultMaxTokens: '', defaultMaxTokensPerHour: '' });
  });
});

describe('defaultTokensChanged', () => {
  const saved = settings({ defaultMaxTokens: 500_000, defaultMaxTokensPerHour: null });

  it('is false against what defaultTokensOf reads back', () => {
    expect(defaultTokensChanged(defaultTokensOf(saved), saved)).toBe(false);
  });

  it('is false for a shorthand that counts the same as the saved value', () => {
    const draft: Record<DefaultTokenField, string> = { defaultMaxTokens: '500k', defaultMaxTokensPerHour: '' };

    expect(defaultTokensChanged(draft, saved)).toBe(false);
  });

  it('is true once either box holds a different count', () => {
    const draft: Record<DefaultTokenField, string> = { defaultMaxTokens: '600k', defaultMaxTokensPerHour: '' };

    expect(defaultTokensChanged(draft, saved)).toBe(true);
  });

  it('is true once a cap is cleared', () => {
    const draft: Record<DefaultTokenField, string> = { defaultMaxTokens: '', defaultMaxTokensPerHour: '' };

    expect(defaultTokensChanged(draft, saved)).toBe(true);
  });
});

describe('defaultTokensProblem', () => {
  it('is null for two empty boxes', () => {
    expect(defaultTokensProblem({ defaultMaxTokens: '', defaultMaxTokensPerHour: '' })).toBeNull();
  });

  it('is null for two token counts', () => {
    expect(defaultTokensProblem({ defaultMaxTokens: '500k', defaultMaxTokensPerHour: '2M' })).toBeNull();
  });

  it('names the per-run box when it cannot parse', () => {
    expect(defaultTokensProblem({ defaultMaxTokens: 'none', defaultMaxTokensPerHour: '' })).toBe(
      'Default max tokens per run takes a token count, like 500k or 2M.',
    );
  });

  it('names the per-hour box when it cannot parse', () => {
    expect(defaultTokensProblem({ defaultMaxTokens: '', defaultMaxTokensPerHour: 'none' })).toBe(
      'Default max tokens per hour takes a token count, like 500k or 2M.',
    );
  });
});

describe('defaultTokensSave', () => {
  it('sends both fields as the wire form, every time', () => {
    expect(defaultTokensSave({ defaultMaxTokens: '500,000', defaultMaxTokensPerHour: '2M' })).toEqual({
      defaultMaxTokens: '500000',
      defaultMaxTokensPerHour: '2M',
    });
  });

  it('sends a cleared box as a clear, not a no-op', () => {
    expect(defaultTokensSave({ defaultMaxTokens: '', defaultMaxTokensPerHour: '' })).toEqual({
      defaultMaxTokens: '',
      defaultMaxTokensPerHour: '',
    });
  });
});

describe('secretsBlocked', () => {
  it('is false when a key is set, so the page looks as it always did', () => {
    expect(secretsBlocked(settings({ canSaveSecrets: true }))).toBe(false);
  });

  it('is true when there is no key to seal under', () => {
    expect(secretsBlocked(settings({ canSaveSecrets: false }))).toBe(true);
  });

  it('says one sentence, naming the variable', () => {
    expect(SECRETS_BLOCKED_WORDS).toContain('HATCH_SECRET_KEY');
    expect(SECRETS_BLOCKED_WORDS.match(/\./g)).toHaveLength(1);
  });

  it('never stops a credential being removed', () => {
    expect(credentialClear('codexApiKey')).toEqual({ codexApiKey: '' });
  });
});

describe.each(FIELDS)('the %s field when what is stored cannot be opened', (field) => {
  const unreadable = settings({
    secretStates: { claudeSubscriptionToken: 'unset', codexApiKey: 'unset', xaiApiKey: 'unset', [field]: 'unreadable' },
  });

  it('reads the state the server gave', () => {
    expect(credentialState(unreadable, field)).toBe('unreadable');
    expect(credentialIsUnreadable(unreadable, field)).toBe(true);
    expect(credentialIsUnreadable(settings(), field)).toBe(false);
  });

  /* Dots mean set and readable, so an unreadable one is not "set" to anything
     that asks that - only to the page, which asks the state. */
  it('is still not set by the dots, and can still be removed', () => {
    expect(credentialIsSet(unreadable, field)).toBe(false);
    expect(credentialIsRemovable(unreadable, field)).toBe(true);
  });

  it('is removable when set and not when unset', () => {
    expect(credentialIsRemovable(settings({ [field]: '••••••••' }), field)).toBe(true);
    expect(credentialIsRemovable(settings(), field)).toBe(false);
  });

  it('says so in the placeholder, without the dots', () => {
    const hint = credentialPlaceholder(unreadable, field);

    expect(hint).toContain('unreadable');
    expect(hint).not.toContain('•');
  });

  it('still sends only the new value when one is typed', () => {
    expect(credentialSave(field, ' sk-new ')).toEqual({ [field]: 'sk-new' });
  });
});

describe('UNREADABLE_WORDS', () => {
  it('is two short sentences that tell the operator what to do', () => {
    expect(UNREADABLE_WORDS).toContain('Enter it again');
    expect(UNREADABLE_WORDS.match(/\./g)).toHaveLength(2);
  });
});
