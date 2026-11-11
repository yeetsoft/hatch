import { Fragment, useEffect } from 'react';
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { Menu, TopBar } from '@hatch/ui';
import './App.css';
import { AttentionProvider } from './components/AttentionProvider';
import { ConfirmationsProvider } from './components/Confirmations';
import { NavAttention } from './components/NavAttention';
import { NavBuild } from './components/NavBuild';
import { NavLocalPerson } from './components/NavLocalPerson';
import { NavOvernight } from './components/NavOvernight';
import { NavSetupPrompt } from './components/NavSetupPrompt';
import { NavUtilization } from './components/NavUtilization';
import { OvernightProvider } from './components/OvernightProvider';
import { PrimaryNav } from './components/PrimaryNav';
import { PullToRefresh } from './components/PullToRefresh';
import { ScopeControl } from './components/ScopeControl';
import { DesignIndexPage, DesignPage } from './design/DesignPage';
import { BoardPage } from './pages/BoardPage';
import { PlanPage } from './pages/PlanPage';
import { DreamsPage } from './pages/DreamsPage';
import { ReportsPage } from './pages/ReportsPage';
import { SearchPage } from './pages/SearchPage';
import { TimelinePage } from './pages/TimelinePage';
import { CumulativeFlowPage } from './pages/CumulativeFlowPage';
import { ActivityReportPage } from './pages/ActivityReportPage';
import { ChangelogReportPage } from './pages/ChangelogReportPage';
import { ConsumptionReportPage } from './pages/ConsumptionReportPage';
import { LeaderboardPage } from './pages/LeaderboardPage';
import { IssueRoute } from './pages/IssueRoute';
import { ProjectsPage } from './pages/ProjectsPage';
import { StatusesPage } from './pages/StatusesPage';
import { PlaybooksPage } from './pages/PlaybooksPage';
import { RunnersLayout, RunnersIndex } from './components/RunnersLayout';
import { RunnerDetail } from './components/RunnerDetail';
import { BulkPage } from './pages/BulkPage';
import { HistoryPage } from './pages/HistoryPage';
import { ImportPage } from './pages/ImportPage';
import { DocsPage } from './pages/DocsPage';
import { RunnerPage } from './pages/RunnerPage';
import { OvernightPage } from './pages/OvernightPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { appHref } from './lib/basename';
import { MeProvider, useMe } from './lib/useMe';
import { scopedPath, scopeTitle } from './lib/scope';
import { ScopeProvider, useScope } from './lib/useScope';
import { hasMultipleProjects } from './lib/useProjects';
import { HOME, HOME_LABEL, navRows } from './lib/nav';
import { usePhone } from './lib/viewport';
import { usePullToRefresh } from './lib/usePullToRefresh';

export function App() {
  return (
    <MeProvider>
      <AttentionProvider>
        <OvernightProvider>
          <ScopeProvider>
            <AppShell />
          </ScopeProvider>
        </OvernightProvider>
      </AttentionProvider>
    </MeProvider>
  );
}

function AppShell() {
  const { me, isAdmin } = useMe();
  const isPhone = usePhone();
  const location = useLocation();
  const pull = usePullToRefresh();
  // ScopeProvider has already loaded the project list; a second useProjects()
  // here would be a second fetch.
  const { keys, allProjects, setScope } = useScope();

  // The only document.title write in the app. `keys` keeps its identity until
  // the scope changes, so this runs on a change of scope and not on a render.
  useEffect(() => {
    document.title = scopeTitle(keys);
  }, [keys]);

  return (
    /* Above <Routes> and inside the router: a confirmation chicklet is raised
       on the board and then read on the issue page it links to, so the stack
       has to outlive the navigation between them - and its Undo has to work
       from either. */
    <ConfirmationsProvider>
      <div className="hatch-app">
        <PullToRefresh {...pull} />
        <TopBar
          appName="Hatch"
          homeHref={appHref(scopedPath(HOME, keys))}
          /* The bar has no Board link: the logo is the way home, so it is
             named for where it goes and lit on the board. */
          homeLabel={HOME_LABEL}
          homeCurrent={location.pathname === HOME}
          width="full"
          /* Below the phone breakpoint PrimaryNav is absent rather than
             hidden by CSS - its triggers must not sit in the tab order when
             the same pages are reached from the gear's panel instead. The
             scope control goes with it: on a phone it is the strip under
             the bar, below. */
          leading={
            isPhone ? undefined : (
              <>
                <PrimaryNav tone="accent" />
                <ScopeControl projects={allProjects} keys={keys} onChange={setScope} />
              </>
            )
          }
          trailing={
            <>
              {/* What the bar says about this session rather than about the
                  board. Draws nothing until a runner of mine has reported a
                  reading - a fresh install, and anybody who has never run
                  one, are both in that state. */}
              <NavUtilization />
              {/* What the loop landed overnight, this reader's own unread
                  mark - a thing to go and look at, not to do, which is why it
                  sits beside the attention control rather than inside it. */}
              <NavOvernight />
              {/* Last, at the right end of the bar: whether the loop is
                  waiting on a person. Unlike the one above it this always draws
                  something - "nothing is waiting" is an answer, and it is the
                  one it gives most of the time. */}
              <NavAttention />
            </>
          }
          menu={
            <>
              {isPhone && (
                /* Everything the primary nav shows on the desk, flattened -
                   order: -1 in App.css draws this ahead of Theme without
                   TopBar.tsx's own DOM order (Theme first) ever changing. */
                <div className="hatch-phone-menu">
                  {navRows(isAdmin).map((row, index, rows) => (
                    <Fragment key={row.to}>
                      {/* A top-level row after a group's rows (Docs) has no
                          heading to set it apart, so it gets a rule. */}
                      {index > 0 && row.groupLabel === null && rows[index - 1].groupLabel !== null ? (
                        <hr className="hatch-menu__divider" />
                      ) : null}
                      {row.groupLabel !== null && row.groupLabel !== rows[index - 1]?.groupLabel ? (
                        <div className="hatch-menu__row">
                          <span className="hatch-menu__row-label">{row.groupLabel}</span>
                        </div>
                      ) : null}
                      <Menu.Item as={NavLink} to={row.to} end={row.end}>
                        {row.label}
                      </Menu.Item>
                    </Fragment>
                  ))}
                </div>
              )}
              <Menu.Item as={NavLink} to="/settings">Settings</Menu.Item>
              {/* Docs is a page of this app. On the phone it is already in the
                  panel above, from navRows, so it is drawn here only on the
                  desk, where the primary nav has no room for it behind a menu. */}
              {!isPhone && <Menu.Item as={NavLink} to="/docs">Docs</Menu.Item>}
              {/* The line that brings somebody new on, one press from any page.
                  Absent from a build stamped with no setup guide. */}
              <NavSetupPrompt />
              {me ? (
                <>
                  <hr className="hatch-menu__divider" />
                  <div className="hatch-menu__row">
                    <NavLocalPerson />
                  </div>
                </>
              ) : null}
              <NavBuild />
            </>
          }
        />

        {/* Gated here as well as inside ScopeControl: the strip has padding, so
            ungated it would draw an empty band on a one-project install. */}
        {isPhone && hasMultipleProjects(allProjects) && (
          <div className="hatch-scope-strip">
            <ScopeControl projects={allProjects} keys={keys} onChange={setScope} />
          </div>
        )}

        <main className="hatch-content">
          <Routes>
            <Route path="/" element={<BoardPage />} />
            <Route path="/plan" element={<PlanPage />} />
            <Route path="/dreams" element={<DreamsPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/reports/timeline" element={<TimelinePage />} />
            <Route path="/reports/cumulative-flow" element={<CumulativeFlowPage />} />
            <Route path="/reports/changelog" element={<ChangelogReportPage />} />
            <Route path="/reports/consumption" element={<ConsumptionReportPage />} />
            <Route path="/reports/activity" element={<ActivityReportPage />} />
            <Route path="/search" element={<SearchPage />} />
            <Route path="/leaderboard" element={<LeaderboardPage />} />
            <Route path="/bulk" element={<BulkPage />} />
            <Route path="/projects" element={<ProjectsPage />} />
            <Route path="/statuses" element={<StatusesPage />} />
            <Route path="/playbooks" element={<PlaybooksPage />} />
            <Route path="/design" element={<DesignIndexPage />} />
            <Route path="/design/:slug" element={<DesignPage />} />
            <Route path="/runners" element={<RunnersLayout />}>
              <Route index element={<RunnersIndex />} />
              <Route path=":name" element={<RunnerDetail />} />
            </Route>
            <Route path="/overnight" element={<OvernightPage />} />
            <Route path="/changelog" element={<Navigate to="/reports/changelog" replace />} />
            <Route path="/history" element={<HistoryPage />} />
            <Route path="/issues/:key" element={<IssueRoute />} />
            <Route path="/import" element={<ImportPage />} />
            <Route path="/runner" element={<RunnerPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/docs" element={<DocsPage />} />
            <Route path="/docs/*" element={<DocsPage />} />
            {/* An unknown deep link lands on the board rather than on nothing -
                the board is the app, and there is no page worth writing that
                says "that URL was wrong". */}
            <Route path="*" element={<BoardPage />} />
          </Routes>
        </main>
      </div>
    </ConfirmationsProvider>
  );
}
