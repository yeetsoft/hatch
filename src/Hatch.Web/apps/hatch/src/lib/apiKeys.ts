/* What the API keys page decides for itself, apart from the page that draws it.

   The one rule worth a file is the secret's: it is shown once, held in memory
   and nowhere else, and gone the moment it is dismissed or replaced. Modelling
   that as a value that is either a panel or null - rather than a string the
   page remembers to clear - is what makes "the list never carries it" a thing
   a test can pin. */

import type { ApiKey, ApiKeyMinted } from '../types';

/** Mirrors EfApiKey.MaxNameLength. */
export const MAX_NAME_LENGTH = 120;

/** Live keys and revoked ones, each in the order the server sent them. */
export function partitionKeys(keys: ApiKey[]): { live: ApiKey[]; revoked: ApiKey[] } {
  return {
    live: keys.filter((k) => k.revokedAt === null),
    revoked: keys.filter((k) => k.revokedAt !== null),
  };
}

/**
 * Why this mint would be refused before it is sent, or null. The server says
 * the same things and stays the authority - this only keeps a button from
 * being pressed to be told what the form already knows. Scopes are not checked: the
 * server allows none, and a key with none simply reaches nothing.
 */
export function mintProblem(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return 'A key needs a name - it is what the audit trail will call it.';
  if (trimmed.length > MAX_NAME_LENGTH) return `A key name is at most ${MAX_NAME_LENGTH} characters.`;
  return null;
}

/** Adds the scope if it is not selected, removes it if it is. */
export function toggleScope(selected: string[], scope: string): string[] {
  return selected.includes(scope) ? selected.filter((s) => s !== scope) : [...selected, scope];
}

/** The secret on its way to a screen once, or nothing. */
export type SecretPanel = { key: ApiKey; secret: string } | null;

/** A second mint replaces the first: the earlier secret is unrecoverable, which is why the panel says so. */
export const showSecret = (minted: ApiKeyMinted): SecretPanel => ({ key: minted.key, secret: minted.secret });

export const dismissSecret = (): SecretPanel => null;

export const scopesLabel = (scopes: string[]): string => (scopes.length === 0 ? 'none' : scopes.join(', '));

export const ownerLabel = (owner: ApiKey['owner']): string => owner?.name ?? 'nobody';

export const lastUsedLabel = (iso: string | null): string =>
  iso === null ? 'never' : new Date(iso).toLocaleString();
