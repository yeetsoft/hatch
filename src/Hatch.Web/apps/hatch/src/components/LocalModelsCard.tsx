import { useState } from 'react';
import { Button, Card, EmptyState, Field, Table } from '@hatch/ui';
import { createLocalModel, deleteLocalModel, getLocalModels } from '../api/client';
import { message } from '../lib/errors';
import { EMPTY_LOCAL_MODEL_DRAFT, localModelRequest } from '../lib/localModels';
import type { LocalModelDraft } from '../lib/localModels';
import { gigabytesLabel } from '../lib/runners';
import { useLoaded } from '../lib/useLoaded';
import type { LocalModelRow } from '../types';

/** The table's rows in the order given - the server's creation order - each
    with its own Delete. Nothing is drawn while the read is out. */
export function LocalModelsTable({
  models,
  onDelete,
}: {
  models: LocalModelRow[] | null;
  onDelete: (model: LocalModelRow) => void;
}) {
  if (models === null) return null;
  if (models.length === 0) return <EmptyState message="No local models yet. Add one below." />;

  return (
    <Table>
      <thead>
        <tr>
          <th>Model</th>
          <th>Context</th>
          <th>Memory</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {models.map((model) => (
          <tr key={model.id}>
            <td>{model.ollamaId}</td>
            <td>{model.contextLengthTokens.toLocaleString('en-US')} tokens</td>
            <td>{gigabytesLabel(model.memoryFootprintBytes)}</td>
            <td>
              <Button variant="danger" onClick={() => onDelete(model)}>
                Delete
              </Button>
            </td>
          </tr>
        ))}
      </tbody>
    </Table>
  );
}

/** The three fields and the Add button, with the server's own sentence beside
    them when it refused the last try. */
export function LocalModelForm({
  draft,
  error,
  busy,
  onChange,
  onAdd,
}: {
  draft: LocalModelDraft;
  error: string | null;
  busy: boolean;
  onChange: (draft: LocalModelDraft) => void;
  onAdd: () => void;
}) {
  const ready = localModelRequest(draft) !== null && !busy;

  return (
    <form
      className="hatch-form"
      onSubmit={(e) => {
        e.preventDefault();
        if (ready) onAdd();
      }}
    >
      <div className="hatch-field-grid">
        <Field label="Ollama id">
          <input
            type="text"
            value={draft.ollamaId}
            placeholder="qwen3-coder:30b"
            onChange={(e) => onChange({ ...draft, ollamaId: e.target.value })}
          />
        </Field>
        <Field label="Context length (tokens)">
          <input
            type="number"
            min="1"
            step="1"
            value={draft.contextLength}
            onChange={(e) => onChange({ ...draft, contextLength: e.target.value })}
          />
        </Field>
        <Field label="Memory footprint (GB)">
          <input
            type="number"
            min="0"
            step="any"
            value={draft.footprintGigabytes}
            onChange={(e) => onChange({ ...draft, footprintGigabytes: e.target.value })}
          />
        </Field>
      </div>
      <div className="hatch-form-actions">
        <Button type="submit" variant="primary" disabled={!ready}>
          Add model
        </Button>
        {error && (
          <span className="text-danger" role="alert">
            {error}
          </span>
        )}
      </div>
    </form>
  );
}

/**
 * The board-wide table of Ollama models a runner can be asked to hold, with
 * the form that adds a row. Every row is a person's to add or remove; the
 * server refuses an API key, and says so in a sentence the form shows as it
 * stands.
 */
export function LocalModelsCard() {
  const { data: models, error: loadError, reload } = useLoaded<LocalModelRow[]>(getLocalModels);
  const [draft, setDraft] = useState<LocalModelDraft>(EMPTY_LOCAL_MODEL_DRAFT);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function add() {
    const request = localModelRequest(draft);
    if (request === null) return;

    setBusy(true);
    try {
      await createLocalModel(request);
      setDraft(EMPTY_LOCAL_MODEL_DRAFT);
      setError(null);
      await reload();
    } catch (err) {
      // The form stays as filled in, so the id can be mended rather than retyped.
      setError(message(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(model: LocalModelRow) {
    try {
      await deleteLocalModel(model.id);
      setError(null);
      await reload();
    } catch (err) {
      setError(message(err));
    }
  }

  return (
    <Card as="section" className="hatch-local-models">
      <h2 className="hatch-section-title">Local models</h2>
      {loadError && <p className="text-danger">{loadError}</p>}
      <LocalModelsTable models={models} onDelete={(model) => void remove(model)} />
      <LocalModelForm draft={draft} error={error} busy={busy} onChange={setDraft} onAdd={() => void add()} />
    </Card>
  );
}
