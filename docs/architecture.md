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

A launch can be scheduled, delayed, scrubbed, in progress, completed, or unknown. A scheduled timestamp only drives T−. The core must switch to T+ only when the canonical state includes a confirmed liftoff timestamp from a trusted source. If sources disagree or confirmation is missing, show the known state and source freshness rather than guessing.

## Launch Library 2 schedule reader

`LaunchLibrary2Client` in `src/MissionControl.Core/LaunchLibrary2Client.cs` reads the [LL2 v2.3.0 upcoming-launch endpoint](https://ll.thespacedevs.com/2.3.0/launches/upcoming/). It requests a small upcoming list, hides recently-past records, caches a successful response for ten minutes by default, and offers a US-only helper based on an explicit launch-site country code. The public API documentation lists a 15-request-per-hour limit for unauthenticated clients, so the cache is intentional.

The adapter is not a source of authoritative liftoff telemetry. It preserves the provider's status label and schedule metadata, maps only known schedule states, and **never sets `LiftoffAtUtc`**. Even an LL2 “Launch in Flight” label remains unknown in the canonical state until a trusted status adapter confirms it. Errors and unexpected response shapes are surfaced instead of being disguised as fresh data.

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

## Milestones

1. **Present:** deterministic launch state/countdown core and Windows CI tests.
2. **Present:** local Ori Small MC Ollama client and repeatable model setup.
3. **Added; CI validation in progress:** cached LL2 schedule reader, explicit US launch-site filter, conservative state mapping, and tests.
4. **Next:** trusted NASA/SpaceX status adapter, official coverage resolver, weather provider, and durable shared cache.
5. Windows 11 desktop dashboard with general chat, launch/weather views, and a shared local service.
6. Packaged Windows Widgets provider.
7. Verified one-command installer, updater, and uninstaller, with optional Ollama/model installation.
