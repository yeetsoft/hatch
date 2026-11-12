import { Button, Text } from '@hatch/ui';
import {
  measurePairs,
  PARAMETERS,
  PARAMETER_KEYS,
  type Palette,
  type ParameterSpec,
  type PaletteParams,
} from '@hatch/ui/palette';
import { GalleryPage, GallerySection } from '../components/Gallery';
import { ACCENT_TOKENS, rimMarks, SEED, sketchPalettes, windowBands } from '../lib/paletteSketch';

/* Three arrangements of one set of parts, each drawn at the fixed seed.
   Nothing here is live: every dial, slider and button is `inert`, and
   Revert/Apply/Roll do nothing yet - this round is sketches for a person to
   look at, and the pick is the outcome. The seed's own text stays selectable,
   so it can still be copied by hand while the button beside it cannot be
   pressed. */

const ACCENT_LABELS: Record<string, string> = {
  '--primary': 'Primary',
  '--danger': 'Danger',
  '--success': 'Success',
  '--warn': 'Warn',
};

const FLOW_KINDS = ['turn', 'family', 'person', 'other'] as const;
const FLOW_LABELS: Record<(typeof FLOW_KINDS)[number], string> = {
  turn: 'Turn',
  family: 'Family',
  person: 'Person',
  other: 'Other',
};

/** The parameters a slider draws - every one but `hue`, which the dial shows instead. */
const SLIDER_KEYS = PARAMETER_KEYS.filter((key) => key !== 'hue');

interface Layout {
  key: 'instrument' | 'split' | 'strip';
  title: string;
  note: string;
}

const LAYOUTS: Layout[] = [
  {
    key: 'instrument',
    title: 'Instrument panel',
    note: 'Board across the top, dial and sliders in a row beneath it, seed and actions in a foot - a glanceable top-to-bottom read, the way a dashboard is laid out. It costs width: the dial and the sliders share one row, and that row is the first to feel narrow below the panel’s own 720px.',
  },
  {
    key: 'split',
    title: 'Split cockpit',
    note: 'Input on the left - dial above its sliders - output on the right - board above the readout - so cause and effect sit side by side rather than stacked. It costs the most horizontal room of the three: two real columns, not one column with a row inside it, so it is the one that crowds first when the panel narrows.',
  },
  {
    key: 'strip',
    title: 'Readout strip',
    note: 'The board as a strip of chips, the readout as a terminal list, the dial centred between them, actions set off at the foot as if pinned - the console’s own idiom, one column read straight down. It costs length: nothing competes for width, but reaching the actions means passing every other part first.',
  },
];

function SwatchBoard({ palette }: { palette: Palette }) {
  return (
    <div className="design-pc-board">
      <div className="design-pc-board-row">
        <div className="design-pc-paper" style={{ background: palette['--bg'], color: palette['--ink'] }}>
          <span>--bg</span>
          <span className="design-pc-paper-ink">--ink</span>
        </div>
        <div
          className="design-pc-paper"
          style={{ background: palette['--card'], color: palette['--muted'], borderColor: palette['--line'] }}
        >
          <span>--card</span>
          <span className="design-pc-paper-ink">--muted</span>
        </div>
      </div>

      <div className="design-pc-board-row">
        {ACCENT_TOKENS.map((token) => {
          const name = token.slice(2);
          return (
            <div key={token} className="design-pc-accent">
              <div className="design-pc-accent-fill" style={{ background: palette[token], color: palette['--on-accent'] }}>
                {ACCENT_LABELS[token]}
              </div>
              <div
                className="design-pc-accent-wash"
                style={{ background: palette[`--${name}-bg`], color: palette[`--${name}-ink`] }}
              >
                --{name}-ink
              </div>
            </div>
          );
        })}
      </div>

      <div
        className="design-pc-chrome"
        style={{
          background: `linear-gradient(135deg, ${palette['--chrome']} 0%, ${palette['--chrome-end']} 100%)`,
          color: palette['--on-chrome'],
        }}
      >
        <span>Chrome</span>
        <span style={{ color: palette['--chrome-glow'] }}>--chrome-glow</span>
      </div>

      <div className="design-pc-board-row">
        {FLOW_KINDS.map((kind) => (
          <div
            key={kind}
            className="design-pc-flow"
            style={{ background: palette[`--flow-${kind}`], color: palette[`--on-flow-${kind}`] }}
          >
            {FLOW_LABELS[kind]}
          </div>
        ))}
      </div>
    </div>
  );
}

function ContrastReadout({ palette }: { palette: Palette }) {
  return (
    <ul className="design-pc-contrast">
      {measurePairs(palette).map((pair, at) => {
        const lit = pair.ratio >= pair.floor;
        return (
          <li key={at} className={`design-pc-contrast-row${lit ? ' is-lit' : ''}`}>
            <span className="design-pc-contrast-pair">
              {pair.fg} / {pair.bg}
            </span>
            <span className="design-pc-contrast-ratio">{pair.ratio.toFixed(2)}:1</span>
            <span className="design-pc-contrast-floor">floor {pair.floor}</span>
            <span className="design-pc-contrast-verdict">{lit ? 'clears' : 'short'}</span>
          </li>
        );
      })}
    </ul>
  );
}

function HueDial({ palette }: { palette: Palette }) {
  const marks = rimMarks(palette);
  const radius = 42;
  const centre = 50;
  return (
    <svg className="design-pc-dial" viewBox="0 0 100 100" role="img" aria-label="The derived accents, around the hue dial">
      <circle cx={centre} cy={centre} r={radius} className="design-pc-dial-ring" />
      {marks.map((mark) => {
        const rad = (mark.hue * Math.PI) / 180;
        const x = centre + radius * Math.cos(rad);
        const y = centre + radius * Math.sin(rad);
        return <circle key={mark.token} cx={x} cy={y} r={5} fill={mark.colour} className="design-pc-dial-mark" />;
      })}
    </svg>
  );
}

function ParameterSlider({ name, spec, value }: { name: string; spec: ParameterSpec; value: number }) {
  const { min, max } = spec;
  const pct = (v: number) => ((v - min) / (max - min)) * 100;
  return (
    <div className="design-pc-slider">
      <div className="design-pc-slider-head">
        <span>{name}</span>
        <span className="design-pc-slider-value">{value.toFixed(3)}</span>
      </div>
      <div className="design-pc-slider-track">
        {windowBands(spec).map(([lo, hi], at) => (
          <span
            key={at}
            className="design-pc-slider-band"
            style={{ left: `${pct(lo)}%`, width: `${pct(hi) - pct(lo)}%` }}
          />
        ))}
        <input
          type="range"
          inert
          readOnly
          tabIndex={-1}
          min={min}
          max={max}
          value={value}
          className="design-pc-slider-input"
          aria-label={name}
        />
      </div>
    </div>
  );
}

function SeedField({ seed }: { seed: string }) {
  return (
    <div className="design-pc-seed">
      <input type="text" readOnly value={seed} spellCheck={false} aria-label="Seed" className="design-pc-seed-input" />
      <button type="button" inert aria-label="Copy seed" className="design-pc-seed-copy">
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
          <rect x="8.5" y="8.5" width="11" height="11" rx="2.5" />
          <rect x="4.5" y="4.5" width="11" height="11" rx="2.5" />
        </svg>
      </button>
    </div>
  );
}

function ActionRow() {
  return (
    <div className="design-pc-actions">
      <Button inert>Revert</Button>
      <Button inert variant="primary">
        Apply
      </Button>
      <Button inert>Roll</Button>
    </div>
  );
}

function ConfiguratorPanel({
  layout,
  title,
  blockLabel,
  palette,
  params,
}: {
  layout: Layout['key'];
  title: string;
  blockLabel: string;
  palette: Palette;
  params: PaletteParams;
}) {
  return (
    <div className={`design-pc-panel design-pc-panel--${layout}`}>
      <header className="design-pc-head">
        <span className="design-pc-head-title">{title}</span>
        <span className="design-pc-head-block">{blockLabel}</span>
      </header>
      <div className="design-pc-area-board">
        <SwatchBoard palette={palette} />
      </div>
      <div className="design-pc-area-contrast">
        <ContrastReadout palette={palette} />
      </div>
      <div className="design-pc-area-dial">
        <HueDial palette={palette} />
      </div>
      <div className="design-pc-area-sliders">
        {SLIDER_KEYS.map((key) => (
          <ParameterSlider key={key} name={key} spec={PARAMETERS[key]} value={params[key]} />
        ))}
      </div>
      <div className="design-pc-area-seed">
        <SeedField seed={SEED} />
      </div>
      <div className="design-pc-area-actions">
        <ActionRow />
      </div>
    </div>
  );
}

export function PaletteConfiguratorPage() {
  const { params, light, dark } = sketchPalettes();

  return (
    <GalleryPage
      title="Palette configurator"
      blurb="Three layouts of the same configurator, drawn at one fixed seed - sketches for the operator to pick between, not the build. Every part is the same; only the arrangement changes."
    >
      {LAYOUTS.map((layout) => (
        <GallerySection key={layout.key} title={layout.title} note={layout.note}>
          <div className="design-pc-pair">
            <ConfiguratorPanel
              layout={layout.key}
              title={layout.title}
              blockLabel="Light block"
              palette={light}
              params={params}
            />
            <ConfiguratorPanel
              layout={layout.key}
              title={layout.title}
              blockLabel="Dark block"
              palette={dark}
              params={params}
            />
          </div>
        </GallerySection>
      ))}

      <GallerySection
        title="What this is not"
        note="No dial, slider or button here is live - Revert, Apply and Roll do nothing, and the dial and sliders are drawings of their resting state at the fixed seed above. Only the seed's own text stays selectable, so it can still be copied by hand."
      >
        <Text tone="muted">Nothing to see here - this section is the note.</Text>
      </GallerySection>
    </GalleryPage>
  );
}
