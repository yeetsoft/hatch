import { Badge, Card, Table, Text } from '@hatch/ui';
import { StatusPill } from './StatusPill';
import { TypeBadge } from './TypeBadge';
import { PromptDiff } from './PromptDiff';
import { deniedToolsDraft, isReviewPlaybook, playbookLabel, projectLabel } from '../lib/playbooks';
import { proposalFieldRows, promptChanged } from '../lib/playbookProposals';
import type { Playbook, PlaybookProposalChange, Status } from '../types';

/**
 * One changed field of a proposed row: header, before/after table and
 * (when the prompt itself changed) its diff. A proposal with three changes
 * draws three of these - see PlaybooksPage, the next task, for the loop.
 *
 * No `projects` prop: `before`/`after` already carry their own
 * `projectKey`/`projectName`, so `projectLabel` needs nothing else, and
 * there is nothing here to gate a Project column behind.
 */
export function PlaybookProposalCard({
  change,
  statuses,
}: {
  change: PlaybookProposalChange;
  statuses: Status[];
}) {
  const { before, after } = change;
  const fromStatus = statuses.find((s) => s.id === before.fromStatusId);
  const toStatus = statuses.find((s) => s.id === before.toStatusId);

  return (
    <Card>
      <div className="hatch-playbook-proposal-head">
        {isReviewPlaybook(before) ? (
          <>
            {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{before.fromStatusName}</strong>}{' '}
            <Text tone="muted" as="span">review playbook</Text>
          </>
        ) : (
          <>
            {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{before.fromStatusName}</strong>}
            <span className="hatch-playbook-arrow">→</span>
            {toStatus ? <StatusPill status={toStatus} /> : <strong>{before.toStatusName}</strong>}
          </>
        )}
        {before.types.length > 0 ? (
          before.types.map((t) => <TypeBadge key={t} type={t} />)
        ) : (
          <Text tone="muted" as="span">any</Text>
        )}
        <Text tone="muted" as="span">
          {playbookLabel(before)}, {before.shape}
        </Text>
        {change.outOfDate && (
          <>
            <Badge tone="danger">out of date</Badge>
            <Text tone="muted" as="span">{change.outOfDateReason}</Text>
          </>
        )}
      </div>
      <Table>
        <thead>
          <tr>
            <th />
            <th>Before</th>
            <th>After</th>
          </tr>
        </thead>
        <tbody>
          {proposalFieldRows(change.changedFields).map((row) => (
            <tr key={row.field}>
              <th>{row.label}</th>
              <td className={row.changed ? 'hatch-playbook-proposal-cell--changed' : undefined}>
                {fieldValue(row.field, before, statuses)}
              </td>
              <td className={row.changed ? 'hatch-playbook-proposal-cell--changed' : undefined}>
                {fieldValue(row.field, after, statuses)}
              </td>
            </tr>
          ))}
        </tbody>
      </Table>
      {promptChanged(change.changedFields) && <PromptDiff defaultPrompt={before.prompt} prompt={after.prompt} />}
    </Card>
  );
}

function fieldValue(field: string, p: Playbook, statuses: Status[]) {
  switch (field) {
    case 'model':
      return p.model;
    case 'effort':
      return p.effort;
    case 'shape':
      return p.shape;
    case 'fromStatusId': {
      const status = statuses.find((s) => s.id === p.fromStatusId);
      return status ? <StatusPill status={status} /> : <strong>{p.fromStatusName}</strong>;
    }
    case 'toStatusId': {
      const status = statuses.find((s) => s.id === p.toStatusId);
      return status ? <StatusPill status={status} /> : <strong>{p.toStatusName}</strong>;
    }
    case 'types':
      return p.types.length > 0 ? (
        p.types.map((t) => <TypeBadge key={t} type={t} />)
      ) : (
        <Text tone="muted" as="span">any</Text>
      );
    case 'budget':
      return p.budget === null ? 'no cap' : p.budget;
    case 'briefLimit':
      return p.briefLimit === null ? 'no cap' : p.briefLimit;
    case 'deniedTools':
      return deniedToolsDraft(p.deniedTools) || 'every tool';
    case 'isCheck':
      return p.isCheck ? 'yes' : 'no';
    case 'projectId':
      return projectLabel(p);
    default:
      return null;
  }
}
