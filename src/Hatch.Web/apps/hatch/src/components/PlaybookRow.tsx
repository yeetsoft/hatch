import { Badge, Button, Text } from '@hatch/ui';
import { StatusPill } from './StatusPill';
import { TypeBadge } from './TypeBadge';
import { isOverridden, isReviewPlaybook, isUncheckedEdit, limitNotes, playbookLabel, transitionLabel } from '../lib/playbooks';
import { hasMultipleProjects } from '../lib/useProjects';
import type { Playbook, Project, Status } from '../types';

/**
 * One playbook's row in `PlaybooksPage`'s table: the facts, and the two
 * actions that open `PlaybookEditDialog` or delete it outright. Pure and
 * presentational, the `ProjectRow` shape - the page owns both pieces of
 * state Edit opens into.
 *
 * One `<td>` per header, matching `PlaybookFileRowLine`'s shape. The open
 * control is the button in the Transition cell - there is no separate Edit
 * button - and the `<tr>` takes the same click as a convenience; Delete
 * stays outside it, since a `<button>` cannot legally nest another one, and
 * stops the click from reaching the row's own handler.
 */
export function PlaybookRow({
  playbook,
  projects,
  statuses,
  note,
  dimmed,
  onEdit,
  onDelete,
}: {
  playbook: Playbook;
  projects: Project[];
  statuses: Status[];
  note?: string | null;
  dimmed?: boolean;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const fromStatus = statuses.find((s) => s.id === playbook.fromStatusId);
  const toStatus = statuses.find((s) => s.id === playbook.toStatusId);
  const multipleProjects = hasMultipleProjects(projects);

  return (
    <tr onClick={onEdit} className={dimmed ? 'hatch-playbook-row--dimmed' : undefined}>
      <td>
        <button
          type="button"
          className="hatch-playbook-row-open"
          onClick={onEdit}
          aria-label={`Edit ${transitionLabel(playbook)}`}
        >
          {isReviewPlaybook(playbook) ? (
            <>
              {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{playbook.fromStatusName}</strong>}{' '}
              <Text tone="muted" as="span">review playbook</Text>
            </>
          ) : (
            <>
              {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{playbook.fromStatusName}</strong>}
              <span className="hatch-playbook-arrow">→</span>
              {toStatus ? <StatusPill status={toStatus} /> : <strong>{playbook.toStatusName}</strong>}
            </>
          )}
        </button>
        {isUncheckedEdit(playbook) && <Badge tone="danger">a check with no denied tools can edit</Badge>}
        {playbook.unreachableReason && <Badge tone="danger">{playbook.unreachableReason}</Badge>}
        {isOverridden(playbook) && <Badge tone="muted">the operator&apos;s own wording</Badge>}
        {limitNotes(playbook).map((n) => (
          <Badge key={n} tone="muted">{n}</Badge>
        ))}
        {note && <Badge tone="muted">{note}</Badge>}
      </td>
      {multipleProjects && (
        <td>
          <Text tone="muted" as="span">{playbookLabel(playbook)}</Text>
        </td>
      )}
      <td>
        {playbook.types.length > 0 ? (
          playbook.types.map((t) => <TypeBadge key={t} type={t} />)
        ) : (
          <Text tone="muted" as="span">any</Text>
        )}
      </td>
      <td>{playbook.model}</td>
      <td>{playbook.effort}</td>
      <td>
        <Button
          variant="danger"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          Delete
        </Button>
      </td>
    </tr>
  );
}
