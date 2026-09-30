import { useState } from 'react';
import { Button, Card, EmptyState, Field, PageHeader, Table, Text } from '@hatch/ui';
import {
  createPlaybook,
  deletePlaybook,
  getPlaybooks,
  getStatuses,
  patchPlaybook,
} from '../api/client';
import { Choice } from '../components/Choice';
import { MarkdownEditor } from '../components/MarkdownEditor';
import { boardColumns } from '../lib/columns';
import { message } from '../lib/errors';
import { isReviewPlaybook, transitionLabel } from '../lib/playbooks';
import { normalizeEol } from '../lib/text';
import { useLoaded } from '../lib/useLoaded';
import {
  ISSUE_TYPES,
  PLAYBOOK_EFFORTS,
  PLAYBOOK_EFFORT_DEFAULT,
  PLAYBOOK_MODELS,
  PLAYBOOK_MODEL_DEFAULT,
  PLAYBOOK_SHAPES,
  PLAYBOOK_SHAPE_DEFAULT,
  type IssueType,
  type Playbook,
  type PlaybookCreateRequest,
  type PlaybookShape,
  type Status,
} from '../types';

/**
 * The matrix an agent is dispatched by, and the only place it can be changed.
 *
 * Everything on this page is refused to an API key on purpose: a playbook picks
 * the next agent's instructions, its model and its budget, so an agent able to
 * edit one could widen its own. The page is the operator's side of that
 * refusal, and it is why the settings are worth having as rows at all - the
 * right effort for a transition is not something anybody guesses correctly the
 * first time, it is something they retune after watching a run go badly.
 */
export function PlaybooksPage() {
  const { data: playbooks, error, setError, reload } = useLoaded<Playbook[]>(getPlaybooks);
  const { data: statuses } = useLoaded<Status[]>(getStatuses);

  async function act(action: () => Promise<unknown>) {
    try {
      await action();
      await reload();
      setError(null);
    } catch (err) {
      setError(message(err));
    }
  }

  return (
    <div className="hatch-page">
      <PageHeader
        title="Playbooks"
        description="What an agent is told, and how much thought to spend, when it moves an issue one column along - and, for the review column to itself, when a pull request there has stopped merging with the trunk."
      />

      {error && <p className="text-danger">{error}</p>}

      {statuses && <NewPlaybook statuses={statuses} onCreate={(r) => void act(() => createPlaybook(r))} />}

      {playbooks?.length === 0 && (
        <EmptyState message="No playbooks yet. Without one, a transition dispatches nothing at all - hatch.sh work says so rather than guessing." />
      )}

      {playbooks && statuses && playbooks.length > 0 && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Transition</th>
                <th>Types</th>
                <th>Shape</th>
                <th>Model</th>
                <th>Effort</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {playbooks.map((playbook) => (
                <Row
                  key={playbook.id}
                  playbook={playbook}
                  onPatch={(request) => void act(() => patchPlaybook(playbook.id, request))}
                  onDelete={() => void act(() => deletePlaybook(playbook.id))}
                />
              ))}
            </tbody>
          </Table>
        </Card>
      )}
    </div>
  );
}

/**
 * One row, with its prompt folded away underneath.
 *
 * Folded because the prompt is the long part and the dispatch is the part being
 * scanned: an operator comparing what four transitions cost wants four lines,
 * not four essays. Opening one is how they edit it.
 */
function Row({
  playbook,
  onPatch,
  onDelete,
}: {
  playbook: Playbook;
  onPatch: (request: {
    types?: IssueType[];
    shape?: PlaybookShape;
    prompt?: string;
    model?: string;
    effort?: string;
  }) => void;
  onDelete: () => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <tr>
        <td>
          {isReviewPlaybook(playbook) ? (
            <>
              <strong>{playbook.fromStatusName}</strong> <Text tone="muted">review playbook</Text>
            </>
          ) : (
            <>
              <strong>{playbook.fromStatusName}</strong> → <strong>{playbook.toStatusName}</strong>
            </>
          )}
        </td>
        <td>
          <TypesCell types={playbook.types} onChange={(types) => onPatch({ types })} />
        </td>
        <td>
          <Choice
            label={`${transitionLabel(playbook)} shape`}
            value={playbook.shape}
            options={PLAYBOOK_SHAPES}
            onChange={(shape) => onPatch({ shape: shape as PlaybookShape })}
          />
        </td>
        <td>
          <Choice
            label={`${transitionLabel(playbook)} model`}
            value={playbook.model}
            options={PLAYBOOK_MODELS}
            onChange={(model) => onPatch({ model })}
          />
        </td>
        <td>
          <Choice
            label={`${transitionLabel(playbook)} effort`}
            value={playbook.effort}
            options={PLAYBOOK_EFFORTS}
            onChange={(effort) => onPatch({ effort })}
          />
        </td>
        <td>
          <div className="hatch-reorder">
            <Button onClick={() => setOpen(!open)}>{open ? 'Hide prompt' : 'Prompt'}</Button>
            <Button variant="danger" onClick={onDelete}>
              Delete
            </Button>
          </div>
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={6}>
            <PromptCell prompt={playbook.prompt} onSave={(prompt) => onPatch({ prompt })} />
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * The prompt, edited as the plain text it is.
 *
 * Saved on a button rather than on blur, unlike the colour and the name on the
 * Statuses page. Those are one gesture and cheap to undo; this is prose
 * somebody is part way through writing, and a PATCH fired because they clicked
 * out of the box to read the ticket they are writing it about is a PATCH that
 * saves half a sentence.
 */
function PromptCell({ prompt, onSave }: { prompt: string; onSave: (prompt: string) => void }) {
  const [draft, setDraft] = useState(prompt);
  // Line endings alone are not an edit: the editor's text is always `\n`.
  const dirty = normalizeEol(draft) !== normalizeEol(prompt);

  return (
    <div className="hatch-playbook-prompt">
      <Field
        label="Prompt"
        as="div"
        hint="What the agent is told before it is shown the ticket. The ticket is the brief; this is the method."
      >
        <MarkdownEditor value={draft} onChange={setDraft} rows={16} className="hatch-grows" ariaLabel="Prompt" />
      </Field>
      <div className="hatch-form-actions">
        <Button variant="primary" disabled={!dirty || draft.trim() === ''} onClick={() => onSave(draft)}>
          Save prompt
        </Button>
        <Button disabled={!dirty} onClick={() => setDraft(prompt)}>
          Revert
        </Button>
      </div>
    </div>
  );
}

/** Which types a row speaks for. Nothing ticked is every type - see NormalizeTypes. */
function TypesCell({ types, onChange }: { types: IssueType[]; onChange: (types: IssueType[]) => void }) {
  return (
    <div className="hatch-playbook-types">
      {ISSUE_TYPES.map((type) => (
        <label key={type}>
          <input
            type="checkbox"
            checked={types.includes(type)}
            onChange={(e) => onChange(e.target.checked ? [...types, type] : types.filter((t) => t !== type))}
          />
          {type}
        </label>
      ))}
      {types.length === 0 && <Text tone="muted">any</Text>}
    </div>
  );
}

/**
 * A new row. The transition is chosen from the board's own columns, so a
 * playbook can only ever name a column that exists - and an install that
 * renamed "todo" gets its own names here without this page knowing any.
 *
 * The board's columns, and only those: nothing is dispatched out of a deferred
 * column or into one, so a playbook naming one is a row that can never match.
 * Offering it would be offering an hour of wondering why the loop is ignoring
 * a rule somebody wrote.
 */
function NewPlaybook({
  statuses: every,
  onCreate,
}: {
  statuses: Status[];
  onCreate: (request: PlaybookCreateRequest) => void;
}) {
  const statuses = boardColumns(every);
  const [from, setFrom] = useState(statuses[0]?.id ?? 0);
  const [to, setTo] = useState(statuses[1]?.id ?? 0);
  const [types, setTypes] = useState<IssueType[]>([]);
  const [prompt, setPrompt] = useState('');
  // Chosen here rather than left to the server's default and corrected in the
  // table afterwards: the model and the effort are what a transition costs,
  // and a row created at the default is a row that quietly runs at the default
  // until somebody notices.
  const [model, setModel] = useState<string>(PLAYBOOK_MODEL_DEFAULT);
  const [effort, setEffort] = useState<string>(PLAYBOOK_EFFORT_DEFAULT);
  const [shape, setShape] = useState<PlaybookShape>(PLAYBOOK_SHAPE_DEFAULT);

  return (
    <Card>
      <h2 className="hatch-section-title">New playbook</h2>
      <div className="hatch-field-grid">
        <Field label="From" hint="The column the issue is in when the agent picks it up.">
          <select value={from} onChange={(e) => setFrom(Number(e.target.value))}>
            {statuses.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </Field>
        <Field
          label="To"
          hint="Where it should be when the agent stops. The review column to itself is the review playbook: what an agent is told when a pull request there has stopped merging or its build fails."
        >
          <select value={to} onChange={(e) => setTo(Number(e.target.value))}>
            {statuses.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Types" hint="Leave all unticked for every type." as="div">
          <TypesCell types={types} onChange={setTypes} />
        </Field>
        <Field label="Shape" hint="Whether the issue's children matter: any, only a leaf, or only a parent.">
          <Choice value={shape} options={PLAYBOOK_SHAPES} onChange={(s) => setShape(s as PlaybookShape)} />
        </Field>
        <Field label="Model" hint="Which model the dispatched session runs on.">
          <Choice value={model} options={PLAYBOOK_MODELS} onChange={setModel} />
        </Field>
        <Field label="Effort" hint="How much thought it may spend on the move.">
          <Choice value={effort} options={PLAYBOOK_EFFORTS} onChange={setEffort} />
        </Field>
      </div>
      <Field label="Prompt" as="div" hint="What the agent is told before it is shown the ticket.">
        <MarkdownEditor
          value={prompt}
          onChange={setPrompt}
          rows={6}
          className="hatch-grows"
          deferred
          ariaLabel="Prompt"
        />
      </Field>
      <div className="hatch-form-actions">
        <Button
          variant="primary"
          /* Not disabled when both ends match: the server says which column may
             name itself, and its sentence is better than a greyed-out button
             that says nothing about why. */
          disabled={!prompt.trim()}
          onClick={() => {
            onCreate({ fromStatusId: from, toStatusId: to, types, prompt, model, effort, shape });
            setPrompt('');
            setTypes([]);
          }}
        >
          Add playbook
        </Button>
      </div>
    </Card>
  );
}
