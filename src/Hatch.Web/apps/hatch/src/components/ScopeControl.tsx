import { useState } from 'react';
import { Menu, MenuCaret, ProjectMark, markVars } from '@hatch/ui';
import { hasMultipleProjects } from '../lib/useProjects';
import { toggleChoice } from '../lib/checklist';
import { leveled, levelAfterPress, setLevel } from '../lib/projectLevel';
import type { LevelGlyph } from '../lib/projectLevel';
import { putProjectPriority } from '../api/client';
import { message } from '../lib/errors';
import { ExpediteGlyph } from './ExpediteGlyph';
import { PauseGlyph } from './PauseGlyph';
import { LevelButton } from './LevelButton';
import type { Project, ProjectPriority } from '../types';

const LEVEL_MARK: Record<'expedited' | 'paused', string> = { expedited: 'Expedited', paused: 'Paused' };

/** One run of the cluster: the level's glyph, then a pill per project, the
    first on top. An owner's pill carries an x that sets the project back to
    normal; anyone else's is a plain mark titled with the project's name. */
function ClusterGroup({
  level,
  projects,
  busyId,
  onClear,
}: {
  level: 'expedited' | 'paused';
  projects: Project[];
  busyId: number | null;
  onClear: (project: Project) => void;
}) {
  if (projects.length === 0) return null;
  const title = `${LEVEL_MARK[level]} projects: ${projects.map((project) => project.key).join(', ')}`;
  return (
    <span className={`hatch-scope-cluster__group hatch-scope-cluster__group--${level}`} title={title}>
      <span className="hatch-scope-cluster__glyph" aria-hidden="true">
        {level === 'expedited' ? <ExpediteGlyph state="filled" /> : <PauseGlyph state="filled" />}
      </span>
      {projects.map((project, index) =>
        project.canAdminister ? (
          <span
            key={project.key}
            className="hatch-scope-cluster__pill"
            style={{ ...markVars(project.color), zIndex: projects.length - index }}
          >
            {project.key}
            <button
              type="button"
              className="hatch-scope-cluster__clear"
              aria-label={`Set ${project.key} back to normal`}
              disabled={busyId === project.id}
              onClick={() => onClear(project)}
            >
              ×
            </button>
          </span>
        ) : (
          <span
            key={project.key}
            className="hatch-scope-cluster__pill"
            style={{ ...markVars(project.color), zIndex: projects.length - index }}
            title={project.name}
          >
            {project.key}
          </span>
        ),
      )}
    </span>
  );
}

/** Beside the scope trigger, never inside it: which projects are expedited and
    which are paused. Draws nothing, not an empty box, when there are none. */
function LevelCluster({
  expedited,
  paused,
  busyId,
  onClear,
}: {
  expedited: Project[];
  paused: Project[];
  busyId: number | null;
  onClear: (project: Project) => void;
}) {
  if (expedited.length === 0 && paused.length === 0) return null;
  return (
    <div className="hatch-scope-cluster">
      <ClusterGroup level="expedited" projects={expedited} busyId={busyId} onClear={onClear} />
      <ClusterGroup level="paused" projects={paused} busyId={busyId} onClear={onClear} />
    </div>
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
 * Beside the trigger, a cluster says which projects are expedited or paused:
 * the level's glyph, then a pill per project, with an x on an owner's pill
 * that sets it back to normal. The glyphs ride inside the trigger as well.
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

  const change = async (project: Project, next: ProjectPriority) => {
    if (busyId === project.id) return;
    setError(null);
    setBusyId(project.id);
    try {
      await setLevel(project, next, { put: (request) => putProjectPriority(project.id, request), onApply });
    } catch (err) {
      setError(message(err));
    } finally {
      setBusyId(null);
    }
  };
  const press = (project: Project, glyph: LevelGlyph) => change(project, levelAfterPress(project.priority, glyph));

  const { expedited, paused } = leveled(projects);
  // Pressing either glyph in the trigger opens the panel, so these ride inside
  // it too: aria-hidden and untitled, leaving the trigger's own label alone.
  const triggerGlyphs = (expedited.length > 0 || paused.length > 0) && (
    <>
      {expedited.length > 0 && (
        <span className="hatch-scope-control__trigger-glyph hatch-scope-control__trigger-glyph--expedite" aria-hidden="true">
          <ExpediteGlyph state="filled" />
        </span>
      )}
      {paused.length > 0 && (
        <span className="hatch-scope-control__trigger-glyph hatch-scope-control__trigger-glyph--pause" aria-hidden="true">
          <PauseGlyph state="filled" />
        </span>
      )}
    </>
  );

  const scoped = keys.length > 0;
  const allKeys = projects.map((project) => project.key);
  // The hint rides on the button itself, so it shows over the padding and the
  // caret as well as the chips - a title on an inner chip covers only that chip.
  const scopeWords = `Projects in scope: ${projects
    .filter((project) => keys.includes(project.key))
    .map((project) => project.name)
    .join(', ')}`;

  return (
    <div className="hatch-scope-control">
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
              {triggerGlyphs}
              <MenuCaret />
            </button>
          ) : (
            <button {...props} className={`${props.className} hatch-scope-control--unscoped`}>
              All projects
              {triggerGlyphs}
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
                      current={project.priority}
                      name={text}
                      glyph="expedite"
                      busy={busyId === project.id}
                      onPress={() => void press(project, 'expedite')}
                    />
                    <LevelButton
                      current={project.priority}
                      name={text}
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
      <LevelCluster
        expedited={expedited}
        paused={paused}
        busyId={busyId}
        onClear={(project) => void change(project, 'normal')}
      />
    </div>
  );
}
