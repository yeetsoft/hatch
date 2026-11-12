/* What every parameter control shows and steps by, read from the entry's own range and window.
   Pure, so the same numbers are unit-tested without a DOM. */
import { windowBands, type ParameterSpec } from '../theme/palette/params';

/** The seed holds 256 steps per parameter, so the finest a control can move is 1/255 of the range. */
export const SEED_STEPS = 255;

/** The native range input's `step`: one seed step. */
export const stepOf = ({ min, max }: ParameterSpec) => (max - min) / SEED_STEPS;

/** Enough decimals that two neighbouring steps never read as the same number: 0 for hue, 3 for spread, 4 for tint. */
export const decimalsOf = (spec: ParameterSpec) => Math.max(0, Math.ceil(-Math.log10(stepOf(spec))));

/** The value as text, without its unit. */
export const formatValue = (spec: ParameterSpec, value: number) => value.toFixed(decimalsOf(spec));

/** The value with its unit: `1.102×`, `200°`, `0.0097`. */
export const formatReadout = (spec: ParameterSpec, value: number) => `${formatValue(spec, value)}${spec.unit ?? ''}`;

/** Whether the value lies inside the roll window (inclusive), i.e. where a roll could have put it. */
export const inWindow = (spec: ParameterSpec, value: number) =>
  windowBands(spec).some(([from, to]) => value >= from && value <= to);

/** Where a value sits along the track, as a percentage of the full range. */
export const percentOf = ({ min, max }: ParameterSpec, value: number) => ((value - min) / (max - min)) * 100;

/** The words a readout adds when the value is outside the roll window. */
export const OUTSIDE_ROLL = 'outside the roll';
