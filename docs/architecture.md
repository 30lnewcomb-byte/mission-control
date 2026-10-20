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
                                      Ori Small MC (optional)
                                      general chat + explanations
                                              |
                                  Ollama local API / Qwen3.5 4B
```

## Launch state is not a clock

A launch can be scheduled, delayed, scrubbed, in progress, completed, or unknown. A scheduled timestamp only drives T−. The core must switch to T+ only after an authoritative source reports confirmed liftoff / launch in progress. If sources disagree or confirmation is missing, show the known state and source freshness rather than guessing.

## Interfaces

- **Desktop dashboard:** richer live countdown, weather, status history, alerts, official livestream access, and a full chat view for Ori Small MC.
- **Windows Widgets board:** compact glanceable card. Windows controls widget refresh cadence, so second-by-second updates are not promised in this surface. It can link to the dashboard for full chat.
- **Shared local service:** one source of truth and cache for both surfaces.

## Ori Small MC

- Base model: Qwen3.5 4B, installed locally by Ollama as `qwen3.5:4b`.
- Custom model name: `ori-small-mc`, created from `models/OriSmallMC.Modelfile`.
- API endpoint: `http://localhost:11434/api/chat`.
- Client: `OriSmallMcClient` in `src/MissionControl.Core/OriSmallMcClient.cs`.
- Setup: `scripts/Install-OriSmallMC.ps1`.

Ori Small MC is a **general-purpose chat assistant**, not a launch-only bot. Its system instructions explicitly allow ordinary conversation, explanations, learning, writing, brainstorming, planning, coding, and curiosity. Mission Control can supply trusted launch facts when answering launch questions.

The app must preserve the conversation history in its chat UI and send prior user/assistant turns to the local API for context. The model is optional: show a clear disconnected state if Ollama is unavailable, with a way to retry or open setup instructions. The eventual installer should offer the multi-gigabyte model download as an explicit optional component, show its size, and report progress/errors rather than silently downloading it.

## Trust boundaries

- The AI can explain supplied facts but must not alter canonical status, scheduled timestamps, source confidence, or countdown calculations.
- Do not claim the AI has internet, telemetry, weather, or external tools unless the app actually supplies those capabilities.
- Keep Ollama bound to the local machine by default. Do not expose its API to the LAN or internet as part of setup.
- The chat can be general-purpose; it should not force every answer into a space theme.

## Planned milestones

1. Deterministic launch state and countdown core.
2. Automated tests for schedule changes, scrubs, uncertain status, and confirmed liftoff.
3. LL2 and trusted-status adapters with cache/rate-limit handling.
4. Local Ori Small MC API client and repeatable model setup.
5. Windows 11 desktop dashboard with general chat, shared local service, and status/weather views.
6. Packaged Windows Widgets provider.
7. Verified one-command installer, updater, and uninstaller, with optional Ollama/model installation.
