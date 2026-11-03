import { Menu } from '@hatch/ui';
import { projectsSummary, toggleProject } from '../lib/cumulativeFlow';
import { Caret } from './Facet';
import { facetClass } from '../lib/facet';
import type { Project } from '../types';

/**
 * Which projects a report scopes to: a control that says its value and opens
 * a list of checkboxes - `TypesFacet`'s own shape, over projects rather than
 * issue types.
 */
export function ProjectsFacet({
  projects,
  chosen,
  onChange,
}: {
  projects: Project[];
  chosen: string[];
  onChange: (next: string[]) => void;
}) {
  const allKeys = projects.map((project) => project.key);
  const all = chosen.length === projects.length;

  return (
    <Menu
      label="Projects"
      hover={false}
      trigger={(props) => (
        <button {...props} className={facetClass(!all)}>
          <span className="hatch-facet__label">Projects</span>
          {projectsSummary(chosen, projects)}
          <Caret />
        </button>
      )}
    >
      <div className="hatch-types-panel" role="group" aria-label="Projects">
        {projects.map((project) => {
          const on = chosen.includes(project.key);
          const last = on && chosen.length === 1;
          return (
            <label
              key={project.key}
              className="hatch-types-row"
              title={last ? 'Keep at least one project' : undefined}
            >
              <input
                type="checkbox"
                checked={on}
                disabled={last}
                onChange={() => onChange(toggleProject(chosen, project.key, allKeys))}
              />
              <span className="hatch-types-name">
                {project.key} — {project.name}
              </span>
            </label>
          );
        })}
        <div className="hatch-types-footer">
          <button type="button" className="hatch-filter-clear" disabled={all} onClick={() => onChange(allKeys)}>
            All
          </button>
        </div>
      </div>
    </Menu>
  );
}
