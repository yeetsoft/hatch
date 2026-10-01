import { Fragment } from 'react';
import { NavLink, Route, Routes } from 'react-router-dom';
import { Menu, TopBar } from '@hatch/ui';
import './App.css';
import { AttentionProvider } from './components/AttentionProvider';
import { ConfirmationsProvider } from './components/Confirmations';
import { NavAttention } from './components/NavAttention';
import { NavLocalPerson } from './components/NavLocalPerson';
import { NavUtilization } from './components/NavUtilization';
import { PrimaryNav } from './components/PrimaryNav';
import { BoardPage } from './pages/BoardPage';
import { PlanPage } from './pages/PlanPage';
import { LeaderboardPage } from './pages/LeaderboardPage';
import { IssuePage } from './pages/IssuePage';
import { ProjectsPage } from './pages/ProjectsPage';
import { StatusesPage } from './pages/StatusesPage';
import { PlaybooksPage } from './pages/PlaybooksPage';
import { RunnersPage } from './pages/RunnersPage';
import { BulkPage } from './pages/BulkPage';
import { ImportPage } from './pages/ImportPage';
import { RunnerPage } from './pages/RunnerPage';
import { ApiKeysPage } from './pages/ApiKeysPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { appHref } from './lib/basename';
import { MeProvider, useMe } from './lib/useMe';
import { navRows } from './lib/nav';
import { usePhone } from './lib/viewport';

export function App() {
  return (
    <MeProvider>
      <AttentionProvider>
        <AppShell />
      </AttentionProvider>
    </MeProvider>
  );
}

function AppShell() {
  const { me, isAdmin } = useMe();
  const isPhone = usePhone();

  return (
    /* Above <Routes> and inside the router: a confirmation chicklet is raised
       on the board and then read on the issue page it links to, so the stack
       has to outlive the navigation between them - and its Undo has to work
       from either. */
    <ConfirmationsProvider>
      <div className="hatch-app">
        <TopBar
          appName="Hatch"
          homeHref={appHref('/')}
          width="full"
          /* Below the phone breakpoint PrimaryNav is absent rather than
             hidden by CSS - its triggers must not sit in the tab order when
             the same pages are reached from the gear's panel instead. */
          leading={isPhone ? undefined : <PrimaryNav tone="accent" />}
          trailing={
            isPhone ? (
              /* The one thing a phone user must always see: never narrower or
                 quieter than it is on a desk. The battery moves into the
                 gear's panel below instead - see .hatch-phone-menu. */
              <NavAttention />
            ) : (
              <>
                {/* What the bar says about this session rather than about the
                    board. Draws nothing until a runner of mine has reported a
                    reading - a fresh install, and anybody who has never run
                    one, are both in that state. */}
                <NavUtilization />
                {/* Last, at the right end of the bar: whether the loop is
                    waiting on a person. Unlike the one above it this always draws
                    something - "nothing is waiting" is an answer, and it is the
                    one it gives most of the time. */}
                <NavAttention />
              </>
            )
          }
          menu={
            <>
              {isPhone && (
                /* Everything the primary nav shows on the desk, flattened
                   with the battery folded in as a row - order: -1 in
                   App.css draws this ahead of Theme without TopBar.tsx's own
                   DOM order (Theme first) ever changing. */
                <div className="hatch-phone-menu">
                  {navRows(isAdmin).map((row, index, rows) => (
                    <Fragment key={row.to}>
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
                  <hr className="hatch-menu__divider" />
                  {/* No-ops until a runner of mine has reported a reading -
                      the same condition that hides it on the desk. */}
                  <NavUtilization />
                </div>
              )}
              <Menu.Item as={NavLink} to="/settings">Settings</Menu.Item>
              {/* A static file shipped beside this bundle rather than a route,
                  so a plain anchor and a real page navigation - a NavLink
                  would hand the path to this app's router, which owns none
                  of it. */}
              <Menu.Item as="a" href="/apps/hatch/hatch-at-home.md">Docs</Menu.Item>
              {me ? (
                <>
                  <hr className="hatch-menu__divider" />
                  <div className="hatch-menu__row">
                    <NavLocalPerson />
                  </div>
                </>
              ) : null}
            </>
          }
        />

        <main className="hatch-content">
          <Routes>
            <Route path="/" element={<BoardPage />} />
            <Route path="/plan" element={<PlanPage />} />
            <Route path="/leaderboard" element={<LeaderboardPage />} />
            <Route path="/bulk" element={<BulkPage />} />
            <Route path="/projects" element={<ProjectsPage />} />
            <Route path="/statuses" element={<StatusesPage />} />
            <Route path="/playbooks" element={<PlaybooksPage />} />
            <Route path="/runners" element={<RunnersPage />} />
            <Route path="/issues/:key" element={<IssuePage />} />
            <Route path="/import" element={<ImportPage />} />
            <Route path="/runner" element={<RunnerPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/users" element={<UsersPage />} />
            <Route path="/api-keys" element={<ApiKeysPage />} />
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
