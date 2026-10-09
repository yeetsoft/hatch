import { useId } from 'react';
import { Button, Modal, Text } from '@hatch/ui';
import { ProjectChooser } from './ProjectChooser';
import type { MoveProject } from '../lib/useMoveProject';
import type { Project } from '../types';

/**
 * The dialog behind *Move…* on the issue page - presentational, like
 * `CloseSubtreeDialog`. Everything it shows past the project picker comes
 * from `move.summary`, already computed by `useMoveProject`; this component
 * never calls `moveSummary` itself.
 */
export function MoveProjectDialog({ move, projects }: { move: MoveProject; projects: Project[] }) {
  const name = useId();

  // Rendered unconditionally so the modal's own focus, escape and scrim
  // handling is the one that runs - see CloseSubtreeDialog for the same shape.
  if (!move.offer) return <Modal open={false} onClose={move.close} title="" />;

  const { offer, summary } = move;
  const currentProjectName = projects.find((p) => p.id === offer.issue.projectId)?.name;

  return (
    <Modal
      open
      onClose={move.close}
      title={`Move ${offer.issue.key} to another project?`}
      footer={
        <div className="hatch-form-actions">
          <Button onClick={move.close}>Cancel</Button>
          <Button
            variant="primary"
            loading={move.busy}
            disabled={!offer.projectId || move.loading || (move.summary?.liveClaims.length ?? 0) > 0}
            onClick={move.confirm}
          >
            Move it
          </Button>
        </div>
      }
    >
      <ProjectChooser
        legend="Move to"
        projects={projects}
        value={offer.projectId}
        disabledId={offer.issue.projectId}
        onChange={move.setProjectId}
      />

      {move.loading && <Text tone="muted">Reading what is under it…</Text>}

      {!move.loading && summary && (
        <>
          <p>{`${offer.issue.key}'s key will change, and the old one will stop resolving.`}</p>

          {summary.detachedFromParentKey && <p>{`It will no longer sit under ${summary.detachedFromParentKey}.`}</p>}

          {summary.descendantChoice && (
            <fieldset className="hatch-move-descendants">
              <label>
                <input
                  type="radio"
                  name={name}
                  checked={offer.moveDescendants}
                  onChange={() => move.setMoveDescendants(true)}
                />
                Move the {summary.descendantChoice.count} issues under it too
              </label>
              <label>
                <input
                  type="radio"
                  name={name}
                  checked={!offer.moveDescendants}
                  onChange={() => move.setMoveDescendants(false)}
                />
                Leave them in {currentProjectName} with no parent
              </label>
            </fieldset>
          )}

          {summary.pullRequests.length > 0 && (
            <ul className="hatch-cascade-list">
              {summary.pullRequests.map((candidate) => (
                <li key={candidate.key}>
                  <code>{candidate.key}</code> carries a pull request that will stay in {currentProjectName}'s
                  repository
                </li>
              ))}
            </ul>
          )}

          {summary.liveClaims.length > 0 && (
            <p className="text-danger">{summary.liveClaims.map((candidate) => candidate.key).join(', ')}</p>
          )}

          {move.error && <p className="text-danger">{move.error}</p>}
        </>
      )}
    </Modal>
  );
}
