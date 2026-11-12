import { useId, type KeyboardEvent, type PointerEvent } from 'react';
import { hexToOklch, type Palette, type ParameterSpec } from '../theme/palette';
import { angleFromPoint, keyValue, rimPoint } from './dialMath';
import { formatValue } from './readoutMath';
import { ParameterReadout } from './ParameterReadout';
import './HueDial.css';

export interface HueDialProps {
  /** The name the dial is announced and labelled by. */
  label: string;
  /** The `hue` entry of the parameter table: its range is the dial's, its unit the readout's. */
  spec: ParameterSpec;
  value: number;
  /** The next angle, unsnapped; the group snaps it to the seed's grid before it reaches a vector. */
  onChange: (next: number) => void;
  /** The derived palette of the block on screen. The rim reads where each accent landed from this and not from
      `value`, because that depends on `spread` and the semantic-hue guard as well as on the hue. */
  palette: Palette;
}

/** The accents drawn around the rim, in the order the engine lists them. An explicit list rather than the
    engine's own `ACCENTS`, which also holds the two chrome gradient ends. */
export const RIM_TOKENS = [
  '--primary',
  '--danger',
  '--success',
  '--warn',
  '--chrome',
  '--flow-turn',
  '--flow-family',
  '--flow-person',
  '--flow-other',
] as const;

/** One accent's dot: its own hue, read off the derived hex so the dot can never disagree with the swatch. */
export function rimDots(palette: Palette): { token: string; hue: number; colour: string }[] {
  return RIM_TOKENS.flatMap((token) => {
    const colour = palette[token];
    return colour ? [{ token, hue: hexToOklch(colour).h, colour }] : [];
  });
}

const FACE = 50;
const RIM = 42;
const NEEDLE = 34;

export function HueDial({ label, spec, value, onChange, palette }: HueDialProps) {
  const labelId = useId();
  const { min, max } = spec;
  const needle = rimPoint(value, NEEDLE);

  function angleAt(event: PointerEvent<SVGSVGElement>) {
    const box = event.currentTarget.getBoundingClientRect();
    return angleFromPoint(event.clientX - (box.left + box.width / 2), event.clientY - (box.top + box.height / 2));
  }

  function onPointerDown(event: PointerEvent<SVGSVGElement>) {
    event.currentTarget.setPointerCapture(event.pointerId);
    event.currentTarget.focus();
    onChange(angleAt(event));
  }

  function onPointerMove(event: PointerEvent<SVGSVGElement>) {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) onChange(angleAt(event));
  }

  function onKeyDown(event: KeyboardEvent<SVGSVGElement>) {
    const next = keyValue(event.key, event.shiftKey, value, min, max);
    if (next === null) return;
    event.preventDefault();
    onChange(next);
  }

  return (
    <div className="hatch-hue-dial">
      <span id={labelId} className="hatch-hue-dial__label">
        {label}
      </span>
      <svg
        className="hatch-hue-dial__face"
        viewBox="0 0 100 100"
        role="slider"
        tabIndex={0}
        aria-labelledby={labelId}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-valuenow={Number(formatValue(spec, value))}
        aria-valuetext={`${formatValue(spec, value)} degrees`}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onKeyDown={onKeyDown}
      >
        <circle className="hatch-hue-dial__track" cx={FACE} cy={FACE} r={RIM} />
        <line className="hatch-hue-dial__needle" x1={FACE} y1={FACE} x2={needle.x} y2={needle.y} />
        <circle className="hatch-hue-dial__hub" cx={FACE} cy={FACE} r={2.5} />
        {rimDots(palette).map(({ token, hue, colour }) => {
          const { x, y } = rimPoint(hue, RIM);
          return <circle key={token} className="hatch-hue-dial__dot" data-token={token} cx={x} cy={y} r={4.5} fill={colour} />;
        })}
      </svg>
      <ParameterReadout spec={spec} value={value} />
    </div>
  );
}
