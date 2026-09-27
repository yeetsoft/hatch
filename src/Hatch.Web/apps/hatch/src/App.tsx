import { NavLink, Route, Routes } from 'react-router-dom';
import { Menu, TopBar } from '@hatch/ui';
import './App.css';
import { CreatedIssuesProvider } from './components/CreatedIssues';
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

export function App() {
  return (
    <MeProvider>
      <AppShell />
    </MeProvider>
  );
}

function AppShell() {
  const { me } = useMe();

  return (
    /* Above <Routes> and inside the router: a confirmation chicklet is raised
       on the board and then read on the issue page it links to, so the stack
       has to outlive the navigation between them. */
    <CreatedIssuesProvider>
      <div className="hatch-app">
        <TopBar
          appName="Hatch"
          homeHref={appHref('/')}
          width="full"
          leading={<PrimaryNav tone="accent" />}
          trailing={
            <>
              {/* What the bar says about this session rather than about the
                  board. Draws nothing on an installation that has no answer
                  for it - a cluster install has none. */}
              <NavUtilization />
              {/* Last, at the right end of the bar: whether the loop is
                  waiting on a person. Unlike the one above it this always draws
                  something - "nothing is waiting" is an answer, and it is the
                  one it gives most of the time. */}
              <NavAttention />
            </>
          }
          menu={
            <>
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
    </CreatedIssuesProvider>
  );
}
