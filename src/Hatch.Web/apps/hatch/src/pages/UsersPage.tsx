import { Fragment, type FormEvent, useCallback, useEffect, useState } from 'react';
import { Badge, Button, Card, EmptyState, Field, Modal, PageHeader, ProjectMark, Table, Text, markVars } from '@hatch/ui';
import {
  createPerson,
  deleteChatLink,
  deletePerson,
  getAuthMe,
  getChatLinks,
  getPeople,
  getPersonSessions,
  getProjects,
  getScopes,
  putPerson,
  putScope,
  revokeGrant,
} from '../api/client';
import { agoPhrase } from '../lib/claim';
import { message } from '../lib/errors';
import {
  addUserWithScope,
  canAddUser,
  canChangeRole,
  canDelete,
  deleteSentence,
  demotionSentence,
  isDemotion,
  isNarrowing,
  linksOf,
  narrowingSentence,
  ROLES,
  SCOPE_MODES,
  scopeForMode,
  scopeLabel,
  sortPeople,
  unlinkSentence,
} from '../lib/people';
import type { ChatLinkRow, Person, PersonRole, PersonScope, PersonScopeMode, PersonScopeWriteRequest, PersonSession, Project } from '../types';

/** A change waiting on a press: a demotion, a delete, an admin narrowing
    their own scope, or the removal of somebody's Slack link. */
export type Pending =
  | { kind: 'demote'; person: Person; to: PersonRole }
  | { kind: 'delete'; person: Person }
  | { kind: 'narrow'; person: Person; from: PersonScope; to: PersonScope }
  | { kind: 'unlink'; person: Person; link: ChatLinkRow };

/** The one panel a row has open under it. */
type Open = { id: string; panel: 'sessions' | 'scope' | 'slack' };

/** The scope the Add user form is holding. */
type NewScope = { mode: PersonScopeMode; projectIds: number[] };

/**
 * An Admin's view of the people this install knows: who they are, what role
 * they hold, and which browsers are signed in as them.
 *
 * The rules that keep the install from locking itself out live on the server
 * (the last Admin is refused with a 409); this page mirrors them so the control
 * is disabled rather than pressed and refused, and still shows the server's
 * sentence for whatever gets through - a second Admin demoting themselves at
 * the same moment, say.
 */
export function UsersPage() {
  const [people, setPeople] = useState<Person[] | null>(null);
  const [myGrantId, setMyGrantId] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<Open | null>(null);
  const [scopes, setScopes] = useState<Map<string, PersonScope>>(new Map());
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [chatLinks, setChatLinks] = useState<ChatLinkRow[]>([]);
  const [mePersonId, setMePersonId] = useState<string | null>(null);
  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState<PersonRole>('user');
  const [newScope, setNewScope] = useState<NewScope>({ mode: 'all', projectIds: [] });

  const load = useCallback(async () => {
    try {
      // A failed read of the links is no reason to lose the page: it reads as a
      // Hatch with no Slack, and the column is not drawn.
      const [list, scopeList, linkList] = await Promise.all([getPeople(), getScopes(), getChatLinks().catch(() => [])]);
      setPeople(sortPeople(list));
      setScopes(new Map(scopeList.map((scope) => [scope.personId, scope])));
      setChatLinks(linkList);
    } catch (err) {
      setFailure(message(err));
    }
  }, []);

  useEffect(() => {
    void load();
    getAuthMe()
      .then((me) => {
        setMyGrantId(me?.grantId ?? null);
        setMePersonId(me?.personId ?? null);
      })
      .catch(() => {});
    getProjects()
      .then(setProjects)
      .catch((err) => setFailure(message(err)));
  }, [load]);

  const allIds = (projects ?? []).map((project) => project.id);

  /** What the table knows of a person's scope. A person it has no row for reads
      `all`, as the server does. */
  const scopeOf = (person: Person): PersonScope =>
    scopes.get(person.id) ?? { personId: person.id, mode: 'all', projectIds: allIds };

  /** Runs a write; whatever happens, asks again so the table shows what is
      true rather than what was pressed. */
  async function act(action: () => Promise<unknown>) {
    setBusy(true);
    try {
      await action();
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setBusy(false);
      setPending(null);
      await load();
    }
  }

  const write = (person: Person, role: PersonRole) =>
    act(() => putPerson(person.id, { name: person.name, role }));

  function chooseRole(person: Person, to: PersonRole) {
    if (to === person.role) return;
    if (isDemotion(person.role, to)) setPending({ kind: 'demote', person, to });
    else void write(person, to);
  }

  /** Saves a scope at once, unless it is an admin narrowing their own, which
      asks first. What the person would hold afterwards is `to`. */
  function writeScope(person: Person, request: { mode: PersonScopeMode; projectIds?: number[] }) {
    const from = scopeOf(person);
    const to: PersonScope = {
      personId: person.id,
      mode: request.mode,
      projectIds: request.mode === 'all' ? allIds : request.mode === 'none' ? [] : (request.projectIds ?? []),
    };
    if (person.id === mePersonId && isNarrowing(from, to)) {
      setPending({ kind: 'narrow', person, from, to });
      return;
    }
    void act(() => putScope(person.id, request));
  }

  function chooseScope(person: Person, mode: PersonScopeMode) {
    const current = scopeOf(person);
    if (mode === current.mode) return;
    writeScope(person, scopeForMode(current, mode));
    if (mode === 'some') setOpen({ id: person.id, panel: 'scope' });
    else if (open?.id === person.id && open.panel === 'scope') setOpen(null);
  }

  function chooseNewScope(mode: PersonScopeMode) {
    setNewScope({ mode, projectIds: mode === 'some' ? allIds : [] });
  }

  async function addUser(e: FormEvent) {
    e.preventDefault();
    if (!canAddUser(newEmail)) return;
    setBusy(true);
    try {
      await addUserWithScope(
        // The form clears once the person exists, so a scope that then fails
        // does not leave the typing behind to be submitted twice.
        async (request) => {
          const person = await createPerson(request);
          setNewEmail('');
          setNewName('');
          setNewScope({ mode: 'all', projectIds: [] });
          return person;
        },
        putScope,
        { email: newEmail.trim(), role: newRole, name: newName.trim() || undefined },
        newScope.mode === 'some' ? { mode: 'some', projectIds: newScope.projectIds } : { mode: newScope.mode },
      );
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setBusy(false);
      await load();
    }
  }

  const now = new Date();
  // A Hatch with no Slack link anywhere shows nothing new.
  const showSlack = chatLinks.length > 0;
  const columns = showSlack ? 9 : 8;

  return (
    <div className="hatch-page">
      <PageHeader
        title="Users"
        description="Who this install knows, what each may reach, and the browsers signed in as them. The last Admin cannot be demoted or deleted."
      />

      <Card>
        <form onSubmit={(e) => void addUser(e)}>
          <Field label="Email" hint="Their first Google sign-in with this address lands with the role below instead of waiting for approval.">
            <input type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} required />
          </Field>
          <Field label="Role">
            <select value={newRole} onChange={(e) => setNewRole(e.target.value as PersonRole)}>
              {ROLES.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Scope" hint="Which projects they see. Saved once they are added.">
            <select value={newScope.mode} onChange={(e) => chooseNewScope(e.target.value as PersonScopeMode)}>
              {SCOPE_MODES.map((mode) => (
                <option key={mode} value={mode}>
                  {mode === newScope.mode ? scopeLabel(newScope) : mode}
                </option>
              ))}
            </select>
          </Field>
          {newScope.mode === 'some' && (
            <ScopeChecklist
              projects={projects}
              selected={newScope.projectIds}
              disabled={busy}
              onChange={(projectIds) => setNewScope({ mode: 'some', projectIds })}
            />
          )}
          <Field label="Name (optional)">
            <input value={newName} onChange={(e) => setNewName(e.target.value)} />
          </Field>
          <div className="hatch-form-actions">
            <Button type="submit" loading={busy} disabled={busy || !canAddUser(newEmail)}>
              Add user
            </Button>
          </div>
        </form>
      </Card>

      {failure && <p className="text-danger">{failure}</p>}

      {people?.length === 0 && <EmptyState message="Nobody yet." />}

      {people && people.length > 0 && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Provider</th>
                <th>Role</th>
                <th>Scope</th>
                <th>Last sign-in</th>
                <th>Sessions</th>
                {showSlack && <th>Slack</th>}
                <th />
              </tr>
            </thead>
            <tbody>
              {people.map((person) => (
                <Fragment key={person.id}>
                  <tr>
                    <td>
                      <strong>{person.name}</strong>
                    </td>
                    <td>{person.email ?? <Text tone="muted">none</Text>}</td>
                    <td>{person.provider ?? <Text tone="muted">none</Text>}</td>
                    <td>
                      <select
                        aria-label={`Role of ${person.name}`}
                        value={person.role}
                        disabled={busy || !canChangeRole(person, people)}
                        title={canChangeRole(person, people) ? undefined : 'The last Admin cannot be demoted.'}
                        onChange={(e) => chooseRole(person, e.target.value as PersonRole)}
                      >
                        {ROLES.map((role) => (
                          <option key={role} value={role}>
                            {role}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <ScopeSelect
                        name={person.name}
                        scope={scopeOf(person)}
                        disabled={busy}
                        onChoose={(mode) => chooseScope(person, mode)}
                      />
                      {scopeOf(person).mode === 'some' && (
                        <Button
                          onClick={() =>
                            setOpen(open?.id === person.id && open.panel === 'scope' ? null : { id: person.id, panel: 'scope' })
                          }
                        >
                          Projects {open?.id === person.id && open.panel === 'scope' ? '▾' : '▸'}
                        </Button>
                      )}
                    </td>
                    <td>{person.lastSignInAt ? agoPhrase(person.lastSignInAt, now) : <Text tone="muted">has not signed in yet</Text>}</td>
                    <td>
                      <Button
                        onClick={() =>
                          setOpen(open?.id === person.id && open.panel === 'sessions' ? null : { id: person.id, panel: 'sessions' })
                        }
                      >
                        {person.sessionCount} {open?.id === person.id && open.panel === 'sessions' ? '▾' : '▸'}
                      </Button>
                    </td>
                    {showSlack && (
                      <td>
                        <Button
                          onClick={() =>
                            setOpen(open?.id === person.id && open.panel === 'slack' ? null : { id: person.id, panel: 'slack' })
                          }
                        >
                          {linksOf(chatLinks, person.id).length}{' '}
                          {open?.id === person.id && open.panel === 'slack' ? '▾' : '▸'}
                        </Button>
                      </td>
                    )}
                    <td>
                      <Button
                        variant="danger"
                        disabled={busy || !canDelete(person, people)}
                        onClick={() => setPending({ kind: 'delete', person })}
                      >
                        Delete
                      </Button>
                    </td>
                  </tr>
                  {open?.id === person.id && (open.panel !== 'slack' || showSlack) && (
                    <tr>
                      <td colSpan={columns}>
                        {open.panel === 'sessions' ? (
                          <Sessions personId={person.id} myGrantId={myGrantId} now={now} onChanged={load} />
                        ) : open.panel === 'slack' ? (
                          <SlackLinks
                            links={linksOf(chatLinks, person.id)}
                            disabled={busy}
                            onRemove={(link) => setPending({ kind: 'unlink', person, link })}
                          />
                        ) : (
                          <ScopeChecklist
                            projects={projects}
                            selected={scopeOf(person).projectIds}
                            disabled={busy}
                            onChange={(projectIds) => writeScope(person, { mode: 'some', projectIds })}
                          />
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      <PendingDialog
        pending={pending}
        busy={busy}
        onClose={() => setPending(null)}
        onConfirm={(p) =>
          void act(() =>
            p.kind === 'delete'
              ? deletePerson(p.person.id)
              : p.kind === 'unlink'
                ? deleteChatLink(p.link.id)
                : p.kind === 'narrow'
                  ? putScope(p.person.id, scopeRequest(p.to))
                  : putPerson(p.person.id, { name: p.person.name, role: p.to }),
          )
        }
      />
    </div>
  );
}

/** The confirmation a risky change waits on. */
export function PendingDialog({
  pending,
  busy,
  onClose,
  onConfirm,
}: {
  pending: Pending | null;
  busy: boolean;
  onClose: () => void;
  onConfirm: (pending: Pending) => void;
}) {
  const name = pending?.person.name ?? '';
  const title =
    pending?.kind === 'delete'
      ? `Delete ${name}?`
      : pending?.kind === 'unlink'
        ? `Remove ${name}'s Slack link?`
        : pending?.kind === 'narrow'
          ? `Change ${name}'s scope?`
          : `Change ${name}'s role?`;

  return (
    <Modal open={pending !== null} onClose={onClose} title={title}>
      {pending && (
        <div className="hatch-claim-clear">
          <p>
            {pending.kind === 'delete'
              ? deleteSentence(pending.person)
              : pending.kind === 'unlink'
                ? unlinkSentence(pending.person, pending.link)
                : pending.kind === 'narrow'
                  ? narrowingSentence(pending.person.name, pending.from, pending.to)
                  : demotionSentence(pending.person.name, pending.person.role, pending.to)}
          </p>
          <div className="hatch-form-actions">
            <Button onClick={onClose}>Leave it</Button>
            <Button variant="danger" loading={busy} onClick={() => onConfirm(pending)}>
              {pending.kind === 'delete'
                ? 'Delete'
                : pending.kind === 'unlink'
                  ? 'Remove'
                  : pending.kind === 'narrow'
                    ? 'Narrow'
                    : 'Demote'}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}

/** What a scope is saved as: `some` carries its ids, the others carry none. */
function scopeRequest(scope: Pick<PersonScope, 'mode' | 'projectIds'>): PersonScopeWriteRequest {
  return scope.mode === 'some' ? { mode: 'some', projectIds: scope.projectIds } : { mode: scope.mode };
}

/** A person's scope as the inline select a row draws, beside the Role one.
    The option for the current mode reads `some (N)` under `some`. */
export function ScopeSelect({
  name,
  scope,
  disabled,
  onChoose,
}: {
  name: string;
  scope: Pick<PersonScope, 'mode' | 'projectIds'>;
  disabled: boolean;
  onChoose: (mode: PersonScopeMode) => void;
}) {
  return (
    <select
      aria-label={`Scope of ${name}`}
      value={scope.mode}
      disabled={disabled}
      onChange={(e) => onChoose(e.target.value as PersonScopeMode)}
    >
      {SCOPE_MODES.map((mode) => (
        <option key={mode} value={mode}>
          {mode === scope.mode ? scopeLabel(scope) : mode}
        </option>
      ))}
    </select>
  );
}

/** Every project, ticked where the person sees it. Ticking calls `onChange`
    with the new set, in the board's project order. */
export function ScopeChecklist({
  projects,
  selected,
  disabled,
  onChange,
}: {
  projects: Project[] | null;
  selected: number[];
  disabled: boolean;
  onChange: (projectIds: number[]) => void;
}) {
  if (!projects) return <Text tone="muted">Reading projects…</Text>;
  if (projects.length === 0) return <Text tone="muted">No projects yet.</Text>;

  const ticked = new Set(selected);
  const toggle = (id: number) =>
    onChange(projects.filter((p) => (p.id === id ? !ticked.has(id) : ticked.has(p.id))).map((p) => p.id));

  return (
    <div className="hatch-project-chooser__tiles">
      {projects.map((project) => (
        <label
          key={project.id}
          className={disabled ? 'hatch-project-chooser__tile hatch-project-chooser__tile--disabled' : 'hatch-project-chooser__tile'}
          style={markVars(project.color)}
        >
          <input type="checkbox" checked={ticked.has(project.id)} disabled={disabled} onChange={() => toggle(project.id)} />
          <ProjectMark size="sm" letters={project.key} color={project.color} icon={project.icon} title={project.name} />
          <span>
            {project.key} {project.name}
          </span>
        </label>
      ))}
    </div>
  );
}

/** One person's Slack links, as the page read them: the workspace, the Slack
    member, and whether the link stands or still waits. An admin removes one;
    there is no button to make or confirm one for somebody else. */
export function SlackLinks({
  links,
  disabled,
  onRemove,
}: {
  links: ChatLinkRow[];
  disabled: boolean;
  onRemove: (link: ChatLinkRow) => void;
}) {
  if (links.length === 0) return <Text tone="muted">No Slack links.</Text>;

  return (
    <Table>
      <thead>
        <tr>
          <th>Workspace</th>
          <th>Slack member</th>
          <th>State</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {links.map((link) => (
          <tr key={link.id}>
            <td>{link.workspaceName}</td>
            <td>{link.memberName}</td>
            <td>
              <Badge tone={link.state === 'linked' ? 'success' : 'muted'}>{link.state}</Badge>
            </td>
            <td>
              <Button variant="danger" disabled={disabled} onClick={() => onRemove(link)}>
                Remove
              </Button>
            </td>
          </tr>
        ))}
      </tbody>
    </Table>
  );
}

/** One person's sessions, read when the row opens. */
function Sessions({
  personId,
  myGrantId,
  now,
  onChanged,
}: {
  personId: string;
  myGrantId: string | null;
  now: Date;
  onChanged: () => Promise<void>;
}) {
  const [sessions, setSessions] = useState<PersonSession[] | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setSessions(await getPersonSessions(personId));
    } catch (err) {
      setFailure(message(err));
    }
  }, [personId]);

  useEffect(() => {
    void load();
  }, [load]);

  async function revoke(id: string) {
    try {
      await revokeGrant(id);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    }
    await load();
    await onChanged();
  }

  if (!sessions) return <Text tone="muted">{failure ?? 'Reading sessions…'}</Text>;

  return (
    <div>
      {failure && <p className="text-danger">{failure}</p>}
      {sessions.length === 0 && <Text tone="muted">No sessions.</Text>}
      {sessions.length > 0 && (
        <Table>
          <thead>
            <tr>
              <th>Session</th>
              <th>Created</th>
              <th>Last seen</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {sessions.map((s) => (
              <tr key={s.id}>
                <td>{s.label}</td>
                <td>{agoPhrase(s.createdAt, now)}</td>
                <td>{agoPhrase(s.lastSeenAt, now)}</td>
                <td>
                  {s.id === myGrantId ? (
                    <Badge tone="primary">this browser</Badge>
                  ) : (
                    <Button variant="danger" onClick={() => void revoke(s.id)}>
                      Revoke
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </div>
  );
}
