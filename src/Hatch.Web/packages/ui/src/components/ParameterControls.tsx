import { PARAMETERS, snapToSeed, type Palette, type ParameterSpec, type PaletteParams } from '../theme/palette';
import { HueDial } from './HueDial';
import { ParameterSlider } from './ParameterSlider';
import './ParameterControls.css';

export interface ParameterControlsProps {
  value: PaletteParams;
  /** The whole next vector, with the changed parameter snapped to the seed's grid. */
  onChange: (next: PaletteParams) => void;
  /** The derived palette of the block on screen, for the dial's rim. */
  palette: Palette;
  /** The table to draw, one control per entry in its order; the engine's `PARAMETERS` unless a caller supplies
      another. The entry named `hue` is the dial and every other entry a slider. */
  parameters?: Readonly<Record<string, ParameterSpec>>;
}

/** A controlled group: it keeps no state, takes a vector and reports the next one. Every value it reports is
    snapped to what the seed holds, so what is on screen is always a vector the seed can write and read back. */
export function ParameterControls({ value, onChange, palette, parameters = PARAMETERS }: ParameterControlsProps) {
  const vector = value as unknown as Readonly<Record<string, number>>;
  return (
    <div className="hatch-parameter-controls" role="group" aria-label="Palette parameters">
      {Object.entries(parameters).map(([key, spec]) => {
        const label = spec.label ?? key;
        const current = vector[key] ?? spec.min;
        const set = (raw: number) => {
          const next = snapToSeed(spec, raw);
          if (next !== vector[key]) onChange({ ...value, [key]: next });
        };
        return key === 'hue' ? (
          <HueDial key={key} label={label} spec={spec} value={current} onChange={set} palette={palette} />
        ) : (
          <ParameterSlider key={key} label={label} spec={spec} value={current} onChange={set} />
        );
      })}
    </div>
  );
}
