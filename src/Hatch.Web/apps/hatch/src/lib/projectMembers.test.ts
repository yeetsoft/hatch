import { describe, expect, it } from 'vitest';
import {
  hasOwner,
  openProjectMembersDraft,
  projectMembersDiff,
  removeObjection,
  reroled,
  roleObjection,
  withMember,
  withoutMember,
} from './projectMembers';
import type { Project, ProjectMember } from '../types';

const owner: ProjectMember = { personId: 'a', name: 'Ada', role: 'owner' };
const approver: ProjectMember = { personId: 'b', name: 'Bea', role: 'approver' };
const otherOwner: ProjectMember = { personId: 'c', name: 'Cid', role: 'owner' };

const project = (members: ProjectMember[]): Project =>
  ({ members } as Project);

describe('openProjectMembersDraft', () => {
  it('starts both known and members at the project members', () => {
    const members = [owner, approver];
    const draft = openProjectMembersDraft(project(members));
    expect(draft.known).toEqual(members);
    expect(draft.members).toEqual(members);
  });
});

describe('projectMembersDiff', () => {
  it('diffs an unmodified draft to nothing', () => {
    const draft = openProjectMembersDraft(project([owner, approver]));
    expect(projectMembersDiff(draft)).toEqual({ puts: [], deletes: [] });
  });

  it('a re-role appears only in puts', () => {
    const known = [owner, approver];
    const draft = { known, members: [owner, { ...approver, role: 'owner' }] };
    expect(projectMembersDiff(draft)).toEqual({
      puts: [{ personId: 'b', role: 'owner' }],
      deletes: [],
    });
  });

  it('an added member appears in puts and not deletes', () => {
    const known = [owner];
    const draft = { known, members: [owner, approver] };
    expect(projectMembersDiff(draft)).toEqual({
      puts: [{ personId: 'b', role: 'approver' }],
      deletes: [],
    });
  });

  it('a removed member appears only in deletes', () => {
    const known = [owner, approver];
    const draft = { known, members: [owner] };
    expect(projectMembersDiff(draft)).toEqual({ puts: [], deletes: ['b'] });
  });

  it('a member left alone appears in neither', () => {
    const known = [owner, approver];
    const draft = { known, members: [owner, approver, otherOwner] };
    const diff = projectMembersDiff(draft);
    expect(diff.deletes).toEqual([]);
    expect(diff.puts).toEqual([{ personId: 'c', role: 'owner' }]);
  });
});

describe('removeObjection', () => {
  it('is silent removing either owner from a two-owner list', () => {
    const members = [owner, otherOwner, approver];
    expect(removeObjection(members, 'a')).toBeNull();
    expect(removeObjection(members, 'c')).toBeNull();
  });

  it('objects to removing the sole owner', () => {
    const members = [owner, approver];
    expect(removeObjection(members, 'a')).not.toBeNull();
  });

  it('is silent removing an approver', () => {
    const members = [owner, approver];
    expect(removeObjection(members, 'b')).toBeNull();
  });
});

describe('roleObjection', () => {
  it('is silent demoting either owner on a two-owner list', () => {
    const members = [owner, otherOwner, approver];
    expect(roleObjection(members, 'a', 'approver')).toBeNull();
    expect(roleObjection(members, 'c', 'approver')).toBeNull();
  });

  it('objects to demoting the sole owner', () => {
    const members = [owner, approver];
    expect(roleObjection(members, 'a', 'approver')).not.toBeNull();
  });

  it('is silent demoting a non-owner', () => {
    const members = [owner, approver];
    expect(roleObjection(members, 'b', 'approver')).toBeNull();
  });
});

describe('withoutMember', () => {
  it('removes either owner from a two-owner list', () => {
    const members = [owner, otherOwner, approver];
    expect(withoutMember(members, 'a')).toEqual([otherOwner, approver]);
  });

  it('leaves a one-owner list unchanged when removing the owner', () => {
    const members = [owner, approver];
    expect(withoutMember(members, 'a')).toEqual(members);
  });

  it('removes an approver', () => {
    const members = [owner, approver];
    expect(withoutMember(members, 'b')).toEqual([owner]);
  });
});

describe('reroled', () => {
  it('demotes either owner on a two-owner list', () => {
    const members = [owner, otherOwner, approver];
    expect(reroled(members, 'a', 'approver')).toEqual([{ ...owner, role: 'approver' }, otherOwner, approver]);
  });

  it('leaves a one-owner list unchanged when demoting the owner', () => {
    const members = [owner, approver];
    expect(reroled(members, 'a', 'approver')).toEqual(members);
  });

  it('demotes a non-owner', () => {
    const members = [owner, approver];
    expect(reroled(members, 'b', 'owner')).toEqual([owner, { ...approver, role: 'owner' }]);
  });
});

describe('withMember', () => {
  it('appends a new person', () => {
    expect(withMember([owner], { id: 'd', name: 'Di' }, 'approver')).toEqual([
      owner,
      { personId: 'd', name: 'Di', role: 'approver' },
    ]);
  });

  it('re-roles in place when the person is already present', () => {
    expect(withMember([owner, approver], { id: 'b', name: 'Bea' }, 'owner')).toEqual([
      owner,
      { ...approver, role: 'owner' },
    ]);
  });
});

describe('hasOwner', () => {
  it('is true with an owner present', () => {
    expect(hasOwner([owner, approver])).toBe(true);
  });

  it('is false with none', () => {
    expect(hasOwner([approver])).toBe(false);
  });
});
