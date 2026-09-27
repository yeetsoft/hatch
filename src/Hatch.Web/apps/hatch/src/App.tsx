import { NavLink, Route, Routes } from 'react-router-dom';
import { TopBar } from '@hatch/ui';
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
import { MeProvider } from './lib/useMe';

const navLinkClass = ({ isActive }: { isActive: boolean }) => `hatch-nav-link${isActive ? ' active' : ''}`;

export function App() {
  return (
    <MeProvider>
      <AppShell />
    </MeProvider>
  );
}

function AppShell() {
  return (
    /* Above <Routes> and inside the router: a confirmation chicklet is raised
       on the board and then read on the issue page it links to, so the stack
       has to outlive the navigation between them. */
    <CreatedIssuesProvider>
      <div className="hatch-app">
        <TopBar appName="Hatch" homeHref={appHref('/')} />

        <div className="hatch-nav">
          <div className="hatch-nav-content">
            <PrimaryNav tone="surface" />
            {/* A page like any other rather than a strip element: what it holds
                is this installation's own configuration, and it is the only place
                a Hatch with no admin app beside it can be configured at all. */}
            <NavLink to="/settings" className={navLinkClass}>Settings</NavLink>
            {/* A static file shipped beside this bundle rather than a route, so
                a plain anchor and a real page navigation - a NavLink would hand
                the path to this app's router, which owns none of it. It is
                never "active" the way a client route is, so it wears the
                resting class outright rather than the callback the others
                need. */}
            <a href="/apps/hatch/hatch-at-home.md" className="hatch-nav-link">Docs</a>

            {/* The right-hand group: what the strip says about this session
                rather than about the board. One box because elements each
                pushed right by their own auto margin would share the free space
                between them and land apart. The first two draw nothing on an
                installation that has no answer for them - a cluster install has
                neither - so the strip is a row of links and no gap where
                something used to be. */}
            <div className="hatch-nav-aside">
              <NavLocalPerson />
              <NavUtilization />
              {/* Last, at the right end of the strip: whether the loop is
                  waiting on a person. Unlike the two above it this always draws
                  something - "nothing is waiting" is an answer, and it is the
                  one it gives most of the time. */}
              <NavAttention />
            </div>
          </div>
        </div>

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
