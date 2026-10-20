import { Fragment } from 'react';
import { NavLink, Route, Routes } from 'react-router-dom';
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
import { BoardPage } from './pages/BoardPage';
import { PlanPage } from './pages/PlanPage';
import { ReportsPage } from './pages/ReportsPage';
import { SearchPage } from './pages/SearchPage';
import { TimelinePage } from './pages/TimelinePage';
import { CumulativeFlowPage } from './pages/CumulativeFlowPage';
import { LeaderboardPage } from './pages/LeaderboardPage';
import { IssueRoute } from './pages/IssueRoute';
import { ProjectsPage } from './pages/ProjectsPage';
import { StatusesPage } from './pages/StatusesPage';
import { PlaybooksPage } from './pages/PlaybooksPage';
import { RunnersPage } from './pages/RunnersPage';
import { BulkPage } from './pages/BulkPage';
import { HistoryPage } from './pages/HistoryPage';
import { ImportPage } from './pages/ImportPage';
import { DocsPage } from './pages/DocsPage';
import { RunnerPage } from './pages/RunnerPage';
import { OvernightPage } from './pages/OvernightPage';
import { ApiKeysPage } from './pages/ApiKeysPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { appHref } from './lib/basename';
import { MeProvider, useMe } from './lib/useMe';
import { navRows } from './lib/nav';
import { usePhone } from './lib/viewport';
import { usePullToRefresh } from './lib/usePullToRefresh';

export function App() {
  return (
    <MeProvider>
      <AttentionProvider>
        <OvernightProvider>
          <AppShell />
        </OvernightProvider>
      </AttentionProvider>
    </MeProvider>
  );
}

function AppShell() {
  const { me, isAdmin } = useMe();
  const isPhone = usePhone();
  const pull = usePullToRefresh();

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
          homeHref={appHref('/')}
          width="full"
          /* Below the phone breakpoint PrimaryNav is absent rather than
             hidden by CSS - its triggers must not sit in the tab order when
             the same pages are reached from the gear's panel instead. */
          leading={isPhone ? undefined : <PrimaryNav tone="accent" />}
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

        <main className="hatch-content">
          <Routes>
            <Route path="/" element={<BoardPage />} />
            <Route path="/plan" element={<PlanPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/reports/timeline" element={<TimelinePage />} />
            <Route path="/reports/cumulative-flow" element={<CumulativeFlowPage />} />
            <Route path="/search" element={<SearchPage />} />
            <Route path="/leaderboard" element={<LeaderboardPage />} />
            <Route path="/bulk" element={<BulkPage />} />
            <Route path="/projects" element={<ProjectsPage />} />
            <Route path="/statuses" element={<StatusesPage />} />
            <Route path="/playbooks" element={<PlaybooksPage />} />
            <Route path="/runners" element={<RunnersPage />} />
            <Route path="/overnight" element={<OvernightPage />} />
            <Route path="/history" element={<HistoryPage />} />
            <Route path="/issues/:key" element={<IssueRoute />} />
            <Route path="/import" element={<ImportPage />} />
            <Route path="/runner" element={<RunnerPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/api-keys" element={<ApiKeysPage />} />
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
