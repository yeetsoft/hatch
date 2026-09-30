import { useCallback, useState } from 'react';
import { Button, Card, Field, PageHeader, Table } from '@hatch/ui';
import {
  createStatus,
  deleteStatus,
  getStatuses,
  getWip,
  patchStatus,
  putWip,
  setExpressSkips,
  setParentPulls,
} from '../api/client';
import { StatusPill } from '../components/StatusPill';
import { safeColor } from '../lib/color';
import { message } from '../lib/errors';
import { useLoaded } from '../lib/useLoaded';
import { limitDraft, limitRequest, toggled, wipBlocked } from '../lib/wip';
import type { Status, WipSliceSetting } from '../types';

export function StatusesPage() {
  const load = useCallback(
    () => Promise.all([getStatuses(), getWip()]).then(([statuses, section]) => ({ statuses, section })),
    [],
  );
  const { data, error, setError, reload } = useLoaded(load);
  const statuses = data?.statuses;
  const section = data?.section;
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);

  async function act(action: () => Promise<unknown>) {
    try {
      await action();
      await reload();
      setError(null);
    } catch (err) {
      setError(message(err));
    }
  }

  async function create() {
    setSaving(true);
    await act(async () => {
      await createStatus({ name });
      setName('');
    });
    setSaving(false);
  }

  /**
   * Reordering by swapping two rows' SortOrder values rather than by
   * renumbering the list. Two writes, no renumber, and a column that was
   * hand-placed between two others keeps whatever number it was given.
   */
  function swap(list: Status[], index: number, delta: number) {
    const a = list[index];
    const b = list[index + delta];
    if (!b) return;
    void act(async () => {
      await patchStatus(a.id, { sortOrder: b.sortOrder });
      await patchStatus(b.id, { sortOrder: a.sortOrder });
    });
  }

  return (
    <div className="hatch-page">
      <PageHeader
        title="Statuses"
        description="One status is one column on the board, left to right - unless it is deferred, which is a status the board does not draw."
      />

      {error && <p className="text-danger">{error}</p>}

      <Card>
        <h2 className="hatch-section-title">New column</h2>
        <div className="hatch-field-grid">
          <Field label="Name" hint="What the column is called on the board.">
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
        </div>
        <div className="hatch-form-actions">
          <Button variant="primary" loading={saving} disabled={!name} onClick={() => void create()}>
            Add column
          </Button>
        </div>
      </Card>

      {section && (
        <Card>
          <h2 className="hatch-section-title">WIP limits</h2>
          <div className="hatch-field-grid">
            <LimitField
              slice={section.slices[0]}
              label="WIP limit"
              hint="Stories and bugs across the WIP columns; blank means no limit."
              onSetLimit={(limit) => void act(() => putWip({ limit }))}
            />
            <LimitField
              slice={section.slices[1]}
              label="Epic limit"
              hint="Epics across the WIP columns; blank means no limit."
              onSetLimit={(epicLimit) => void act(() => putWip({ epicLimit }))}
            />
          </div>
        </Card>
      )}

      {statuses && section && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Colour</th>
                <th>Done column</th>
                <th title="Parked work. Not drawn on the board and not dragged into - the issue page is the only way in.">
                  Deferred
                </th>
                <th title="Whether a story, bug or epic here counts towards its limit above.">WIP</th>
                <th title="An express issue standing here is carried on to the next column with no session, as long as it has no unanswered question. The terminal column is never entered.">
                  Express skips
                </th>
                <th title="A child standing here is carried on to the next column with no session while its parent stands in the implementation column.">
                  Parent pulls
                </th>
                <th>Order</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {statuses.map((status, index) => (
                <tr key={status.id}>
                  <td>
                    <NameCell status={status} onRename={(next) => void act(() => patchStatus(status.id, { name: next }))} />
                  </td>
                  <td>
                    <ColorCell
                      status={status}
                      onRecolor={(color) => void act(() => patchStatus(status.id, { color }))}
                    />
                  </td>
                  <td>
                    <input
                      type="checkbox"
                      checked={status.isTerminal}
                      aria-label={`${status.name} means shipped`}
                      onChange={(e) => void act(() => patchStatus(status.id, { isTerminal: e.target.checked }))}
                    />
                  </td>
                  {/* Independent of the box beside it, the way the two flags are
                      stored apart: done is "this shipped", deferred is "stop
                      counting this", and a board is free to have several of
                      either. Ticking this takes the column off the board on the
                      next load - the issues in it keep their status and stay
                      reachable by key, by search, and from their parents. */}
                  <td>
                    <input
                      type="checkbox"
                      checked={status.isDeferred}
                      aria-label={`${status.name} is deferred`}
                      onChange={(e) => void act(() => patchStatus(status.id, { isDeferred: e.target.checked }))}
                    />
                  </td>
                  {/* Independent of Done and Deferred, and refused by the
                      server on either: a column that has just shipped or been
                      parked is disabled here before the refusal is ever sent -
                      see lib/wip.ts's wipBlocked. */}
                  <td>
                    <input
                      type="checkbox"
                      checked={section.statusIds.includes(status.id)}
                      disabled={wipBlocked(status) !== null}
                      title={wipBlocked(status) ?? undefined}
                      aria-label={`${status.name} is work in progress`}
                      onChange={(e) =>
                        void act(() => putWip({ statusIds: toggled(section, statuses, status.id, e.target.checked) }))
                      }
                    />
                  </td>
                  {/* Its own route rather than a field on the patch: writing it
                      is closed to an API key, because express decides which
                      gates the loop may pass unattended - see
                      StatusesController.PutExpressSkips. */}
                  <td>
                    <input
                      type="checkbox"
                      checked={status.expressSkips}
                      aria-label={`${status.name} is skipped by express work`}
                      onChange={(e) => void act(() => setExpressSkips(status.id, e.target.checked))}
                    />
                  </td>
                  {/* Its own route rather than a field on the patch, for the
                      same reason Express skips has one - see
                      StatusesController.PutParentPulls. */}
                  <td>
                    <input
                      type="checkbox"
                      checked={status.parentPulls}
                      aria-label={`${status.name} pulls its children on`}
                      onChange={(e) => void act(() => setParentPulls(status.id, e.target.checked))}
                    />
                  </td>
                  <td>
                    <div className="hatch-reorder">
                      <Button disabled={index === 0} aria-label={`Move ${status.name} left`} onClick={() => swap(statuses, index, -1)}>
                        ←
                      </Button>
                      <Button
                        disabled={index === statuses.length - 1}
                        aria-label={`Move ${status.name} right`}
                        onClick={() => swap(statuses, index, 1)}
                      >
                        →
                      </Button>
                    </div>
                  </td>
                  <td>
                    <Button variant="danger" onClick={() => void act(() => deleteStatus(status.id))}>
                      Delete
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}
    </div>
  );
}

/**
 * A column's colour, edited where it is read.
 *
 * The pill beside the picker is the point: the board writes the status name on
 * the colour, and whether that name comes out white or black is computed rather
 * than chosen (lib/color.ts). Showing the pill here is how the operator finds
 * out that their pale yellow is fine and their mid grey is not, before the
 * board tells them.
 *
 * Committed on blur rather than on change: a colour input fires continuously
 * while a swatch is being dragged around, and a PATCH per frame is a PATCH per
 * frame.
 */
function ColorCell({ status, onRecolor }: { status: Status; onRecolor: (color: string) => void }) {
  const [draft, setDraft] = useState(safeColor(status.color));
  const [known, setKnown] = useState(status.color);

  // Re-syncs when the row changes underneath - see InlineTitle on the issue page.
  if (status.color !== known) {
    setKnown(status.color);
    setDraft(safeColor(status.color));
  }

  return (
    <div className="hatch-color-cell">
      <input
        type="color"
        value={draft}
        aria-label={`${status.name} colour`}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          if (draft !== safeColor(status.color)) onRecolor(draft);
        }}
      />
      <StatusPill status={{ ...status, color: draft }} />
    </div>
  );
}

function NameCell({ status, onRename }: { status: Status; onRename: (name: string) => void }) {
  const [draft, setDraft] = useState(status.name);

  return (
    <input
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => {
        if (draft.trim() && draft !== status.name) onRename(draft.trim());
      }}
    />
  );
}

/**
 * How much of one slice may sit across the WIP columns at once. Committed on
 * blur, the way NameCell is, and only if changed - a blank box takes the
 * limit off rather than refusing "0", the same string-on-the-wire convention
 * `RunnerBounds` uses.
 */
function LimitField({
  slice,
  label,
  hint,
  onSetLimit,
}: {
  slice: WipSliceSetting;
  label: string;
  hint: string;
  onSetLimit: (limit: string) => void;
}) {
  const [draft, setDraft] = useState(limitDraft(slice));
  const [known, setKnown] = useState(slice.limit);

  // Re-syncs when the row changes underneath - see ColorCell above.
  if (slice.limit !== known) {
    setKnown(slice.limit);
    setDraft(limitDraft(slice));
  }

  return (
    <Field label={label} hint={hint}>
      <input
        inputMode="numeric"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const next = limitRequest(draft);
          if (next !== limitDraft(slice)) onSetLimit(next);
        }}
      />
    </Field>
  );
}
