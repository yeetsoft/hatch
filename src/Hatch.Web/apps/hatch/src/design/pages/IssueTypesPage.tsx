import { TypeTile } from '../../components/TypeGlyph';
import { ISSUE_TYPE_ROWS } from '../../lib/issueType';
import { GalleryPage, GallerySection, TokenName, TokenValue } from '../components/Gallery';
import { useTokenValues } from '../lib/useTokenValues';

/** The sizes the tile is judged at: the 14 a card's corner uses, the 16 and 24
    a control and a header use, and a 72 to see the drawing itself. */
const SIZES = [14, 16, 24, 72] as const;

export function IssueTypesPage() {
  const values = useTokenValues(ISSUE_TYPE_ROWS.map((row) => `--type-${row.name}`));

  return (
    <GalleryPage
      title="Issue types"
      blurb="The tile each type wears in a card's corner: an outline, a picture and a colour of its own. It is judged at the smallest size, where one tile has to be told from its neighbours."
    >
      <GallerySection
        title="Every type"
        note="Each tile is the real component, sized here and nowhere else. The colour ramp runs deepest at the largest thing: epic, then story, then task. Red is the bug's alone. A rounded square is a size of work; a disc marks the two types that are not one, bug and reflection. The picture is always --on-accent, whatever the type's colour."
      >
        <div>
          {ISSUE_TYPE_ROWS.map((row) => {
            const token = `--type-${row.name}`;
            return (
              <div className="design-type-row" key={row.name}>
                {SIZES.map((size) => (
                  <span className={`design-type-tile design-type-tile--${size}`} key={size}>
                    <TypeTile type={row.name} />
                  </span>
                ))}
                <span>{row.word}</span>
                <TokenName name={token} />
                <TokenValue value={values[token]} />
              </div>
            );
          })}
        </div>
      </GallerySection>

      <GallerySection
        title="At the size a card uses"
        note="The five tiles at 14 pixels on a card, on the ground a board column gives it. Read this one in both themes: if two of them cannot be told apart here, the other sizes do not matter."
      >
        <div className="design-type-column">
          <div className="design-type-card">
            {ISSUE_TYPE_ROWS.map((row) => (
              <span className="design-type-tile design-type-tile--14" key={row.name}>
                <TypeTile type={row.name} />
              </span>
            ))}
          </div>
        </div>
      </GallerySection>
    </GalleryPage>
  );
}
