<div align="center">

# Codex Provider Manager

**Switch, add and manage custom model providers for Codex — on Windows, macOS and Linux.**

[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](#build)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![UI](https://img.shields.io/badge/UI-Avalonia-9B4FCA)](https://avaloniaui.net)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

</div>

One desktop app (with an integrated CLI) that rewrites `~/.codex/config.toml`
for you: point Codex at **LM Studio, Ollama, OpenRouter, or any
OpenAI-compatible endpoint** — then switch back to your built-in OpenAI
account with one click. No manual TOML editing, no stale API keys.

![screenshot](docs/screenshot-avalonia.png)

## Why

Codex supports custom providers, but managing them by hand is painful:

- you edit `config.toml` manually, and one typo breaks every chat;
- provider credentials end up in the config file in plaintext;
- if you change an env var, **already-running processes keep the stale key**
  until you kill everything;
- switching back to the plain OpenAI account means remembering exactly which
  lines to delete.

Codex Provider Manager solves all four — and works the same way on all three
OSes.

## Features

| | |
|---|---|
| **Providers list** | Every `[model_providers.*]` section in your config, with the active one highlighted; one click to switch |
| **Model catalog** | Per-provider model list with search filter (live catalog through bridge providers, static map as fallback) |
| **Add / Edit / Remove** | Forms write *managed blocks* into the config — idempotent, with a rolling backup before every write |
| **API keys, safely** | Paste a key in the form → it is stored as a **user environment variable** (never in the config); the generated `[auth]` block reads it **at request time** |
| **Restore OpenAI** | Removes all overrides *and* restarts the app — back to the built-in account in one click |
| **Restart integration** | Optional auto-restart of the Codex desktop app after every switch (persisted preference) |
| **CLI included** | The same binary is a full CLI: `list`, `set`, `models`, `add`, `remove`, `restart` |
| **Cross-platform** | Native Avalonia UI; single-file self-contained builds, no .NET required on the target machine |

## The stale-key problem, and how it is fixed

The usual way to configure a provider key is an `env_key` entry: Codex reads
the variable **when the process starts**. Change the key (or add it after
launch) and every Codex chat keeps failing with `Missing environment variable`
until you fully kill and restart everything.

This app instead generates a command-based `[auth]` block that resolves the
key **on every request**, from the platform-native secret store:

```toml
# BEGIN codex-provider-manager: freellm
[model_providers.freellm]
name = "Free LLM"
base_url = "http://localhost:3001/v1"
wire_api = "responses"

[model_providers.freellm.auth]
command = "powershell"
args = ["-NoProfile", "-Command", "(Get-ItemProperty HKCU:\\Environment).'FREELLM_API_KEY'"]
# END codex-provider-manager: freellm
```

| OS | Where the key lives | How it is read |
|---|---|---|
| Windows | `HKCU\Environment` (registry) | `powershell` → `Get-ItemProperty` |
| macOS / Linux | `~/.codex/provider-keys.env` (mode 600) | `sh -c` source of the file |

Keys are set through the GUI (paste field) or by hand; values are never
written to `config.toml`, logs, or this repository.

## Getting started

1. **Download** a build from
   [Releases](../../releases) (Windows / macOS / Linux,
   self-contained single-file), or run from source with `dotnet run`.
2. Start the app — it reads your existing `~/.codex/config.toml` as-is.
3. **Add** a provider (or pick an existing one), optionally paste its API key.
4. **Activate provider** → the app rewrites the config and (optionally)
   restarts the Codex desktop app.
5. Open a **new chat** in Codex — a thread stays anchored to the model it was
   born on.

Example providers people typically add:

| Provider | Base URL | Wire API | Notes |
|---|---|---|---|
| LM Studio | `http://localhost:1234/v1` | `responses`* | via [colibri-bridge](https://github.com/su8z3r0/colibri-bridge) |
| Ollama (incl. cloud models) | `http://localhost:8013/v1` | `responses`* | via bridge; `-cloud` models run on Ollama's servers |
| OpenRouter | `https://openrouter.ai/api/v1` | `responses` | env var `OPENROUTER_API_KEY` |
| colibri (local MoE) | `http://localhost:8012/v1` | `responses`* | via bridge |
| Any OpenAI-compatible | `https://host/v1` | `chat` / `responses` | — |

<sub>\* Codex requires the Responses API; providers that only speak chat
completions are fronted by a tiny local bridge that translates
`/v1/responses` → `/v1/chat/completions` and hydrates the model picker.</sub>

## CLI

```
CodexProvider list                  # providers + active one
CodexProvider set <id> [model]      # switch provider/model
CodexProvider models <id> [filter]  # model catalog
CodexProvider add <id> <url> [envKey] [model]
CodexProvider remove <id>
CodexProvider restart               # restart the Codex desktop app
```

Run without arguments for the GUI.

## Build

Requires the .NET 8 SDK.

```bash
dotnet build CodexProvider.sln -c Release

# single-file, self-contained (pick your RID):
dotnet publish CodexProvider.UI -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
# also: osx-x64, osx-arm64, linux-x64
```

CI (`.github/workflows/build.yml`) publishes artifacts for all three OSes on
every push and attaches them to a GitHub Release on `v*` tags.

## Project layout

```
CodexProvider.sln
├── CodexProvider.Core/          # portable logic, zero dependencies
│   ├── CodexConfig.cs           # TOML parse/switch/add/update/remove, settings
│   ├── WindowsHooks.cs          # HKCU registry keys, desktop app restart
│   └── UnixHooks.cs             # ~/.codex/provider-keys.env, osascript (macOS)
└── CodexProvider.UI/            # Avalonia UI + integrated CLI
    ├── MainWindow.axaml         # dark theme, providers, models, log
    ├── AddProviderForm.*        # add/edit dialog with API-key paste
    └── Program.cs               # GUI when no args, CLI otherwise
```

Per-OS behavior is isolated behind `IPlatformHooks` — the Core project is pure
portable C#.

## Limitations (by design)

- Switching provider/model requires **restarting the Codex app + a new
  chat** — Codex pins a thread to its birth model. The *Restore OpenAI* and
  *Restart now* buttons exist exactly for this.
- The model catalog for bridge-less providers comes from a local map
  (`~/.codex/codex-provider.models.json`), editable but not auto-discovered.
- Desktop-app restart is supported on Windows and macOS; on Linux you get a
  clear "restart manually" notice.

## Acknowledgements

The managed-block TOML pattern and the Responses→Chat polyfill design were
inspired by
[applyinnovations/bifrost-model-router](https://github.com/applyinnovations/bifrost-model-router)
(Apache 2.0) — go read it if you want a full-blown Dockerized router.

## License

[MIT](LICENSE)