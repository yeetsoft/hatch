import type { Project } from '../types';
import { creatorChoices } from '../lib/timeline';
import { hasMultipleProjects } from '../lib/useProjects';
import { Facet } from './Facet';

/** The timeline's filters: which project, and whose work. It reports what was
    chosen and writes nothing - the page that owns the URL does - and fetches
    nothing: the projects and the creators are handed in. */
export function TimelineFilters({
  projects,
  project,
  creators,
  creator,
  onProject,
  onCreator,
  scoped = false,
}: {
  projects: Project[];
  /** The chosen project's key, `''` for all. */
  project: string;
  creators: string[];
  /** The chosen creator's name, `''` for anyone. */
  creator: string;
  onProject: (value: string) => void;
  onCreator: (value: string) => void;
  /** The tab narrows the projects handed in, so the empty option says so. */
  scoped?: boolean;
}) {
  return (
    <div className="hatch-timeline-controls" role="group" aria-label="Filters">
      {/* Drawn only when there is a choice to make, as on the board. */}
      {hasMultipleProjects(projects) && (
        <Facet
          label="Project"
          lit={project !== ''}
          value={project}
          options={[
            { value: '', text: scoped ? 'All in scope' : 'All projects' },
            ...projects.map((p) => ({ value: p.key, text: `${p.key} — ${p.name}` })),
          ]}
          onChange={onProject}
        />
      )}
      <Facet
        label="Creator"
        lit={creator !== ''}
        value={creator}
        options={[
          { value: '', text: 'Anyone' },
          ...creatorChoices(creators, creator).map((name) => ({ value: name, text: name })),
        ]}
        onChange={onCreator}
      />
    </div>
  );
}
