import { NavLink, useLocation } from 'react-router-dom';
import { Menu } from '@hatch/ui';
import type { MenuTone } from '@hatch/ui';
import { activeGroup, navFor } from '../lib/nav';
import { useMe } from '../lib/useMe';

const navLinkClass = ({ isActive }: { isActive: boolean }) => `hatch-nav-link${isActive ? ' active' : ''}`;

/**
 * The app's four-way nav: Board and Plan as links, Agents and Manage as
 * menus. `lib/nav.ts` is the one place to change what it shows or where a
 * page lives; this component only draws whatever that file returns.
 *
 * HA-25 moves this element into the gradient bar with `tone="accent"` - the
 * component itself does not change, only the tone it is passed and the box it
 * sits in.
 */
export function PrimaryNav({ tone }: { tone: MenuTone }) {
  const { isAdmin } = useMe();
  const location = useLocation();
  const lit = activeGroup(location.pathname);

  return (
    <nav className="hatch-primary-nav" aria-label="Primary">
      <Menu.Bar>
        {navFor(isAdmin).map((entry) =>
          entry.kind === 'link' ? (
            <NavLink key={entry.to} to={entry.to} end={entry.end} className={navLinkClass}>
              {entry.label}
            </NavLink>
          ) : (
            <Menu key={entry.label} label={entry.label} tone={tone} active={entry.label === lit?.label}>
              {entry.rows.map((row) => (
                <Menu.Item key={row.to} as={NavLink} to={row.to}>
                  {row.label}
                </Menu.Item>
              ))}
            </Menu>
          ),
        )}
      </Menu.Bar>
    </nav>
  );
}
