import { useEffect, useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { createRunner, getAuthMe, getPeople, setRunnerOwner, suggestRunnerName } from '../api/client';
import { createProblem, createRunnerKey } from '../lib/runnerKey';
import { useLoaded } from '../lib/useLoaded';
import type { RunnerSecret } from '../lib/runnerKey';

/**
 * "Create" in docs/runners-page-design.md: name and owner, nothing else -
 * tags are set on the detail afterwards.
 *
 * The owner is held as `string | null`: null until `getAuthMe` answers, so
 * Create stays disabled rather than risk a press that sends "nobody" before
 * the admin's own default has loaded.
 */
export function CreateRunnerDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  onCreated: (secret: RunnerSecret) => void;
}) {
  const { data: people } = useLoaded(getPeople);
  const [name, setName] = useState('');
  const [owner, setOwner] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  const suggest = () => void suggestRunnerName().then(setName).catch(() => {});

  useEffect(() => {
    if (!open) return;
    suggest();
    setRefusal(null);
    getAuthMe()
      .then((authMe) => setOwner((current) => current ?? authMe?.personId ?? ''))
      .catch(() => setOwner((current) => current ?? ''));
  }, [open]);

  const problem = createProblem(name);

  async function create() {
    if (owner === null) return;
    setCreating(true);
    try {
      const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name, ownerPersonId: owner });
      if ('refusal' in result) {
        setRefusal(result.refusal);
      } else {
        setName('');
        setOwner(null);
        onCreated(result.secret);
      }
    } finally {
      setCreating(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Create runner"
      footer={
        <div className="hatch-form-actions">
          <Button onClick={onClose}>Cancel</Button>
          <Button
            variant="primary"
            loading={creating}
            disabled={problem !== null || owner === null}
            onClick={() => void create()}
          >
            Create
          </Button>
        </div>
      }
    >
      <div className="hatch-runner-create-name">
        <Field
          label="Name"
          hint="A name from the cast list; yours to change. It is the runner's identity and cannot be renamed."
        >
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Button onClick={suggest}>Suggest another</Button>
      </div>
      <Field label="Owner">
        <select value={owner ?? ''} onChange={(e) => setOwner(e.target.value)}>
          <option value="">nobody</option>
          {(people ?? []).map((person) => (
            <option key={person.id} value={person.id}>
              {person.name}
            </option>
          ))}
        </select>
      </Field>
      {refusal && <p className="text-danger">{refusal}</p>}
    </Modal>
  );
}
