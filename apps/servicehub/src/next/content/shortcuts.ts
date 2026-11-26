/**
 * The keyboard shortcuts of the 5.0.0 shell (build-plan unit F6, the keyboard half) — and ONLY those that work (the 4.2.0 rule 6.6): a test
 * presses every key listed here and checks it does what the line says, so a line cannot outlive its shortcut. A single key never fires
 * inside a text field or with a modifier; Esc belongs to whatever is open (the overlay host closes it).
 * The 4.2.0 "A — switch Simple / Advanced" is gone because the switch is: there is one surface now (ADR draft 0019).
 */
export interface NextShortcut {
  readonly keys: string
  readonly does: string
}

export const nextShortcuts: readonly NextShortcut[] = [
  { keys: '⌘K or Ctrl-K', does: 'Search connections, queues and pages' },
  { keys: '?', does: 'Open Help' },
  { keys: 'R', does: 'Replay the open message (shows the preview first; only where the Replay link is offered)' },
  { keys: '/', does: 'Go to the search box of the list you are on' },
  { keys: 'Esc', does: 'Close a panel or dialog' },
]
