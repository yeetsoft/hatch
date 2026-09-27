import type { Person, PersonRole } from '../types';

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
