# Architecture

## Shared data flow

```text
Launch Library 2 + trusted status sources ─┐
Weather provider ──────────────────────────┼─> Local data service/cache
Official coverage links ───────────────────┘            |
                                                        v
                                              MissionControl.Core
                                          status rules + countdown math
                                               /              \
                                              v                v
                                     Desktop dashboard   Windows widget
                                              |
                                      Optional local AI
                                  explains facts; never invents status
```

## Launch state is not a clock

A launch can be scheduled, delayed, scrubbed, in progress, completed, or unknown. A scheduled timestamp only drives T−. The core must switch to T+ only after an authoritative source reports confirmed liftoff / launch in progress. If sources disagree or confirmation is missing, show the known state and source freshness rather than guessing.

## Interfaces

- **Desktop dashboard:** richer live countdown, weather, status history, alerts, and official livestream access.
- **Windows Widgets board:** compact glanceable card. Windows controls widget refresh cadence, so second-by-second updates are not promised in this surface.
- **Shared local service:** one source of truth and cache for both surfaces.

## Local AI

The model is optional and runs only for user-facing explanations/summaries. It receives structured facts and must not change canonical status, schedule timestamps, source confidence, or countdown calculations. Core functionality must remain useful when the model is absent.

## Planned milestones

1. Deterministic launch state and countdown core.
2. Automated tests for schedule changes, scrubs, uncertain status, and confirmed liftoff.
3. LL2 and trusted-status adapters with cache/rate-limit handling.
4. Windows 11 desktop dashboard and shared local service.
5. Packaged Windows Widgets provider.
6. Optional small local model.
7. Verified one-command installer, updater, and uninstaller.
