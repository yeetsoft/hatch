/* The list arithmetic behind the Projects page's members cell.

   Operates on ProjectMember[], the shape every read and every write already
   carries - unlike lib/repositories.ts there is no separate write shape here,
   since a member edit is one person at a time rather than a whole-list PUT.
   No DOM, same reasoning as lib/projectKey.ts and lib/repositories.ts. */

import type { Project, ProjectMember } from '../types';

export interface ProjectMembersDraft {
  known: ProjectMember[];
  members: ProjectMember[];
}

export interface ProjectMembersDiff {
  puts: { personId: string; role: string }[];
  deletes: string[];
}

/** Both halves start as the project's current members - `known` is what the
    server last said, `members` is what gets edited from here. */
export function openProjectMembersDraft(project: Project): ProjectMembersDraft {
  return { known: project.members, members: project.members };
}

/** The minimal set of writes that would carry `draft.members` back to the
    server: a put for every member whose role is new or has changed, a delete
    for every known member missing from the draft. A member present in both
    with the same role produces neither. */
export function projectMembersDiff(draft: ProjectMembersDraft): ProjectMembersDiff {
  const known = new Map(draft.known.map((m) => [m.personId, m.role]));
  const puts = draft.members
    .filter((m) => known.get(m.personId) !== m.role)
    .map((m) => ({ personId: m.personId, role: m.role }));
  const memberIds = new Set(draft.members.map((m) => m.personId));
  const deletes = draft.known.filter((m) => !memberIds.has(m.personId)).map((m) => m.personId);
  return { puts, deletes };
}

/** Whether any row is `'owner'`. */
export function hasOwner(members: ProjectMember[]): boolean {
  return members.some((m) => m.role === 'owner');
}

/** null, or a sentence, when `personId` is the only `'owner'` in `members` and
    removing it would leave none - the same refusal ProjectsController.DeleteMember
    gives back as a 409. */
export function removeObjection(members: ProjectMember[], personId: string): string | null {
  const member = members.find((m) => m.personId === personId);
  if (member?.role !== 'owner') return null;
  if (members.filter((m) => m.role === 'owner').length > 1) return null;
  return 'Would be left with no owner.';
}

/** null, or a sentence, when `personId` is the only `'owner'` in `members` and
    re-roling it to anything but `'owner'` would leave none - the same refusal
    ProjectsController.PutMember gives back as a 409. */
export function roleObjection(members: ProjectMember[], personId: string, role: string): string | null {
  if (role === 'owner') return null;
  const member = members.find((m) => m.personId === personId);
  if (member?.role !== 'owner') return null;
  if (members.filter((m) => m.role === 'owner').length > 1) return null;
  return 'Would be left with no owner.';
}

/** `members` with `personId` removed, or unchanged when `removeObjection` objects. */
export function withoutMember(members: ProjectMember[], personId: string): ProjectMember[] {
  if (removeObjection(members, personId) !== null) return members;
  return members.filter((m) => m.personId !== personId);
}

/** `members` with `personId` re-roled to `role`, or unchanged when `roleObjection` objects. */
export function reroled(members: ProjectMember[], personId: string, role: string): ProjectMember[] {
  if (roleObjection(members, personId, role) !== null) return members;
  return members.map((m) => (m.personId === personId ? { ...m, role } : m));
}

/** Appends `person` at `role`, or re-roles it in place when its id is already present. */
export function withMember(
  members: ProjectMember[],
  person: { id: string; name: string },
  role: string,
): ProjectMember[] {
  if (members.some((m) => m.personId === person.id)) {
    return members.map((m) => (m.personId === person.id ? { ...m, role } : m));
  }
  return [...members, { personId: person.id, name: person.name, role }];
}
