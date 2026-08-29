# PetSitters.UiTests — end-to-end UI regression suite

Automated **UI regression tests** for the Sitters4Us WPF app, using
[FlaUI](https://github.com/FlaUI/FlaUI) (Windows UI Automation) + MSTest.

This is the outermost layer of the test pyramid and is separate from
`PetSitters.Tests` (the fast logic + persistence tests). Instead of calling
functions directly, these launch the *real* `PetSitters.exe` and drive it the way
a person would — clicking buttons, typing into boxes, switching tabs, opening
dialogs and reading labels back.

Its regression job is to catch breakages unit tests cannot see: view wiring,
navigation, role routing and cross-role workflows. Notably **FR-S3** (sitter views
full job details before deciding) is UI-only presentation, so this project is its
*only* automated coverage.

## The tests

| Test | What it locks down |
|------|--------------------|
| `BookingJourney_OwnerBooksSitterAndSitterAccepts_CompletesWithChatOpen` | The full two-role workflow (below) |
| `Login_WithUnknownCredentials_ShowsGenericErrorAndStaysOnLogin` | Failed login shows the generic, non-enumerating message and stays put |

The journey covers: register **Sitter** → personal details → sitting profile →
register **Owner** → personal details → add pet → browse sitters → **book** →
owner sees *Pending* → sitter reviews **full job details** in the popup →
**accepts** → **chat opens and a message sends** → request leaves the pending list
→ appears under active chats → owner sees *Accepted*.

Requirements exercised: FR-A1, FR-A2, FR-O1–FR-O4, FR-S1–FR-S5.
Not covered: **FR-O5** (owner-side chat) is not implemented in the app yet.

## Clean database every run — core to the suite

Before launching the app, **every test** deletes the live database at
`%AppData%\PetSitters\petsitters.db` (`AppLocator.WipeDatabase`). The app then
recreates an empty schema on startup.

This is not incidental: without it, data left behind by an earlier run (duplicate
emails, stale bookings) silently changes what the UI shows and the results stop
meaning anything. The wipe therefore **verifies** the delete and fails loudly —
usually pointing at a `PetSitters.exe` from a previous run still holding the file
open. Because it runs per-test, the tests are independent and order-agnostic.

> ⚠️ It really does wipe your local Sitters4Us data. That's intentional — just
> don't run it against a database you care about.

## How to run

UI automation drives a real window, so this needs a normal interactive Windows
desktop (not headless/locked) — and **don't use the mouse or keyboard while it
runs**, as stealing focus will derail it.

1. Open `PetSitters.sln` in Visual Studio 2022.
2. **Build the solution** (the tests need `PetSitters.exe` built — see below).
3. Test Explorer → run the `Regression` category.

From the command line, build the app with VS MSBuild first, then:

```bash
dotnet test PetSitters.UiTests\PetSitters.UiTests.csproj
```

### Build order (important)

Like `PetSitters.Tests`, this project has **no `<ProjectReference>`** to
`PetSitters.csproj`, because the .NET SDK cannot build that classic (non-SDK) WPF
project — adding one makes `dotnet build`/`dotnet test` fail outright. The app is
launched as a process, so no assembly reference is needed at all. Build the app
first:

```bash
msbuild PetSitters.csproj /t:Build /p:Configuration=Debug
```

### Run speed

Each action pauses briefly so the run is easy to follow. Override it with
`PETSITTERS_UI_DELAY_MS`:

```bash
set PETSITTERS_UI_DELAY_MS=0
```

`0` = fast regression run, `700` = default watch-along pace, higher = demo speed.
The delay is cosmetic only — correctness never depends on it (waits and retries
are explicit).

### Pointing at a specific executable

By default the test finds `PetSitters.exe` under the solution's `bin\Debug` (then
`bin\Release`). Override with `PETSITTERS_EXE=<full path>`.

## Failure diagnostics

- Each step is logged to the test output (`STEP: ...`), so a failure shows exactly
  how far the journey got.
- On failure a **screenshot** is saved next to the test results and its path
  written to the output — which makes accidental desktop interference obvious
  rather than looking like a real defect.

## How controls are found

Every control the tests touch has an `x:Name` in the XAML, which WPF exposes to UI
Automation as the **AutomationId** (`EmailBox`, `RateBox`, `ChatInput`, ...).
Buttons and tabs have no `x:Name`, so they're matched by visible text
(`"Create account"`, `"My Chats"`, ...). Rename either in the app and the matching
string here must change too.

Passwords are a special case: a WPF `PasswordBox` deliberately exposes no value
pattern, so the driver clicks it and types real keystrokes, confirming it holds
keyboard focus first.
