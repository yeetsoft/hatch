import type { HatchSettings, HatchSettingsWriteRequest, Me } from '../types';
import { parseTokenCount, thousands } from './workLog';
import { tokensWire } from './runners';

/**
 * The rules the Settings page's fields follow, pulled out of the component
 * so they can be read - and tested - without a browser.
 *
 * They are small on purpose. What is worth writing down is not "render an
 * input"; it is that a blank credential field means *no change* rather than
 * *clear it*, that clearing is `''` and never `null`, and that the redacted
 * dots the server hands back are a signal about the stored value rather than a
 * value to put in the box. Those rules are the same for every agent's
 * credential, so each helper takes the field it is about.
 */

/** The settings that are credentials: one per supported agent, none ever read back. */
export type CredentialField = 'claudeSubscriptionToken' | 'codexApiKey' | 'xaiApiKey';

/** Whether a credential is already stored. The server answers dots or nothing, never the credential. */
export const credentialIsSet = (settings: HatchSettings, field: CredentialField): boolean => settings[field] !== '';

/**
 * What the empty credential input says.
 *
 * The dots themselves never go into the box: an operator who saved and came
 * back would otherwise be editing a placeholder, and pressing Save would store
 * eight bullet characters as their credential.
 */
export const credentialPlaceholder = (settings: HatchSettings, field: CredentialField): string =>
  credentialIsSet(settings, field) ? 'A token is set — type a new one to replace it' : 'Not set';

/**
 * The edit a Save of a credential field sends, or null when there is nothing
 * to send. It names that field and no other.
 *
 * Blank is "leave it alone", not "clear it" - clearing has its own button,
 * because a credential should not be removable by tabbing through a form.
 */
export const credentialSave = (field: CredentialField, draft: string): HatchSettingsWriteRequest | null =>
  draft.trim() === '' ? null : { [field]: draft.trim() };

/** Removes a credential. `''` rather than an omitted field, which would leave it alone. */
export const credentialClear = (field: CredentialField): HatchSettingsWriteRequest => ({ [field]: '' });

/**
 * The edit a Save of the name field sends.
 *
 * Trimmed, and a blank draft is a clear rather than a no-op - unlike the token,
 * this field shows what is stored, so an operator who empties it is looking at
 * an empty box and asking for an empty name. Nothing here validates the name:
 * the server's read side already falls back for anything it will not use, and
 * two places deciding that would be two places to keep in step.
 */
export const nameSave = (draft: string): HatchSettingsWriteRequest => ({ localPersonName: draft.trim() });

/** The settings that set a site-wide default token budget. */
export type DefaultTokenField = 'defaultMaxTokens' | 'defaultMaxTokensPerHour';

/** Whether the token budget fields may be edited: the loading-is-permissive
    convention the Runners page already gates on, so a page that has not yet
    heard back from `/api/hatch/auth/me` does not flash the fields closed. */
export const tokenBudgetEditable = (me: Me | null, isAdmin: boolean): boolean =>
  me === null || isAdmin || me.role === null;

/** What the token budget fields look like in the form that edits them. */
export const defaultTokensOf = (settings: HatchSettings): Record<DefaultTokenField, string> => ({
  defaultMaxTokens: settings.defaultMaxTokens === null ? '' : thousands(settings.defaultMaxTokens),
  defaultMaxTokensPerHour: settings.defaultMaxTokensPerHour === null ? '' : thousands(settings.defaultMaxTokensPerHour),
});

/** Whether a token box holds something other than the saved one. Compared as
    counts where both parse, so `2M` against a saved `2,000,000` is no change. */
function tokensDiffer(draft: string, saved: string): boolean {
  const a = parseTokenCount(tokensWire(draft));
  const b = parseTokenCount(tokensWire(saved));
  return a !== null && b !== null ? a !== b : draft.trim() !== saved.trim();
}

/** Whether the form is holding anything the settings are not. */
export const defaultTokensChanged = (draft: Record<DefaultTokenField, string>, settings: HatchSettings): boolean => {
  const saved = defaultTokensOf(settings);
  return (['defaultMaxTokens', 'defaultMaxTokensPerHour'] as const).some((f) => tokensDiffer(draft[f], saved[f]));
};

/** Why the form cannot be saved yet, or null. */
export const defaultTokensProblem = (draft: Record<DefaultTokenField, string>): string | null => {
  const maxTokens = tokensWire(draft.defaultMaxTokens);
  if (maxTokens !== '' && parseTokenCount(maxTokens) === null) {
    return 'Default max tokens per run takes a token count, like 500k or 2M.';
  }
  const maxTokensPerHour = tokensWire(draft.defaultMaxTokensPerHour);
  if (maxTokensPerHour !== '' && parseTokenCount(maxTokensPerHour) === null) {
    return 'Default max tokens per hour takes a token count, like 500k or 2M.';
  }
  return null;
};

/** The edit a Save of the token budget fields sends: both, every time, so a
    cleared box is a clear and not a no-op. */
export const defaultTokensSave = (draft: Record<DefaultTokenField, string>): HatchSettingsWriteRequest => ({
  defaultMaxTokens: tokensWire(draft.defaultMaxTokens),
  defaultMaxTokensPerHour: tokensWire(draft.defaultMaxTokensPerHour),
});
