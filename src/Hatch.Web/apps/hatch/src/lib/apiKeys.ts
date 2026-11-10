/* What the API keys page decides for itself, apart from the page that draws it.

   The one rule worth a file is the secret's: it is shown once, held in memory
   and nowhere else, and gone the moment it is dismissed or replaced. Modelling
   that as a value that is either a panel or null - rather than a string the
   page remembers to clear - is what makes "the list never carries it" a thing
   a test can pin. */

import { MAX_NAME_LENGTH } from './runners';
import type { ApiKey, ApiKeyMinted, Runner, RunnerMinted } from '../types';

export { MAX_NAME_LENGTH, lastUsedLabel } from './runners';

/** A runner row as the keys page shows it, or null for a heartbeat no key
    claims - this page only lists keys, so that row is dropped rather than
    drawn. */
export function runnerAsKey(r: Runner): ApiKey | null {
  if (r.apiKeyId === null || r.apiKeyId === undefined) return null;
  return {
    id: r.apiKeyId,
    name: r.name,
    prefix: r.prefix ?? '',
    createdAt: r.createdAt ?? r.firstSeenAt,
    lastUsedAt: r.lastUsedAt ?? null,
    revokedAt: r.revokedAt ?? null,
    owner: r.ownerPersonId && r.for ? { id: r.ownerPersonId, name: r.for } : null,
  };
}

/** Every runner row that is a key, in the order the server sent them. */
export function runnersAsKeys(rows: Runner[]): ApiKey[] {
  return rows.flatMap((r) => runnerAsKey(r) ?? []);
}

/** A freshly minted runner, as the keys page's secret panel wants it. */
export function mintedAsKey(m: RunnerMinted): ApiKeyMinted {
  const key = runnerAsKey(m.runner);
  if (key === null) throw new Error('a freshly minted runner has no key');
  return { key, secret: m.secret };
}

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
 * being pressed to be told what the form already knows.
 */
export function mintProblem(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return 'A key needs a name - it is what the audit trail will call it.';
  if (trimmed.length > MAX_NAME_LENGTH) return `A key name is at most ${MAX_NAME_LENGTH} characters.`;
  return null;
}

/** The secret on its way to a screen once, or nothing. */
export type SecretPanel = { key: ApiKey; secret: string } | null;

/** A second mint replaces the first: the earlier secret is unrecoverable, which is why the panel says so. */
export const showSecret = (minted: ApiKeyMinted): SecretPanel => ({ key: minted.key, secret: minted.secret });

export const dismissSecret = (): SecretPanel => null;

export const ownerLabel = (owner: ApiKey['owner']): string => owner?.name ?? 'nobody';
