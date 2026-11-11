import { Menu, MenuCaret, ProjectMark, markVars } from '@hatch/ui';
import { hasMultipleProjects } from '../lib/useProjects';
import { toggleChoice } from '../lib/checklist';
import type { Project } from '../types';

/**
 * The topbar's own scope control: a chip-filled trigger naming the scoped
 * projects, and a panel that ticks any project in or out with no lock - the
 * one place this deliberately does not copy `ProjectsFacet`'s "keep at least
 * one" rule, since scoping to nothing is scoping to everything. Each row
 * carries `ChecklistFacet`'s per-row Only, but the footer carries no global
 * None beside All: a None here would just set the scope back to empty, which
 * already means every project, so it would read as another All (HA-2201).
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
  // The hint rides on the button itself, so it shows over the padding and the
  // caret as well as the chips - a title on an inner chip covers only that chip.
  const scopeWords = `Projects in scope: ${projects
    .filter((project) => keys.includes(project.key))
    .map((project) => project.name)
    .join(', ')}`;

  return (
    <Menu
      label={scoped ? scopeWords : 'Projects in scope'}
      tone="accent"
      hover={false}
      trigger={(props) =>
        scoped ? (
          <button {...props} className={`${props.className} hatch-scope-control--scoped`} title={scopeWords}>
            {projects
              .filter((project) => keys.includes(project.key))
              .map((project) => (
                <span key={project.key} className="hatch-scope-control__chip" style={markVars(project.color)}>
                  {project.key}
                </span>
              ))}
            <MenuCaret />
          </button>
        ) : (
          <button {...props}>
            All projects
            <MenuCaret />
          </button>
        )
      }
    >
      <div className="hatch-types-panel hatch-scope-control__panel" role="group" aria-label="Projects">
        {projects.map((project) => {
          const on = keys.length === 0 || keys.includes(project.key);
          const text = `${project.key} — ${project.name}`;
          return (
            <div key={project.key} className="hatch-types-item">
              <label className="hatch-types-label">
                <input
                  type="checkbox"
                  checked={on}
                  onChange={() => onChange(toggleChoice(keys.length === 0 ? allKeys : keys, project.key, allKeys))}
                />
                <ProjectMark size="sm" letters={project.key} color={project.color} icon={project.icon} title={project.name} />
                <span className="hatch-types-name hatch-scope-control__name">{text}</span>
              </label>
              <button
                type="button"
                className="hatch-filter-clear hatch-types-only"
                aria-label={`Only ${text}`}
                onClick={() => onChange([project.key])}
              >
                Only
              </button>
            </div>
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
