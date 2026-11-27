import { useState } from 'react';
import { Button, Field, Modal } from '@hatch/ui';
import { draftOk, removeSentence, writeAddCapability, writeEditCapability, writeRemoveCapability } from '../lib/capabilityEdit';
import type { Capability } from '../types';

/**
 * Adds a capability, last. Drawn in place of the `hatch capability add`
 * command once a person may write here (`homeCardApplies`), and under the
 * table once there is one to be last after.
 */
export function CapabilityAddForm({ issueKey, onAdded }: { issueKey: string; onAdded: () => Promise<void> }) {
  const [statement, setStatement] = useState('');
  const [howToTell, setHowToTell] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    try {
      const refusal = await writeAddCapability(issueKey, statement, howToTell);
      if (refusal !== null) {
        setError(refusal);
        return;
      }
      setStatement('');
      setHowToTell('');
      setError(null);
      await onAdded();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="hatch-inline-form hatch-capability-add" role="group" aria-label="Add a capability">
      <Field label="Statement">
        <input
          value={statement}
          disabled={busy}
          onChange={(e) => setStatement(e.target.value)}
          aria-label="New capability's statement"
        />
      </Field>
      <Field label="How to tell">
        <input
          value={howToTell}
          disabled={busy}
          onChange={(e) => setHowToTell(e.target.value)}
          aria-label="New capability's how-to-tell line"
        />
      </Field>
      <Button variant="primary" loading={busy} disabled={!draftOk(statement, howToTell)} onClick={() => void save()}>
        Add a capability
      </Button>
      {error && <p className="text-danger">{error}</p>}
    </div>
  );
}

/**
 * A capability's statement and how-to-tell line, open for editing in place of
 * the row's own display. Escape restores the row without writing; Save
 * writes both lines at once - there is no partial save, since the server's
 * own refusal (a blank line, a line too long) is about the pair together.
 */
export function CapabilityEditForm({
  issueKey,
  capability,
  onSaved,
  onCancel,
}: {
  issueKey: string;
  capability: Capability;
  onSaved: () => Promise<void>;
  onCancel: () => void;
}) {
  const [statement, setStatement] = useState(capability.statement);
  const [howToTell, setHowToTell] = useState(capability.howToTell);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    try {
      const refusal = await writeEditCapability(issueKey, capability.id, statement, howToTell);
      if (refusal !== null) {
        setError(refusal);
        return;
      }
      await onSaved();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      className="hatch-inline-form hatch-capability-edit"
      role="group"
      aria-label={`Edit capability #${capability.position}`}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.stopPropagation();
          onCancel();
        }
      }}
    >
      <Field label="Statement">
        <input
          value={statement}
          disabled={busy}
          onChange={(e) => setStatement(e.target.value)}
          aria-label={`Statement: ${capability.statement}`}
        />
      </Field>
      <Field label="How to tell">
        <input
          value={howToTell}
          disabled={busy}
          onChange={(e) => setHowToTell(e.target.value)}
          aria-label={`How to tell: ${capability.statement}`}
        />
      </Field>
      <div className="hatch-form-actions">
        <Button variant="primary" loading={busy} disabled={!draftOk(statement, howToTell)} onClick={() => void save()}>
          Save
        </Button>
        <Button disabled={busy} onClick={onCancel}>
          Cancel
        </Button>
      </div>
      {error && <p className="text-danger">{error}</p>}
    </div>
  );
}

/**
 * The speed bump in front of removing a capability - names the epics that
 * serve it, since removing it unlinks them. Rendered unconditionally, like
 * ClearClaimDialog, so the modal's own focus and escape handling is the one
 * that runs.
 */
export function CapabilityRemoveDialog({
  issueKey,
  capability,
  onRemoved,
  onClose,
}: {
  issueKey: string;
  /** The capability being removed, or null when the dialog is shut. */
  capability: Capability | null;
  onRemoved: () => Promise<void>;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!capability) return <Modal open={false} onClose={onClose} title="" />;

  const removing = capability;

  async function remove() {
    setBusy(true);
    try {
      const refusal = await writeRemoveCapability(issueKey, removing.id);
      if (refusal !== null) {
        setError(refusal);
        return;
      }
      onClose();
      await onRemoved();
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} title={`Remove capability #${capability.position}?`}>
      <div className="hatch-capability-remove">
        <p>{removeSentence(capability)}</p>

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button disabled={busy} onClick={onClose}>
            Leave it
          </Button>
          <Button variant="danger" loading={busy} onClick={() => void remove()}>
            Remove it
          </Button>
        </div>
      </div>
    </Modal>
  );
}
