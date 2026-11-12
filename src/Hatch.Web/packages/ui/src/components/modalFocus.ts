/* The Modal's focus rule, with no React and no DOM in it so a node test can
   drive it with stub objects.

   When a dialog opens, its panel takes focus - unless something inside it
   already has it. React applies a child's `autoFocus` at commit, before the
   Modal's effect runs, so by then `active` is that child: the panel stepping in
   would take focus off a field that asked for it. */

export function panelTakesFocus(panel: { contains(n: unknown): boolean }, active: unknown): boolean {
  if (active === null || active === undefined) return true;
  if (active === panel) return true;
  return !panel.contains(active);
}
