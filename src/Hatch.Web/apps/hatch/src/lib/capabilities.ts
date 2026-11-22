import type { BadgeTone } from '@hatch/ui';

/** The four words a capability's or a verdict's state is told apart by - the
    one place they live, so a label never drifts from the state it names. */
export function capabilityStateLabel(state: string): string {
  switch (state) {
    case 'holds':
      return 'Holds';
    case 'does_not_hold':
      return 'Does not hold';
    case 'cannot_tell':
      return 'Cannot tell';
    case 'not_checked':
      return 'Not checked';
    default:
      return state;
  }
}

/** The tone a state's badge wears - the one place the colour is chosen, so
    Capabilities, Serves and each verdict in Passes cannot drift apart. */
export function capabilityStateTone(state: string): BadgeTone {
  switch (state) {
    case 'holds':
      return 'success';
    case 'does_not_hold':
      return 'danger';
    case 'cannot_tell':
      return 'warn';
    default:
      return 'muted';
  }
}
