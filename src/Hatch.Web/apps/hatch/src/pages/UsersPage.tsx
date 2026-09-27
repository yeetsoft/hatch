import { Fragment, type FormEvent, useCallback, useEffect, useState } from 'react';
import { Badge, Button, Card, EmptyState, Field, Modal, PageHeader, Table, Text } from '@hatch/ui';
import { createPerson, deletePerson, getAuthMe, getPeople, getPersonSessions, putPerson, revokeGrant } from '../api/client';
import { agoPhrase } from '../lib/claim';
import { message } from '../lib/errors';
import {
  canAddUser,
  canChangeRole,
  canDelete,
  deleteSentence,
  demotionSentence,
  isDemotion,
  ROLES,
  sortPeople,
} from '../lib/people';
import type { Person, PersonRole, PersonSession } from '../types';

/** A change waiting on a press: a demotion, or a delete. */
type Pending =
  | { kind: 'demote'; person: Person; to: PersonRole }
  | { kind: 'delete'; person: Person };

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
  const [open, setOpen] = useState<string | null>(null);
  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState<PersonRole>('user');

  const load = useCallback(async () => {
    try {
      setPeople(sortPeople(await getPeople()));
    } catch (err) {
      setFailure(message(err));
    }
  }, []);

  useEffect(() => {
    void load();
    getAuthMe()
      .then((me) => setMyGrantId(me?.grantId ?? null))
      .catch(() => {});
  }, [load]);

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

  async function addUser(e: FormEvent) {
    e.preventDefault();
    if (!canAddUser(newEmail)) return;
    setBusy(true);
    try {
      await createPerson({ email: newEmail.trim(), role: newRole, name: newName.trim() || undefined });
      setFailure(null);
      setNewEmail('');
      setNewName('');
    } catch (err) {
      setFailure(message(err));
    } finally {
      setBusy(false);
      await load();
    }
  }

  const now = new Date();

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
                <th>Last sign-in</th>
                <th>Sessions</th>
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
                    <td>{person.lastSignInAt ? agoPhrase(person.lastSignInAt, now) : <Text tone="muted">has not signed in yet</Text>}</td>
                    <td>
                      <Button onClick={() => setOpen(open === person.id ? null : person.id)}>
                        {person.sessionCount} {open === person.id ? '▾' : '▸'}
                      </Button>
                    </td>
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
                  {open === person.id && (
                    <tr>
                      <td colSpan={7}>
                        <Sessions personId={person.id} myGrantId={myGrantId} now={now} onChanged={load} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      <Modal
        open={pending !== null}
        onClose={() => setPending(null)}
        title={pending?.kind === 'delete' ? `Delete ${pending.person.name}?` : `Change ${pending?.person.name ?? ''}'s role?`}
      >
        {pending && (
          <div className="hatch-claim-clear">
            <p>
              {pending.kind === 'delete'
                ? deleteSentence(pending.person)
                : demotionSentence(pending.person.name, pending.person.role, pending.to)}
            </p>
            <div className="hatch-form-actions">
              <Button onClick={() => setPending(null)}>Leave it</Button>
              <Button
                variant="danger"
                loading={busy}
                onClick={() =>
                  void act(() =>
                    pending.kind === 'delete'
                      ? deletePerson(pending.person.id)
                      : putPerson(pending.person.id, { name: pending.person.name, role: pending.to }),
                  )
                }
              >
                {pending.kind === 'delete' ? 'Delete' : 'Demote'}
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
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
