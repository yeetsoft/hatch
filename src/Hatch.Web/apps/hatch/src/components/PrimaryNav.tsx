import { NavLink, useLocation } from 'react-router-dom';
import { Menu } from '@hatch/ui';
import type { MenuTone } from '@hatch/ui';
import { activeGroup, navFor } from '../lib/nav';
import type { NavEntry } from '../lib/nav';
import { useMe } from '../lib/useMe';
import { SearchGlyph } from './SearchGlyph';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `hatch-menu__trigger hatch-menu__trigger--accent${isActive ? ' hatch-menu__trigger--active' : ''}`;

const ICONS = { search: SearchGlyph };

function NavLinkEntry({ entry }: { entry: Extract<NavEntry, { kind: 'link' }> }) {
  if (!entry.icon) {
    return (
      <NavLink to={entry.to} end={entry.end} className={navLinkClass}>
        {entry.label}
      </NavLink>
    );
  }
  const Icon = ICONS[entry.icon];
  return (
    <NavLink to={entry.to} end={entry.end} className={navLinkClass} aria-label={entry.label} title={entry.label}>
      <Icon />
    </NavLink>
  );
}

/**
 * The app's nav: Search as the magnifying glass, Ambitions as a word,
 * Track, Agents and Manage as menus. The board is the logo's, and
 * Docs the settings menu's. `lib/nav.ts` is the one place to change what it
 * shows or where a page lives; this component only draws whatever that file
 * returns.
 *
 * Rides in the gradient bar's `leading` slot with `tone="accent"` - the
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
            <NavLinkEntry key={entry.to} entry={entry} />
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
