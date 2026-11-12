import { Navigate, NavLink, useParams } from 'react-router-dom';
import { DEFAULT_SLUG, SECTIONS, groupedSections } from './sections';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `design-nav-link${isActive ? ' active' : ''}`;

/** `/design` itself, with no slug: the rail's own first row. */
export function DesignIndexPage() {
  return <Navigate to={`/design/${DEFAULT_SLUG}`} replace />;
}

/** `/design/:slug`: the rail beside whichever section the slug names, or a
    redirect to the first section when it names none. */
export function DesignPage() {
  const { slug } = useParams();
  const section = SECTIONS.find((candidate) => candidate.slug === slug);
  const groups = groupedSections();

  if (!section) return <Navigate to={`/design/${DEFAULT_SLUG}`} replace />;

  const Page = section.Page;

  return (
    <div className="design-page">
      <nav className="design-rail" aria-label="Sections">
        <div className="design-rail-groups">
          {groups.map((group) => (
            <div key={group.group} className="design-nav-group">
              <h2 className="design-nav-group-name">{group.group}</h2>
              {group.sections.map((candidate) => (
                <NavLink key={candidate.slug} to={`/design/${candidate.slug}`} className={navLinkClass}>
                  {candidate.title}
                </NavLink>
              ))}
            </div>
          ))}
        </div>
      </nav>

      <main className="design-content">
        <Page />
      </main>
    </div>
  );
}
