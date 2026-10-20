import { describe, expect, it } from 'vitest';
import { credentialClear, credentialIsSet, credentialPlaceholder, credentialSave, nameSave } from './hatchSettings';
import type { CredentialField } from './hatchSettings';
import type { HatchSettings } from '../types';

const settings = (over: Partial<HatchSettings> = {}): HatchSettings => ({
  claudeSubscriptionToken: '',
  codexApiKey: '',
  xaiApiKey: '',
  localPersonNameApplies: true,
  localPersonName: '',
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
