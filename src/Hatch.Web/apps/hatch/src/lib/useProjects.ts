/* The project list every page can share: one fetch, a lookup by key, and the
   one rule that decides whether a project's mark is worth drawing at all. */

import { useCallback, useMemo } from 'react';
import { getProjects } from '../api/client';
import { useLoaded } from './useLoaded';
import type { Project } from '../types';

/**
 * The projects Hatch has, and a lookup by key. No `error` is returned: a
 * project list that failed to load is swallowed the way BoardPage's own
 * mount effect already swallows it today - a board or an issue page is
 * readable without it.
 */
export function useProjects() {
  const { data, setData } = useLoaded<Project[]>(getProjects);
  const projects = useMemo(() => data ?? [], [data]);
  const byKey = useMemo(() => new Map(projects.map((project) => [project.key, project])), [projects]);

  /** Replaces one project in place, for the scope control's optimistic level
      write and its revert - this page's own `useProjects()` call still
      catches up to the server's answer on its next focus or reload. */
  const applyProject = useCallback(
    (project: Project) => setData((prev) => (prev ?? []).map((existing) => (existing.id === project.id ? project : existing))),
    [setData],
  );

  return { projects, byKey, loaded: data !== null, applyProject };
}

/** Whether there is a choice to make between projects - the one rule
    BoardFilters.tsx used to spell out inline to decide whether to draw its
    own Project `<select>`, named once so every place that hides a project
    control on a single-project install reads the same boolean. */
export function hasMultipleProjects(projects: Project[]): boolean {
  return projects.length > 1;
}
