import type { ParameterSpec } from '../theme/palette';
import { formatReadout, inWindow, OUTSIDE_ROLL } from './readoutMath';
import './ParameterReadout.css';

/** A control's live value: text with its unit, announced politely as it changes. Inside the roll window it
    glows; outside it dims and says so in words, so the state never rests on colour alone. */
export function ParameterReadout({ spec, value }: { spec: ParameterSpec; value: number }) {
  const outside = !inWindow(spec, value);
  return (
    <output className={`hatch-parameter-readout${outside ? ' is-outside' : ''}`} aria-live="polite">
      {formatReadout(spec, value)}
      {outside ? ` ${OUTSIDE_ROLL}` : ''}
    </output>
  );
}
