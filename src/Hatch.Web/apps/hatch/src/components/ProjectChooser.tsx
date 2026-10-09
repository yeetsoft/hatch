import { useId } from 'react';
import { ProjectMark, markVars } from '@hatch/ui';
import type { Project } from '../types';

export interface ProjectChooserProps {
  projects: Project[];
  value: number | null;
  onChange: (id: number) => void;
  /** Visible text, not a11y-only - "Project" in the New issue dialog, something
      else in the move dialog. */
  legend: string;
  /** The one project that cannot be chosen - unset in the New issue dialog,
      which excludes nothing; the move dialog's own project in the move
      dialog. */
  disabledId?: number | null;
  /** Rendered as one more tile, first, sharing this chooser's own radio group.
      Omitted by the New-issue and Move dialogs, which have no "leave it alone"
      state to offer. */
  keepLabel?: string;
  onKeep?: () => void;
}

/**
 * Same radio-group pattern as `ProjectIconPicker` and `@hatch/ui`'s
 * `ThemeSwitch` - a real `<input type="radio">` per option, so arrow-key/tab
 * navigation and the screen-reader grouping come from the browser. Its own
 * component because the issue page's move dialog (HA-236) draws the same row.
 *
 * Unlike those two, the legend here is visible text - it is the control's
 * only label, never wrapped in a `<Field>` - so it is not clipped offscreen.
 *
 * The checked tile fills with the project's own colour, via `markVars` - the
 * same custom properties `ProjectMark` itself paints from - set on every tile
 * so the CSS can read them only on the `:checked` one.
 */
export function ProjectChooser({ projects, value, onChange, legend, disabledId, keepLabel, onKeep }: ProjectChooserProps) {
  const name = useId();

  return (
    <fieldset className="hatch-project-chooser">
      <legend className="hatch-project-chooser__legend">{legend}</legend>
      <div className="hatch-project-chooser__tiles">
        {keepLabel && (
          <label className="hatch-project-chooser__tile">
            <input type="radio" name={name} checked={value === null} onChange={() => onKeep?.()} />
            <span>{keepLabel}</span>
          </label>
        )}
        {projects.map((project) => {
          const disabled = project.id === disabledId;
          return (
            <label
              key={project.id}
              className={
                disabled ? 'hatch-project-chooser__tile hatch-project-chooser__tile--disabled' : 'hatch-project-chooser__tile'
              }
              style={markVars(project.color)}
            >
              <input
                type="radio"
                name={name}
                checked={value === project.id}
                onChange={() => onChange(project.id)}
                disabled={disabled}
              />
              <ProjectMark size="sm" letters={project.key} color={project.color} icon={project.icon} title={project.name} />
              <span>
                {project.key} {project.name}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
