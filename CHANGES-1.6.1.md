# MOC3 Recovery 1.6.1

- Replaces the marquee indicator with a determinate bar and adjacent percentage label.
- Progress resets to 0% on each conversion. A successful conversion retains 100%; selecting another model clears the indicator.
- Core stages use monotonic, bounded 0–100 reporting. PSD import reports completed layers; matching/relinking reports phases/candidates; ZIP preservation reports completed files.
- The percentage is a weighted stage estimate, not a time estimate. The backend exposes no internal percentage, so its stage holds while it runs rather than inventing timer progress.
- 100% is emitted only after the output CMO3, preservation ZIP and report have been installed successfully. Failures retain their last partial percentage.
- Late queued callbacks are ignored after completion/failure, so an older update cannot replace the final status.
- Existing string progress callbacks remain supported. Recovery behavior and output naming are unchanged.
- Automated integration checks cover monotonic range, initial/final values, existing output files at 100%, intermediate archive percentages, failure without 100%, and PSD-layer increments. Windows visual testing remains outstanding.
