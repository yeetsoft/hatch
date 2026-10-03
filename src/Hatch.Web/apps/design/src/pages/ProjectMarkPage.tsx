import { PROJECT_ICONS, ProjectMark } from '@hatch/ui';
import type { ProjectMarkSize } from '@hatch/ui';
import { GalleryPage, GallerySection, TokenName } from '../components/Gallery';
import placeholderLogo from '../assets/placeholder-logo.png';

const SIZES: ProjectMarkSize[] = ['sm', 'md', 'lg'];
const PLUM = '#7a5cff';

/** A key long enough to show what the letters do when a project's key is not
    the usual two or three characters - the overflow this specimen exists to
    surface, not to hide. */
const LONG_KEY = 'HATCHY';

export function ProjectMarkPage() {
  return (
    <GalleryPage
      title="Project mark"
      blurb="A project's identity, drawn the same way everywhere one is shown: its own uploaded logo if it has one, else the icon it picked from the closed stock set, else its key's letters on a ground of its own colour."
    >
      <GallerySection
        title="Letters"
        note="The fallback every project starts with. The ground is the project's own colour, or --muted for one that has not chosen one; the ink is whichever of black or white reads on it."
      >
        <div className="row">
          {SIZES.map((size) => (
            <ProjectMark key={size} letters="HA" size={size} title="Hatch" />
          ))}
          {SIZES.map((size) => (
            <ProjectMark key={`${size}-color`} letters="HA" color={PLUM} size={size} title="Hatch" />
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="Icon"
        note="A slug from the stock set, drawn in the same ink the letters would have used."
      >
        <div className="row">
          {SIZES.map((size) => (
            <ProjectMark key={size} letters="HA" icon="rocket" size={size} title="Hatch" />
          ))}
          {SIZES.map((size) => (
            <ProjectMark key={`${size}-color`} letters="HA" icon="rocket" color={PLUM} size={size} title="Hatch" />
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="Logo"
        note="An uploaded logo takes precedence over both the icon and the letters. It is an <img> with object-fit: cover inside a ring of the project's colour, never inlined - the pixels are the operator's, not this house's to redraw."
      >
        <div className="row">
          {SIZES.map((size) => (
            <ProjectMark key={size} letters="HA" logoUrl={placeholderLogo} size={size} title="Hatch" />
          ))}
          {SIZES.map((size) => (
            <ProjectMark key={`${size}-color`} letters="HA" logoUrl={placeholderLogo} color={PLUM} size={size} title="Hatch" />
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="A key longer than two letters"
        note="Letters draws whatever it is handed rather than truncating to a fixed count - a six-letter key is the edge this exists to show, not one the component quietly solves."
      >
        <div className="row">
          {SIZES.map((size) => (
            <ProjectMark key={size} letters={LONG_KEY} color={PLUM} size={size} title="Hatchy" />
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="An icon this install does not recognise"
        note="A slug the stock set has no entry for draws the letters instead of refusing to render - a project can be mid-upgrade to a newer icon set without its mark going blank."
      >
        <div className="row">
          <ProjectMark letters="HA" icon="a-brand-new-slug-nobody-shipped-yet" size="lg" title="Hatch" />
        </div>
      </GallerySection>

      <GallerySection
        title="The whole stock set"
        note="Every icon `@hatch/ui` can draw - the picker's own reference, so a slug added here is a slug the picker already offers."
      >
        <div className="icon-grid">
          {PROJECT_ICONS.map(({ slug, title, Icon }) => (
            <div className="icon-grid-cell" key={slug}>
              <Icon className="icon-grid-glyph" />
              <TokenName name={slug} />
              <span className="icon-grid-title">{title}</span>
            </div>
          ))}
        </div>
      </GallerySection>
    </GalleryPage>
  );
}
