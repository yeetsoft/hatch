import { Fragment } from 'react';
import { AmbitionTile, TypeTile } from '../../components/TypeGlyph';
import type { TileFill } from '../../components/TypeGlyph';
import { ISSUE_TYPE_ROWS } from '../../lib/issueType';
import type { PreApproval } from '../../lib/preApproval';
import type { Priority } from '../../lib/tileMarks';
import { GalleryPage, GallerySection, TokenName, TokenValue } from '../components/Gallery';
import { useTokenValues } from '../lib/useTokenValues';

/** The sizes the tile is judged at: the 14 a card's corner uses, the 16 and 24
    a control and a header use, and a 72 to see the drawing itself. */
const SIZES = [14, 16, 24, 72] as const;

/** The two sizes a mark is judged at: a 72 to see the drawing itself, and the card's 14. The 72 comes first because its trail and flame reach far to the left. */
const MARK_SIZES = [72, 14] as const;

interface MarkState {
  /** The name printed under the cell. */
  label: string;
  preApproval?: PreApproval;
  priority?: Priority;
  fill?: TileFill;
}

/* One list per channel, each state a cell. The heats are the five rows of the
   drawing: the pre-approved emergency is a state of its own, since it is the
   one that draws something different from its parts. */
const TRAIL_STATES: MarkState[] = [
  { label: 'none', preApproval: 'none' },
  { label: 'express', preApproval: 'express' },
  { label: 'preapproved', preApproval: 'preapproved' },
];

const HEAT_STATES: MarkState[] = [
  { label: 'normal', priority: 'normal' },
  { label: 'economy', priority: 'economy' },
  { label: 'expedited', priority: 'expedited' },
  { label: 'emergency', priority: 'emergency' },
  { label: 'emergency, pre-approved', priority: 'emergency', preApproval: 'preapproved' },
];

const FILL_STATES: MarkState[] = [
  { label: 'outline', fill: 'outline' },
  { label: 'solid', fill: 'solid' },
  { label: 'landed', fill: 'landed' },
];

/** One row per type, one cell per state, each at both sizes with the state's name under it. */
function MarkRows({ states }: { states: MarkState[] }) {
  return (
    <div>
      {ISSUE_TYPE_ROWS.map((row) => (
        <div className="design-mark-row" key={row.name}>
          <span className="design-mark-type">{row.word}</span>
          {states.map(({ label, ...marks }) => (
            <div className="design-mark-cell" key={label}>
              <div className="design-mark-pair">
                {MARK_SIZES.map((size) => (
                  <span className={`design-type-tile design-type-tile--${size}`} key={size}>
                    <TypeTile type={row.name} {...marks} />
                  </span>
                ))}
              </div>
              <span className="design-mark-state">{label}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

const AMBITION_TOKENS = ['--gradient-ink-from', '--gradient-ink-to'] as const;

/** The rows with a `--type-*` token. The ambition has none: its fill is the
    brand gradient, and it has its own section below. */
const TOKEN_ROWS = ISSUE_TYPE_ROWS.filter((row) => row.name !== 'ambition');

export function IssueTypesPage() {
  const values = useTokenValues([...TOKEN_ROWS.map((row) => `--type-${row.name}`), ...AMBITION_TOKENS]);

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
          {TOKEN_ROWS.map((row) => {
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
        title="The ambition"
        note="The tile for the ambition: the Hatch egg in --on-accent on the brand gradient, drawn from the mark's one source. Its fill is a gradient between the two tokens named here, not a --type-* token, so it is not a row above. The ambition is an issue type, and wears this tile wherever a type is drawn."
      >
        <div className="design-type-row design-type-row--ambition">
          {SIZES.map((size) => (
            <span className={`design-type-tile design-type-tile--${size}`} key={size}>
              <AmbitionTile />
            </span>
          ))}
          <span>ambition</span>
          {AMBITION_TOKENS.map((token) => (
            <Fragment key={token}>
              <TokenName name={token} />
              <TokenValue value={values[token]} />
            </Fragment>
          ))}
        </div>
      </GallerySection>

      <GallerySection
        title="Trail"
        note="Lines for pre-approval: none draws nothing, express draws one line behind the tile, and pre-approved draws three and leans the tile forward. The lines take the type's own colour at 65%. Each type is shown in each state, at the card's 14 pixels and at 72."
      >
        <MarkRows states={TRAIL_STATES} />
      </GallerySection>

      <GallerySection
        title="Heat"
        note="Flame for priority: economy draws no flame and fades the tile to 40%, normal draws nothing, expedited shows only the ember's tip above the tile, and emergency draws the whole flame. An emergency that is also pre-approved streams the flame back off the leaning tile, over two lines. The flame is amber with a paler core, and red is left to the bug. The tile takes an outline in the ground's colour where a flame is behind it."
      >
        <MarkRows states={HEAT_STATES} />
      </GallerySection>

      <GallerySection
        title="Fill"
        note="Fill for where a ticket stands: an outline is not begun, a solid tile is under way, and landed is a solid tile with a tick at its lower right. The tick reaches past the tile's box and is drawn whole."
      >
        <MarkRows states={FILL_STATES} />
      </GallerySection>

      <GallerySection
        title="At the size a card uses"
        note="The six tiles at 14 pixels on a card, on the ground a board column gives it. Read this one in both themes: if two of them cannot be told apart here, the other sizes do not matter."
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
