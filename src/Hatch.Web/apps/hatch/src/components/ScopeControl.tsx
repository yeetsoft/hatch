import { useState } from 'react';
import { Menu, MenuCaret, ProjectMark, markVars } from '@hatch/ui';
import { hasMultipleProjects } from '../lib/useProjects';
import { toggleChoice } from '../lib/checklist';
import { glyphState, levelAfterPress } from '../lib/projectLevel';
import type { LevelGlyph } from '../lib/projectLevel';
import { putProjectPriority } from '../api/client';
import { message } from '../lib/errors';
import { ExpediteGlyph } from './ExpediteGlyph';
import { PauseGlyph } from './PauseGlyph';
import type { Project } from '../types';

const LEVEL_MARK: Record<'expedited' | 'paused', string> = { expedited: 'Expedited', paused: 'Paused' };

/** A row's expedite or pause button, for an owner alone - holds its own
    hover/focus state, since that is what half-fills the glyph before a
    level actually changes. */
function LevelButton({
  project,
  glyph,
  busy,
  onPress,
}: {
  project: Project;
  glyph: LevelGlyph;
  busy: boolean;
  onPress: () => void;
}) {
  const [active, setActive] = useState(false);
  const state = glyphState(project.priority, glyph, active);
  const next = levelAfterPress(project.priority, glyph);
  const text = `${project.key} — ${project.name}`;
  const label =
    glyph === 'expedite'
      ? next === 'expedited'
        ? `Expedite ${text}`
        : `Stop expediting ${text}`
      : next === 'paused'
        ? `Pause ${text}`
        : `Resume ${text}`;

  return (
    <button
      type="button"
      className={`hatch-scope-control__level hatch-scope-control__level--${glyph}`}
      aria-pressed={state === 'filled'}
      aria-label={label}
      disabled={busy}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
      onClick={onPress}
    >
      {glyph === 'expedite' ? <ExpediteGlyph state={state} /> : <PauseGlyph state={state} />}
    </button>
  );
}

/**
 * The topbar's own scope control: a chip-filled trigger naming the scoped
 * projects, and a panel that ticks any project in or out with no lock - the
 * one place this deliberately does not copy `ProjectsFacet`'s "keep at least
 * one" rule, since scoping to nothing is scoping to everything. Each row
 * carries `ChecklistFacet`'s per-row Only, but the footer carries no global
 * None beside All: a None here would just set the scope back to empty, which
 * already means every project, so it would read as another All (HA-2201).
 *
 * Each row also carries the project's level: an owner (`canAdminister`) gets
 * an expedite arrow and a pause button, between the name and Only, that flip
 * the project's `priority` at once and write it to the server - reverted,
 * with the refusal's sentence shown, if the write is refused. Everyone else
 * sees a plain mark for a project already at a level, and nothing for a
 * normal one.
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
  onApply,
}: {
  projects: Project[];
  keys: string[];
  onChange: (next: string[]) => void;
  onApply: (project: Project) => void;
}) {
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (!hasMultipleProjects(projects)) return null;

  const press = async (project: Project, glyph: LevelGlyph) => {
    if (busyId === project.id) return;
    const next = levelAfterPress(project.priority, glyph);
    setError(null);
    setBusyId(project.id);
    onApply({ ...project, priority: next });
    try {
      const answer = await putProjectPriority(project.id, { priority: next });
      onApply(answer);
    } catch (err) {
      onApply(project);
      setError(message(err));
    } finally {
      setBusyId(null);
    }
  };

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
        {error && (
          <div className="hatch-scope-control__error" role="alert">
            {error}
          </div>
        )}
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
              {project.canAdminister ? (
                <>
                  <LevelButton
                    project={project}
                    glyph="expedite"
                    busy={busyId === project.id}
                    onPress={() => void press(project, 'expedite')}
                  />
                  <LevelButton
                    project={project}
                    glyph="pause"
                    busy={busyId === project.id}
                    onPress={() => void press(project, 'pause')}
                  />
                </>
              ) : (
                project.priority !== 'normal' && (
                  <span
                    className="hatch-scope-control__level-mark"
                    role="img"
                    title={LEVEL_MARK[project.priority]}
                    aria-label={LEVEL_MARK[project.priority]}
                  >
                    {project.priority === 'expedited' ? <ExpediteGlyph state="filled" /> : <PauseGlyph state="filled" />}
                  </span>
                )
              )}
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
