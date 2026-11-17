import type {
  Person,
  PersonCreateRequest,
  PersonRole,
  PersonScope,
  PersonScopeMode,
  PersonScopeWriteRequest,
} from '../types';
import { message } from './errors';

/** The roles in the order a select offers them: least to most. */
export const ROLES: readonly PersonRole[] = ['pending', 'user', 'admin'];

/** The people, by name - the server's order, kept here so an edit that changes
    nothing about the order does not reshuffle the table under the cursor. Ties
    break on id so the order is total. */
export function sortPeople(people: readonly Person[]): Person[] {
  return [...people].sort(
    (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) || a.id.localeCompare(b.id),
  );
}

/** The one Admin the install would be left with none without. Null when there
    are two or more, or none. */
export function loneAdmin(people: readonly Person[]): Person | null {
  const admins = people.filter((p) => p.role === 'admin');
  return admins.length === 1 ? admins[0] : null;
}

/** Whether this row's role may be changed: never the lone Admin's, which the
    server would refuse with a 409 anyway. */
export function canChangeRole(person: Person, people: readonly Person[]): boolean {
  return loneAdmin(people)?.id !== person.id;
}

/** Whether the row may be deleted - the same rule, for the same reason. */
export const canDelete = canChangeRole;

/** Whether moving from one role to another takes something away, and so asks
    first. Ordered by ROLES: the later, the more. */
export function isDemotion(from: PersonRole, to: PersonRole): boolean {
  return ROLES.indexOf(to) < ROLES.indexOf(from);
}

/** What the confirmation says. The consequence rather than "are you sure":
    what the person can no longer reach. */
export function demotionSentence(name: string, from: PersonRole, to: PersonRole): string {
  const loses =
    to === 'pending'
      ? 'they will reach nothing behind the wall until someone lets them back in'
      : from === 'admin'
        ? 'they will keep the everyday apps but lose people, sessions and the rest of the operator’s tools'
        : 'they will lose the everyday apps';
  return `${name} goes from ${from} to ${to}: ${loses}.`;
}

/** What the delete confirmation says - including that the sessions go too. */
export function deleteSentence(person: Pick<Person, 'name' | 'sessionCount'>): string {
  const n = person.sessionCount;
  const sessions =
    n === 0 ? 'They have no sessions.' : `Their ${n === 1 ? 'session ends' : `${n} sessions end`} with them.`;
  return `${person.name} will be removed. ${sessions}`;
}

/** Whether the Add user form may be submitted: an address is all it needs. The
    server judges the address; this only keeps an empty form from posting. */
export function canAddUser(email: string): boolean {
  return email.trim().length > 0;
}

/** The scope modes in the order a select offers them: least to most. */
export const SCOPE_MODES: readonly PersonScopeMode[] = ['none', 'all', 'some'];

/** What a scope reads as in a select and a row: the mode, and under `some` how
    many projects are ticked. `some (0)` sees nothing, as `none` does. */
export function scopeLabel(scope: Pick<PersonScope, 'mode' | 'projectIds'>): string {
  return scope.mode === 'some' ? `some (${scope.projectIds.length})` : scope.mode;
}

/** Whether moving from one scope to another takes any project away, and so
    asks first when it is one's own. `all` already lists every project, so
    `all` to `some` with everything ticked takes nothing. */
export function isNarrowing(
  from: Pick<PersonScope, 'projectIds'>,
  to: Pick<PersonScope, 'projectIds'>,
): boolean {
  const kept = new Set(to.projectIds);
  return from.projectIds.some((id) => !kept.has(id));
}

/** What the confirmation says: by count, what the person stops seeing. */
export function narrowingSentence(
  name: string,
  from: Pick<PersonScope, 'mode' | 'projectIds'>,
  to: Pick<PersonScope, 'mode' | 'projectIds'>,
): string {
  const kept = new Set(to.projectIds);
  const lost = from.projectIds.filter((id) => !kept.has(id)).length;
  const projects = `${lost} ${lost === 1 ? 'project' : 'projects'}`;
  return `${name} goes from ${scopeLabel(from)} to ${scopeLabel(to)}: they will stop seeing ${projects}.`;
}

/** The request to send on choosing a mode. `some` carries over what the person
    holds now, so from `all` every project starts ticked and from `none` none
    does; `all` and `none` send no ids. */
export function scopeForMode(
  current: Pick<PersonScope, 'projectIds'>,
  mode: PersonScopeMode,
): PersonScopeWriteRequest {
  return mode === 'some' ? { mode, projectIds: [...current.projectIds] } : { mode };
}

/** Adds a person, then gives them the scope the form chose. No row already
    reads `all`, so that choice makes no second call. When the second call
    fails the person exists, on `all`, and the sentence says so. */
export async function addUserWithScope(
  create: (request: PersonCreateRequest) => Promise<Pick<Person, 'id'>>,
  setScope: (personId: string, request: PersonScopeWriteRequest) => Promise<unknown>,
  request: PersonCreateRequest,
  scope: PersonScopeWriteRequest,
): Promise<void> {
  const person = await create(request);
  if (scope.mode === 'all') return;
  try {
    await setScope(person.id, scope);
  } catch (err) {
    throw new Error(`${request.email} was added with scope all, and the scope was not saved: ${message(err)}`);
  }
}
