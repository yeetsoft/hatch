import { NavLink, useLocation } from 'react-router-dom';
import { DocsSearch } from './DocsSearch';
import { CHANGELOG_LINK, DOC_PAGES, docSections } from '../lib/docs';
import { usePhone } from '../lib/viewport';

/** The docs sidebar: a search field, a link back to the landing, then every
    docSections(DOC_PAGES) section as its own fold with its pages as links. Desk:
    sticky, each section open on its own. Phone: the whole thing starts under one
    closed "Pages" fold, keyed on pathname so picking a page leaves it closed again
    rather than however the reader last left it - the search field goes with it,
    inside the same fold, so there is no second render path for either. */
export function DocsSidebar() {
  const isPhone = usePhone();
  const { pathname } = useLocation();

  const nav = (
    <nav className="hatch-docs-sidebar" aria-label="Docs">
      <DocsSearch />
      <NavLink to="/docs" end className="hatch-docs-sidebar-home">
        Docs
      </NavLink>
      {docSections(DOC_PAGES).map(({ section, pages }) => (
        <details key={section} open>
          <summary>{section}</summary>
          <ul className="hatch-docs-sidebar-list">
            {section === 'Reference' && (
              <li>
                <NavLink to={CHANGELOG_LINK.to}>{CHANGELOG_LINK.title}</NavLink>
              </li>
            )}
            {pages.map((page) => (
              <li key={page.slug}>
                <NavLink to={`/docs/${page.slug}`}>{page.title}</NavLink>
              </li>
            ))}
          </ul>
        </details>
      ))}
    </nav>
  );

  if (!isPhone) return nav;

  return (
    <details key={pathname} className="hatch-docs-sidebar-phone">
      <summary>Pages</summary>
      {nav}
    </details>
  );
}
