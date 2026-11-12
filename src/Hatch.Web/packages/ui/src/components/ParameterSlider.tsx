import { useId } from 'react';
import { windowBands, type ParameterSpec } from '../theme/palette';
import { ParameterReadout } from './ParameterReadout';
import { percentOf, stepOf } from './readoutMath';
import './ParameterSlider.css';

export interface ParameterSliderProps {
  /** The name the slider is labelled and announced by. */
  label: string;
  /** The table entry: its full range is the track, its roll window the lit band on it. */
  spec: ParameterSpec;
  value: number;
  /** The next value, unsnapped; the group snaps it to the seed's grid before it reaches a vector. */
  onChange: (next: number) => void;
}

export function ParameterSlider({ label, spec, value, onChange }: ParameterSliderProps) {
  const id = useId();
  return (
    <div className="hatch-parameter-slider">
      <div className="hatch-parameter-slider__head">
        <label htmlFor={id} className="hatch-parameter-slider__label">
          {label}
        </label>
        <ParameterReadout spec={spec} value={value} />
      </div>
      <div className="hatch-parameter-slider__track">
        <input
          id={id}
          type="range"
          className="hatch-parameter-slider__input"
          min={spec.min}
          max={spec.max}
          step={stepOf(spec)}
          value={value}
          onChange={(event) => onChange(Number(event.currentTarget.value))}
        />
        <span className="hatch-parameter-slider__bands" aria-hidden="true">
          {windowBands(spec).map(([from, to], at) => (
            <span
              key={at}
              className="hatch-parameter-slider__band"
              style={{ left: `${percentOf(spec, from)}%`, width: `${percentOf(spec, to) - percentOf(spec, from)}%` }}
            />
          ))}
        </span>
      </div>
    </div>
  );
}
