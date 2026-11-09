import { Menu, ProjectMark, markVars } from '@hatch/ui';
import { hasMultipleProjects } from '../lib/useProjects';
import { toggleChoice } from '../lib/checklist';
import type { Project } from '../types';

/**
 * The topbar's own scope control: a chip-filled trigger naming the scoped
 * projects, and a panel that ticks any project in or out with no lock - the
 * one place this deliberately does not copy `ProjectsFacet`'s "keep at least
 * one" rule, since scoping to nothing is scoping to everything.
 *
 * `null` on a single-project install: the gate lives here, not in the
 * caller, so the "one project -> nothing" acceptance is a direct render test
 * on this component rather than needing an App.tsx-level test harness this
 * app does not have.
 */
export function ScopeControl({
  projects,
  keys,
  onChange,
}: {
  projects: Project[];
  keys: string[];
  onChange: (next: string[]) => void;
}) {
  if (!hasMultipleProjects(projects)) return null;

  const scoped = keys.length > 0;
  const allKeys = projects.map((project) => project.key);

  return (
    <Menu
      label="Projects in scope"
      tone="accent"
      hover={false}
      trigger={(props) =>
        scoped ? (
          <button {...props} className={`${props.className} hatch-scope-control--scoped`}>
            {projects
              .filter((project) => keys.includes(project.key))
              .map((project) => (
                <span key={project.key} className="hatch-scope-control__chip" style={markVars(project.color)} title={project.name}>
                  {project.key}
                </span>
              ))}
          </button>
        ) : (
          <button {...props}>All projects</button>
        )
      }
    >
      <div className="hatch-types-panel" role="group" aria-label="Projects">
        {projects.map((project) => {
          const on = keys.length === 0 || keys.includes(project.key);
          return (
            <label key={project.key} className="hatch-types-row">
              <input
                type="checkbox"
                checked={on}
                onChange={() => onChange(toggleChoice(keys.length === 0 ? allKeys : keys, project.key, allKeys))}
              />
              <ProjectMark size="sm" letters={project.key} color={project.color} icon={project.icon} title={project.name} />
              <span className="hatch-types-name">
                {project.key} — {project.name}
              </span>
            </label>
          );
        })}
        <div className="hatch-types-footer">
          <button type="button" className="hatch-filter-clear" disabled={keys.length === 0} onClick={() => onChange(allKeys)}>
            All
          </button>
        </div>
      </div>
    </Menu>
  );
}
