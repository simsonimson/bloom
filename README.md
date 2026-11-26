# bloom

**bloom** is an open-source Windows companion for Roblox: a bootstrapper with a
safe fallback to the official installation, push-to-talk for Roblox Voice Chat,
Discord Rich Presence, and a self-hostable Discord bot for Roblox verification
and group role sync.

> Status: **Phase 0 – technical spikes.** Nothing here is ready for end users yet.

bloom is not affiliated with Roblox Corporation or Discord Inc.

## Repository layout

| Path | Contents |
| --- | --- |
| `docs/architecture/` | Architecture Decision Records (ADR-001 … ADR-011) |
| `tools/spikes/` | Throw-away prototypes from Phase 0 |
| `tools/intro-render/` | Render pipeline for the ASCII intro (based on `bloom3d.py`, MIT) |
| `src/` | Desktop app and server (from Phase 1) |

## Phase 0 spikes

| Spike | Question | Where |
| --- | --- | --- |
| S1 | Does muting only Roblox' capture session silence Roblox Voice? | `tools/spikes/PttSpike` |
| S2 | Does Raw Input arrive while Roblox has focus? | `tools/spikes/PttSpike` |
| S3 | Does a 2× / 60 fps render of the intro keep the original look? | `tools/intro-render` |
| S4 | WinUI 3 unpackaged + Velopack + `bloom://` scheme | planned |
| S5 | Roblox OAuth with PKCE and custom scheme | planned |
| S6 | Open Cloud group membership lookup, public profile description | planned |
| S7 | Bootstrapper: version, manifest, packages, launch arguments | planned |

## License

MIT, see [LICENSE](LICENSE). Third-party notices: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
