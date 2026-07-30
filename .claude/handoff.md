# Handoff

Last checkpoint: 2026-07-30.

## Completed

- [x] `README.md` — user guide for someone handed a build: prerequisites, the
      one-time three-folder setup, page tour, worked export, the four list files
      a run writes, how to read a sheet, the two Roost buttons, troubleshooting.
- [x] Tooltips on every interactive control across all five pages, plus inline
      captions for the rules that govern a whole page rather than one control.
- [x] Rewrote the user-facing strings that were written for maintainers: the
      three export mode descriptions, palette subtitle and built-in notice,
      tone-axis caption, pack-missing bars, Settings hint, catalogue count.
- [x] Canvas now states that drawing is not switched on, on the surface rather
      than in the tool panel (the panel is collapsed by default at 150% scaling).
- [x] `ui-tests.ps1` reads through `-w <hwnd>` instead of `-a <pid>`.

## Pending

- [ ] **Canvas is still a stub.** Pencil, fill and eraser have no command bound.
      The README and the page both say so; wiring them is the obvious next
      feature, and it is the only page with no working behaviour.
- [ ] **A new palette set needs an app restart** before it shows in the Pipeline
      tone list — `BatchExportViewModel` snapshots `RampService.Ramps` once in its
      constructor. Documented as a known quirk rather than fixed. Live-shaping it
      is a small change if it starts to annoy.
- [ ] [[open-repeat-run-native-crash]] is untouched and still open. The repro is
      two suite runs against **one** instance; launching a fresh app between runs
      does not exercise it.

## Learned

- The Pipeline picker's tenth slot expander is a canary for vertical space. Row 0
  of that page is `Auto` and the picker is the `*` row, so ~20 DIPs of extra
  header (a subtitle wrapping to two lines was enough) drops the `ItemsRepeater`
  to nine realized items, and the tenth then has no automation id at all.
- The `winapp ui` window-disambiguation advisory goes to **stderr**, and the
  read helpers merge it with `2>&1`. Since an open tooltip counts as a second
  window, adding tooltips made that advisory routine and it started landing
  inside captured values — where its HWND got parsed as a planned-file count.
- Three Pipeline failures in a suite run are usually pre-existing virtualization
  flake, not a regression. Baseline with `git stash push --include-untracked`
  before investigating; a clean tree fails the same way about half the time.

## Context

- Branch: `main` | Checkpoint: `1ede1f6` (pushed to `origin/main`)
- Verified at this checkpoint: build 0 warnings / 0 errors, 235/235 unit tests,
  UI suite 31/31 on two fresh instances, screenshots of all five pages reviewed
  in light and dark.
