import type { ComponentType, CSSProperties, ReactNode } from 'react';
import type { MeasuredPair, Palette } from '../theme/palette';
import { ROLES } from '../theme/palette/roles';
import './PaletteBoard.css';

export interface PaletteBoardProps {
  /** The candidate palette, as `derivePalette` returns it. Read, never changed. */
  palette: Palette;
  /** The measured contrast of every guarded pair, as `measurePairs` returns it. Whether a row clears is
      decided here from `ratio >= floor` on what is passed in, never assumed. */
  pairs: readonly MeasuredPair[];
  /** Rendered between the swatches and the readout: where the Instrument panel puts the dial and sliders. */
  children?: ReactNode;
}

const ACCENT_NAMES = ['primary', 'danger', 'success', 'warn'] as const;
const FLOW_KINDS = ['turn', 'family', 'person', 'other'] as const;

const NEUTRAL_TOKENS = ['--bg', '--card', '--ink', '--muted', '--line', '--line-strong'];
const ACCENT_TOKENS = [
  ...ACCENT_NAMES.flatMap((name) => [`--${name}`, `--${name}-bg`, `--${name}-ink`]),
  '--success-fill',
  '--on-accent',
  '--on-accent-wash',
];
const CHROME_TOKENS = ['--chrome', '--chrome-end', '--chrome-glow', '--on-chrome', '--on-chrome-wash'];
const FLOW_TOKENS = FLOW_KINDS.flatMap((kind) => [`--flow-${kind}`, `--on-flow-${kind}`]);

/** A token's value, or undefined when the palette does not carry it - which paints nothing rather than throwing. */
const paint = (palette: Palette, token: string): string | undefined => palette[token];

const fillStyle = (palette: Palette, fill: string, ink: string): CSSProperties => ({
  background: paint(palette, fill),
  color: paint(palette, ink),
});

/** `+0.03` / `-0.12`: two decimals, so a pair 0.01 short does not read as `4.5 / 4.5`. */
const signed = (margin: number) => `${margin < 0 ? '-' : '+'}${Math.abs(margin).toFixed(2)}`;

function Neutrals({ palette }: { palette: Palette }) {
  return (
    <div className="hatch-palette-board-sheet" style={fillStyle(palette, '--bg', '--ink')}>
      <div
        className="hatch-palette-board-card"
        style={{ background: paint(palette, '--card'), color: paint(palette, '--ink'), borderColor: paint(palette, '--line') }}
      >
        <span>--ink on --card</span>
        <span style={{ color: paint(palette, '--muted') }}>--muted on --card</span>
        <span className="hatch-palette-board-rule" style={{ background: paint(palette, '--line') }} />
        <span className="hatch-palette-board-rule" style={{ background: paint(palette, '--line-strong') }} />
      </div>
      <span>--ink on --bg</span>
    </div>
  );
}

function Accents({ palette }: { palette: Palette }) {
  const card = paint(palette, '--card');
  return (
    <div className="hatch-palette-board-specimen">
      {ACCENT_NAMES.map((name) => (
        <div key={name} className="hatch-palette-board-accent">
          <div className="hatch-palette-board-fill" style={fillStyle(palette, `--${name}`, '--on-accent')}>
            {name === 'primary' ? (
              <>
                <span>--primary</span>
                <span className="hatch-palette-board-wash-chip" style={{ background: paint(palette, '--on-accent-wash') }}>
                  --on-accent-wash
                </span>
              </>
            ) : (
              <span>--{name}</span>
            )}
          </div>
          <div className="hatch-palette-board-on-card" style={{ background: card, color: paint(palette, `--${name}-ink`) }}>
            --{name}-ink
          </div>
          <div className="hatch-palette-board-backing" style={{ background: card }}>
            <div className="hatch-palette-board-on-card" style={fillStyle(palette, `--${name}-bg`, `--${name}-ink`)}>
              --{name}-bg
            </div>
          </div>
        </div>
      ))}
      <div className="hatch-palette-board-accent">
        <div className="hatch-palette-board-fill" style={fillStyle(palette, '--success-fill', '--on-accent')}>
          --success-fill
        </div>
      </div>
    </div>
  );
}

function Chrome({ palette }: { palette: Palette }) {
  const start = paint(palette, '--chrome');
  const end = paint(palette, '--chrome-end');
  return (
    <div
      className="hatch-palette-board-chrome"
      style={{
        background: start && end ? `linear-gradient(135deg, ${start} 0%, ${end} 100%)` : undefined,
        color: paint(palette, '--on-chrome'),
      }}
    >
      <span>--on-chrome</span>
      <span style={{ color: paint(palette, '--chrome-glow') }}>--chrome-glow</span>
      <span className="hatch-palette-board-wash-chip" style={{ background: paint(palette, '--on-chrome-wash') }}>
        --on-chrome-wash
      </span>
    </div>
  );
}

function Flow({ palette }: { palette: Palette }) {
  return (
    <div className="hatch-palette-board-specimen">
      {FLOW_KINDS.map((kind) => (
        <div
          key={kind}
          className="hatch-palette-board-flow"
          style={fillStyle(palette, `--flow-${kind}`, `--on-flow-${kind}`)}
        >
          --on-flow-{kind}
        </div>
      ))}
    </div>
  );
}

interface Group {
  title: string;
  tokens: string[];
  Specimen: ComponentType<{ palette: Palette }>;
}

const GROUPS: Group[] = [
  { title: 'Neutrals', tokens: NEUTRAL_TOKENS, Specimen: Neutrals },
  { title: 'Accents', tokens: ACCENT_TOKENS, Specimen: Accents },
  { title: 'Chrome', tokens: CHROME_TOKENS, Specimen: Chrome },
  { title: 'Flow', tokens: FLOW_TOKENS, Specimen: Flow },
];

/** One legend entry. A wash is drawn over a `--card`-coloured backing so it reads. */
function Legend({ palette, tokens }: { palette: Palette; tokens: string[] }) {
  return (
    <ul className="hatch-palette-board-legend">
      {tokens.map((token) => (
        <li key={token} data-token={token}>
          <span className="hatch-palette-board-chip" style={{ background: paint(palette, '--card') }}>
            <span style={{ background: paint(palette, token) }} />
          </span>
          <code>{token}</code>
          <span>{palette[token]}</span>
        </li>
      ))}
    </ul>
  );
}

function Readout({ pairs }: { pairs: readonly MeasuredPair[] }) {
  return (
    <div className="hatch-palette-board-readout">
      <table>
        <caption>Measured contrast</caption>
        <thead>
          <tr>
            <th scope="col">Pair</th>
            <th scope="col">Ratio</th>
            <th scope="col">Floor</th>
            <th scope="col">Margin</th>
          </tr>
        </thead>
        <tbody>
          {pairs.map((pair, at) => {
            const clears = pair.ratio >= pair.floor;
            return (
              <tr key={at} data-pair data-state={clears ? 'clears' : 'under'}>
                <td>
                  {pair.fg} on {pair.bg}
                  {pair.over ? ` over ${pair.over}` : ''}
                </td>
                <td>{pair.ratio.toFixed(1)}:1</td>
                <td>{pair.floor.toFixed(1)}</td>
                <td>
                  {signed(pair.ratio - pair.floor)}
                  {clears ? '' : ' under floor'}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/**
 * A candidate palette as a swatch board, with the measured contrast of every guarded pair beneath it.
 *
 * It reads a palette and changes nothing. The legends are built from the tokens the palette carries that are not
 * `fixed`, so a token the engine adds appears here, under "Other", with no change to this file.
 */
export function PaletteBoard({ palette, pairs, children }: PaletteBoardProps) {
  const shown = (token: string) => palette[token] !== undefined && ROLES[token] !== 'fixed';
  const placed = new Set(GROUPS.flatMap((group) => group.tokens));
  const other = Object.keys(palette).filter((token) => !placed.has(token) && ROLES[token] !== 'fixed');

  return (
    <section className="hatch-palette-board" aria-label="Candidate palette">
      {GROUPS.map(({ title, tokens, Specimen }) => (
        <div key={title} className="hatch-palette-board-group">
          <h3>{title}</h3>
          <Specimen palette={palette} />
          <Legend palette={palette} tokens={tokens.filter(shown)} />
        </div>
      ))}
      {other.length > 0 ? (
        <div className="hatch-palette-board-group">
          <h3>Other</h3>
          <Legend palette={palette} tokens={other} />
        </div>
      ) : null}
      {children}
      <Readout pairs={pairs} />
    </section>
  );
}
