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
