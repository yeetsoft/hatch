import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation, useNavigate, useNavigationType } from 'react-router-dom';
import { useProjects } from './useProjects';
import { SCOPE, SCOPE_CLEARED, keepScope, knownKeys, rawScope, scopeProjectIds, scopeSearch, withScope } from './scope';
import type { Project } from '../types';

interface ScopeValue {
  keys: string[];
  /** The projects the scope names, or every project when unscoped. */
  projects: Project[];
  /** Every project, whatever the scope - what the scope control itself lists. */
  allProjects: Project[];
  isScoped: boolean;
  inScope(projectKey: string): boolean;
  setScope(keys: string[]): void;
  ids: number[];
  loaded: boolean;
}

const ScopeContext = createContext<ScopeValue>({
  keys: [],
  projects: [],
  allProjects: [],
  isScoped: false,
  inScope: () => true,
  setScope: () => undefined,
  ids: [],
  loaded: true,
});

/**
 * The scope the address names, carried across a click the way `lib/scope.ts`
 * describes, and validated against the project list. Mounted once in
 * `App()`, above `<AppShell />` and so above `<Routes>` - no page reads
 * `scope` from its own `useSearchParams`.
 */
export function ScopeProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const navigationType = useNavigationType();
  const navigate = useNavigate();
  const { projects, loaded } = useProjects();

  const [carried, setCarried] = useState<string[]>(() => rawScope(new URLSearchParams(location.search)));

  const step = keepScope({ location, navigationType, carried });

  useEffect(() => {
    setCarried(step.carried);
    if (step.redirect) navigate(step.redirect, { replace: true, state: location.state });
    /* Deps are [location.key] alone, not [location.key, step]: setScope
       changes the URL, not carried, so an effect re-running on carried could
       write the old scope back over a clear still in flight. The key, not
       search, because a PUSH to the same URL is still an arrival that has to
       run the keeper again. */
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.key]);

  /* `step.carried` is a new array on every render (`rawScope` builds one), so
     the memos below key on its content: `keys` and `ids` keep their identity
     until the scope or the project list changes, which is what a page's
     `useCallback` dep list reads. Keys hold no comma and are never empty, so
     the joined string round-trips. */
  const carriedKey = step.carried.join(',');
  const carriedKeys = useMemo(() => (carriedKey ? carriedKey.split(',') : []), [carriedKey]);
  const keys = useMemo(() => knownKeys(carriedKeys, projects), [carriedKeys, projects]);
  const ids = useMemo(() => scopeProjectIds(keys, projects), [keys, projects]);

  const value = useMemo<ScopeValue>(() => {
    const isScoped = keys.length > 0;
    const scoped = isScoped ? projects.filter((project) => keys.includes(project.key)) : projects;

    return {
      keys,
      projects: scoped,
      allProjects: projects,
      isScoped,
      inScope: (projectKey: string) => !isScoped || keys.includes(projectKey),
      ids,
      loaded,
      setScope: (next: string[]) => {
        const params = withScope(
          new URLSearchParams(location.search),
          next,
          projects.map((project) => project.key),
        );
        const cleared = !params.has(SCOPE);

        navigate(
          { pathname: location.pathname, search: scopeSearch(params), hash: location.hash },
          {
            replace: true,
            state: cleared
              ? { ...(location.state as object | null), [SCOPE_CLEARED]: true }
              : location.state,
          },
        );
      },
    };
  }, [keys, ids, projects, loaded, location, navigate]);

  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}

export const useScope = () => useContext(ScopeContext);
