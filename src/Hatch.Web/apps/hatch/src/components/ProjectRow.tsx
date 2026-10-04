import { Button } from '@hatch/ui';
import type { Project } from '../types';

/**
 * One project's row in `ProjectsPage`'s table: the facts, and the two actions
 * that open `ProjectEditDialog` or the delete-confirm modal. Pure and
 * presentational - the page owns both pieces of state this opens.
 */
export function ProjectRow({
  project,
  onEdit,
  onDelete,
}: {
  project: Project;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <tr>
      <td>
        <code>{project.key}</code>
      </td>
      <td>{project.name}</td>
      <td>{project.issueCount}</td>
      <td>
        {project.repositories.length}
        {project.repositories.length > 0 && (
          <> <span className="text-muted">{project.repositories[0].canonical}</span></>
        )}
      </td>
      <td>
        <div className="hatch-row-actions">
          <Button onClick={onEdit}>Edit</Button>
          <Button variant="danger" disabled={project.issueCount > 0} onClick={onDelete}>
            Delete
          </Button>
        </div>
      </td>
    </tr>
  );
}
