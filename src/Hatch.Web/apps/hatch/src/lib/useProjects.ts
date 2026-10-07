/* The project list every page can share: one fetch, a lookup by key, and the
   one rule that decides whether a project's mark is worth drawing at all. */

import { useMemo } from 'react';
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
  const { data } = useLoaded<Project[]>(getProjects);
  const projects = useMemo(() => data ?? [], [data]);
  const byKey = useMemo(() => new Map(projects.map((project) => [project.key, project])), [projects]);
  return { projects, byKey };
}

/** Whether there is a choice to make between projects - the one rule
    BoardFilters.tsx used to spell out inline to decide whether to draw its
    own Project `<select>`, named once so every place that hides a project
    control on a single-project install reads the same boolean. */
export function hasMultipleProjects(projects: Project[]): boolean {
  return projects.length > 1;
}
