import { message } from './errors';
import { MAX_NAME_LENGTH } from './runners';
import type { CreateRunnerRequest, Me, RunnerMinted } from '../types';

/**
 * What the Runner page should say about a key, from who is looking.
 *
 * `Me.kind` is `person` only when the wall is up. Somebody who has not been
 * answered yet is `none` - the page is silent by design until it knows, so
 * nothing flashes a claim that turns out wrong. Only an Admin is sent to the
 * keys page; anyone else is told to ask, since the page would refuse them.
 */
export type KeyAdvice = 'needs-key-admin' | 'needs-key-user' | 'none';

export function keyAdvice(me: Me | null): KeyAdvice {
  if (me?.kind !== 'person') return 'none';
  return me.role === 'admin' ? 'needs-key-admin' : 'needs-key-user';
}

/** A newly-minted or rotated secret, shown once - "The secret panel" in
    docs/runners-page-design.md. Held only in `RunnersLayout`'s `useState`,
    never storage, URL or router state, so it is gone the moment the page
    is left. */
export interface RunnerSecret {
  name: string;
  secret: string;
  rotated: boolean;
}

/**
 * Why Create would be refused before it is sent, or null - mirrors
 * `mintProblem` in `lib/apiKeys.ts`. The server says the same things and
 * stays the authority; this only keeps a button from being pressed to be
 * told what the form already knows.
 */
export function createProblem(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length === 0) return 'A runner needs a name - it is what the audit trail will call it.';
  if (trimmed.length > MAX_NAME_LENGTH) return `A runner name is at most ${MAX_NAME_LENGTH} characters.`;
  return null;
}

export interface CreateRunnerClient {
  createRunner: (request: CreateRunnerRequest) => Promise<RunnerMinted>;
  setRunnerOwner: (name: string, personId: string | null) => Promise<unknown>;
}

/**
 * Create's own press. `CreateRunner` has no way to say "nobody" - an omitted
 * `ownerPersonId` defaults to the caller - so "nobody" creates with no owner
 * at all, then clears it with a second call. If that second call fails the
 * secret still shows; the Identity owner select shows the true owner.
 */
export async function createRunnerKey(
  client: CreateRunnerClient,
  request: { name: string; ownerPersonId: string },
): Promise<{ secret: RunnerSecret } | { refusal: string }> {
  const name = request.name.trim();
  try {
    const minted = await client.createRunner(
      request.ownerPersonId ? { name, ownerPersonId: request.ownerPersonId } : { name },
    );
    if (!request.ownerPersonId) {
      try {
        await client.setRunnerOwner(minted.runner.name, null);
      } catch {
        // The secret still shows regardless - see the note above.
      }
    }
    return { secret: { name: minted.runner.name, secret: minted.secret, rotated: false } };
  } catch (err) {
    return { refusal: message(err) };
  }
}

export interface RotateRunnerClient {
  rotateRunner: (name: string) => Promise<RunnerMinted>;
}

export async function rotateRunnerKey(
  client: RotateRunnerClient,
  name: string,
): Promise<{ secret: RunnerSecret } | { refusal: string }> {
  try {
    const minted = await client.rotateRunner(name);
    return { secret: { name: minted.runner.name, secret: minted.secret, rotated: true } };
  } catch (err) {
    return { refusal: message(err) };
  }
}

export interface RevokeRunnerClient {
  revokeRunner: (name: string) => Promise<unknown>;
}

/** Revoke's press: the refusal, or null where it went through. The reload
    runs either way, as `saveRunner` does. */
export async function revokeRunnerKey(
  client: RevokeRunnerClient,
  name: string,
  reload: () => Promise<void>,
): Promise<string | null> {
  try {
    await client.revokeRunner(name);
    return null;
  } catch (err) {
    return message(err);
  } finally {
    await reload();
  }
}

export interface ChangeOwnerClient {
  setRunnerOwner: (name: string, personId: string | null) => Promise<unknown>;
}

/** The Identity card's owner select: the refusal, or null where it went
    through. The reload runs either way, as `saveRunner` does. */
export async function changeOwner(
  client: ChangeOwnerClient,
  name: string,
  personId: string | null,
  reload: () => Promise<void>,
): Promise<string | null> {
  try {
    await client.setRunnerOwner(name, personId);
    return null;
  } catch (err) {
    return message(err);
  } finally {
    await reload();
  }
}
