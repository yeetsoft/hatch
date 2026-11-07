import { projectsSummary } from '../lib/cumulativeFlow';
import { ChecklistFacet } from './ChecklistFacet';
import type { Project } from '../types';

export function ProjectsFacet({
  projects,
  chosen,
  onChange,
}: {
  projects: Project[];
  chosen: string[];
  onChange: (next: string[]) => void;
}) {
  return (
    <ChecklistFacet
      label="Projects"
      groupLabel="Projects"
      summary={projectsSummary(chosen, projects)}
      options={projects.map((project) => ({ value: project.key, text: `${project.key} — ${project.name}` }))}
      chosen={chosen}
      onChange={onChange}
      lockTitle="Keep at least one project"
    />
  );
}
