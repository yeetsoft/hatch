/* Adding, editing, reordering and removing a capability from an ambition's
   page - the same split budget.ts makes, and for the same reason: this app
   has no DOM test setup, so what can be wrong lives in a function with a
   test beside it.

   Every write answers null on success or the server's own refusal sentence,
   never throwing - the page shows that sentence beside the control and keeps
   whatever was typed. */

import { addCapability, editCapability, moveCapabilities, removeCapability } from '../api/client';
import type { Capability } from '../types';
import { message } from './errors';

/** Said once above the section on an ambition that has reached the
    implementation column, where a write here is only a session's to work out
    the consequences of. */
export const implementationNote = 'A session will work out what a change does to the epics in flight.';

/** Both lines, trimmed, are non-blank - the same rule the server holds a
    draft to, checked here first so Save need not round-trip to say so. */
export function draftOk(statement: string, howToTell: string): boolean {
  return statement.trim() !== '' && howToTell.trim() !== '';
}

/** Whether the row at this index may move up - every one but the first. */
export function canMoveUp(index: number): boolean {
  return index > 0;
}

/** Whether the row at this index may move down - every one but the last. */
export function canMoveDown(index: number, count: number): boolean {
  return index < count - 1;
}

/** The ids in the order a move would put them in - `id` swapped with its
    neighbour in the direction given - or null at the end that has no such
    neighbour. */
export function reordered(ids: number[], id: number, direction: 'up' | 'down'): number[] | null {
  const index = ids.indexOf(id);
  if (index < 0) return null;

  const target = direction === 'up' ? index - 1 : index + 1;
  if (target < 0 || target >= ids.length) return null;

  const next = [...ids];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

/** What the remove dialog says below its question - the epics that serve the
    capability, since removing it unlinks them, or that none does. */
export function removeSentence(capability: Capability): string {
  if (capability.servedBy.length === 0) return 'No epic serves this capability.';

  const names =
    capability.servedBy.length < 2
      ? capability.servedBy[0]
      : `${capability.servedBy.slice(0, -1).join(', ')} and ${capability.servedBy[capability.servedBy.length - 1]}`;
  const verb = capability.servedBy.length === 1 ? 'serves' : 'serve';

  return `${names} ${verb} this capability, and will be unlinked.`;
}

/** Adds a capability, last. Answers null on success or the refusal. */
export async function writeAddCapability(key: string, statement: string, howToTell: string): Promise<string | null> {
  try {
    await addCapability(key, { statement: statement.trim(), howToTell: howToTell.trim() });
    return null;
  } catch (err) {
    return message(err);
  }
}

/** Writes both lines of an existing capability. Answers null on success or the refusal. */
export async function writeEditCapability(
  key: string,
  id: number,
  statement: string,
  howToTell: string,
): Promise<string | null> {
  try {
    await editCapability(key, id, { statement: statement.trim(), howToTell: howToTell.trim() });
    return null;
  } catch (err) {
    return message(err);
  }
}

/** Puts the capabilities in the order given. Answers null on success or the refusal. */
export async function writeCapabilityOrder(key: string, ids: number[]): Promise<string | null> {
  try {
    await moveCapabilities(key, ids);
    return null;
  } catch (err) {
    return message(err);
  }
}

/** Removes a capability. Answers null on success or the refusal. */
export async function writeRemoveCapability(key: string, id: number): Promise<string | null> {
  try {
    await removeCapability(key, id);
    return null;
  } catch (err) {
    return message(err);
  }
}
