import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Card, Field, Text } from '@hatch/ui';
import { RunnerColumnsField } from './RunnerColumnsField';
import { RunnerTagsField } from './RunnerTagsField';
import {
  declaredDangerous,
  declaredProjects,
  isIssueKeyShape,
  settingsChanged,
  settingsOf,
  settingsRequest,
  type RunnerSettingsRequest,
} from '../lib/runners';
import { columnsWords } from '../lib/runnerColumns';
import type { Runner, RunnerSession, Status } from '../types';

/**
 * The detail's "What it may work" card: the runner's tags, its under, the
 * models it refuses and the columns it works, saved on their own button and
 * never together with the ceilings.
 *
 * Saved on a button rather than on blur, as the bounds are: an epic key
 * half-typed is not one to send. `refusal` is the server's refusal of the last
 * save, drawn inside this card. A caller who cannot edit - a viewer, or any
 * caller on a revoked or retired runner - gets the same values as text, with
 * no input and no button.
 */
export function RunnerSettingsForm({
  runner,
  live,
  statuses,
  editable,
  refusal,
  onSave,
}: {
  runner: Runner;
  live: RunnerSession[];
  /** The board's columns, or null before they are read or when the read failed: the Columns section waits for them. */
  statuses: Status[] | null;
  editable: boolean;
  refusal: string | null;
  onSave: (request: RunnerSettingsRequest) => void;
}) {
  const [draft, setDraft] = useState(settingsOf(runner));
  const dirty = settingsChanged(draft, runner);
  const projects = declaredProjects(live);
  const dangerous = declaredDangerous(live);

  const underLink = (under: string) => (
    <Link className="hatch-card-key" to={`/issues/${under}`}>
      {under}
    </Link>
  );

  return (
    <Card>
      <h2 className="hatch-section-title">What it may work</h2>

      {editable ? (
        <div className="hatch-runner-bounds">
          <div className="hatch-field-grid">
            <RunnerTagsField
              label="Tags"
              hint="One tag per line. Empty is any runner."
              value={draft.tags}
              onChange={(tags) => setDraft({ ...draft, tags })}
            />
            <Field label="Under" hint="An epic to stay inside, like AER-930. Empty is the whole board.">
              <input value={draft.under} onChange={(e) => setDraft({ ...draft, under: e.target.value })} />
            </Field>
            <RunnerTagsField
              label="Refuses models"
              hint="One model per line, like fable or claude-opus-5. Empty refuses none. Binds on the board's very next dispatch read, not on the next heartbeat the way tags do."
              value={draft.refusedModels}
              onChange={(refusedModels) => setDraft({ ...draft, refusedModels })}
            />
          </div>
          {statuses && (
            <RunnerColumnsField
              name={runner.name}
              statuses={statuses}
              mode={draft.statusMode}
              ids={draft.statusIds}
              onChange={(statusMode, statusIds) => setDraft({ ...draft, statusMode, statusIds })}
            />
          )}
          <Field
            as="div"
            label="Preferred"
            hint="Takes work before runners without this. With less work than runners, the others sit idle."
          >
            <label>
              <input
                type="checkbox"
                checked={draft.preferred}
                onChange={(e) => setDraft({ ...draft, preferred: e.target.checked })}
              />{' '}
              Preferred
            </label>
          </Field>
          {isIssueKeyShape(draft.under) && <Text tone="muted">{underLink(draft.under.trim())}</Text>}

          {refusal && <p className="text-danger">{refusal}</p>}

          <div className="hatch-form-actions">
            <Button variant="primary" disabled={!dirty} onClick={() => onSave(settingsRequest(draft, runner))}>
              Save settings
            </Button>
            <Button disabled={!dirty} onClick={() => setDraft(settingsOf(runner))}>
              Revert
            </Button>
          </div>
        </div>
      ) : (
        <>
          <Text>Tags: {runner.tags.length === 0 ? 'any runner' : runner.tags.join(', ')}</Text>
          <Text>Under: {runner.under ? underLink(runner.under) : 'the whole board'}</Text>
          <Text>Refuses: {runner.refusedModels.length === 0 ? 'nothing' : runner.refusedModels.join(', ')}</Text>
          {statuses && (
            <Text>Columns: {columnsWords(runner.statusMode ?? 'all', runner.statusIds ?? [], statuses)}</Text>
          )}
          <Text>Preferred: {runner.preferred ? 'yes' : 'no'}</Text>
        </>
      )}

      <Text>
        <strong>Declared by its sessions</strong>
      </Text>
      <Text>Projects: {projects.length === 0 ? 'every project' : projects.join(', ')}</Text>
      <Text tone="muted">Does not change the merge train.</Text>
      {dangerous.map((entry) => (
        <Text key={entry.where ?? ''}>
          Dangerous{entry.where ? ` on ${entry.where}` : ''}: {entry.dangerous ? 'Yes' : 'No'}
        </Text>
      ))}
      <Text tone="muted">
        From <code>--project</code> and <code>--dangerous</code> on each loop. Not editable here.
      </Text>
    </Card>
  );
}
