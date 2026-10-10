import { Button, ProjectMark } from '@hatch/ui';
import { hasOwner } from '../lib/projectMembers';
import { projectLogoUrl } from '../lib/projectLogo';
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
  onClaim,
}: {
  project: Project;
  onEdit: () => void;
  onDelete: () => void;
  onClaim: () => void;
}) {
  return (
    <tr>
      <td>
        <ProjectMark
          size="sm"
          letters={project.key}
          color={project.color}
          icon={project.icon}
          logoUrl={projectLogoUrl(project)}
          title={project.name}
        />
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
          {!hasOwner(project.members) && <Button onClick={onClaim}>Claim</Button>}
          <Button onClick={onEdit}>Edit</Button>
          <Button variant="danger" disabled={project.issueCount > 0} onClick={onDelete}>
            Delete
          </Button>
        </div>
      </td>
    </tr>
  );
}
