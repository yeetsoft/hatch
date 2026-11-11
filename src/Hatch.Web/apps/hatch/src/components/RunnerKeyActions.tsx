import { useState } from 'react';
import { Button, Modal, Text } from '@hatch/ui';
import { getPeople, revokeRunner, rotateRunner, setRunnerOwner } from '../api/client';
import { changeOwner, revokeRunnerKey, rotateRunnerKey } from '../lib/runnerKey';
import { useLoaded } from '../lib/useLoaded';
import type { RunnerSecret } from '../lib/runnerKey';
import type { Runner } from '../types';

/** Rotate's confirmation body, its own component so a test can render it
    without opening the modal around it. */
export function RotateRunnerBody({ refusal }: { refusal: string | null }) {
  return (
    <>
      <Text>
        A new secret is made now and the current one stops working at once. Every loop using it is refused on its
        next call until it is given the new secret. The name, owner, tags, bounds, sessions and history carry over.
        The new secret is shown once.
      </Text>
      {refusal && <p className="text-danger">{refusal}</p>}
    </>
  );
}

/** Revoke's confirmation body, its own component so a test can render it
    without opening the modal around it. */
export function RevokeRunnerBody({ refusal }: { refusal: string | null }) {
  return (
    <>
      <Text>
        Anything using this key is refused from the next call. The runner, its tags, history and attribution stay,
        marked revoked, so the audit trail can still name it. To replace a leaked secret and keep the runner
        working, rotate instead.
      </Text>
      {refusal && <p className="text-danger">{refusal}</p>}
    </>
  );
}

/**
 * The Identity card's owner select, Rotate and Revoke - "Who sees what":
 * only an admin, on a runner that is neither revoked nor retired, reaches
 * `RunnerDetail` with this in its `actions` slot at all.
 */
export function RunnerKeyActions({
  runner,
  onSecret,
  reload,
}: {
  runner: Runner;
  onSecret: (secret: RunnerSecret) => void;
  reload: () => Promise<void>;
}) {
  const { data: people } = useLoaded(getPeople);
  const [savingOwner, setSavingOwner] = useState(false);
  const [ownerRefusal, setOwnerRefusal] = useState<string | null>(null);

  const [rotating, setRotating] = useState(false);
  const [rotateBusy, setRotateBusy] = useState(false);
  const [rotateRefusal, setRotateRefusal] = useState<string | null>(null);

  const [revoking, setRevoking] = useState(false);
  const [revokeBusy, setRevokeBusy] = useState(false);
  const [revokeRefusal, setRevokeRefusal] = useState<string | null>(null);

  async function saveOwner(personId: string) {
    setSavingOwner(true);
    try {
      setOwnerRefusal(await changeOwner({ setRunnerOwner }, runner.name, personId || null, reload));
    } finally {
      setSavingOwner(false);
    }
  }

  async function rotate() {
    setRotateBusy(true);
    try {
      const result = await rotateRunnerKey({ rotateRunner }, runner.name);
      if ('refusal' in result) {
        setRotateRefusal(result.refusal);
      } else {
        setRotateRefusal(null);
        setRotating(false);
        onSecret(result.secret);
      }
    } finally {
      setRotateBusy(false);
    }
  }

  async function revoke() {
    setRevokeBusy(true);
    try {
      const refusal = await revokeRunnerKey({ revokeRunner }, runner.name, reload);
      setRevokeRefusal(refusal);
      if (refusal === null) setRevoking(false);
    } finally {
      setRevokeBusy(false);
    }
  }

  return (
    <>
      <div className="hatch-form-actions">
        <select
          aria-label={`Owner of ${runner.name}`}
          value={runner.ownerPersonId ?? ''}
          disabled={savingOwner}
          onChange={(e) => void saveOwner(e.target.value)}
        >
          <option value="">nobody</option>
          {(people ?? []).map((person) => (
            <option key={person.id} value={person.id}>
              {person.name}
            </option>
          ))}
        </select>
        <Button onClick={() => setRotating(true)}>Rotate key</Button>
        <Button variant="danger" onClick={() => setRevoking(true)}>
          Revoke
        </Button>
      </div>
      {ownerRefusal && <p className="text-danger">{ownerRefusal}</p>}

      <Modal
        open={rotating}
        onClose={() => setRotating(false)}
        title={`Rotate ${runner.name}'s key?`}
        footer={
          <div className="hatch-form-actions">
            <Button onClick={() => setRotating(false)}>Leave it</Button>
            <Button variant="primary" loading={rotateBusy} onClick={() => void rotate()}>
              Rotate key
            </Button>
          </div>
        }
      >
        <RotateRunnerBody refusal={rotateRefusal} />
      </Modal>

      <Modal
        open={revoking}
        onClose={() => setRevoking(false)}
        title={`Revoke ${runner.name}?`}
        footer={
          <div className="hatch-form-actions">
            <Button onClick={() => setRevoking(false)}>Leave it</Button>
            <Button variant="danger" loading={revokeBusy} onClick={() => void revoke()}>
              Revoke
            </Button>
          </div>
        }
      >
        <RevokeRunnerBody refusal={revokeRefusal} />
      </Modal>
    </>
  );
}
