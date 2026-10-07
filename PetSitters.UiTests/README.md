# PetSitters.UiTests — end-to-end UI regression suite

Automated **UI regression tests** for the Sitters4Us WPF app, using
[FlaUI](https://github.com/FlaUI/FlaUI) (Windows UI Automation) + MSTest.

This is the outermost layer of the test pyramid and is separate from
`PetSitters.Tests` (the fast logic + persistence tests). Instead of calling
functions directly, these launch the *real* `PetSitters.exe` and drive it the way
a person would — clicking buttons, typing into boxes, switching tabs, opening
dialogs and reading labels back.

Its regression job is to catch breakages unit tests cannot see: view wiring,
navigation, role routing, cross-role workflows and what the header tells the user
about which database the app opened on. Notably **FR-S3** (sitter views full job
details before deciding) is UI-only presentation, so this project is its *only*
automated coverage.

## The tests

| Test | What it locks down |
|------|--------------------|
| `BookingJourney_OwnerBooksSitterAndSitterAccepts_CompletesWithChatOpen` | The full two-role workflow (below) |
| `OverlappingRequests_SitterAcceptsOne_SecondIsRefusedAndStaysPending` | REQ-GR-08: after accepting one of two same-date requests, accepting the other is refused with a "stay pending" message and it remains in the list |
| `SamePetDoubleBooking_IsRefused_UntilOwnerCancelsTheFirst` | REQ-PO-08 + REQ-PO-07: booking the same pet twice is refused; cancelling the pending booking (with confirmation) frees the pet to rebook, and the cancelled one leaves the sitter's queue |
| `SharedEmail_OwnerAlsoRegistersAsSitter_LoginAsksWhichRole` | REQ-GR-06: an owner re-registers as a sitter with the same email; a second sitter sign-up is refused with a warning; login with the shared password asks Owner/Sitter and opens the chosen dashboard |
| `BookingForm_InvalidRequests_AreRejectedWithSpecificMessages` | REQ-GR-04: no pet, start in the past and a 15-day booking are each rejected with their own message; exactly 14 days is accepted (dates are set through UI Automation with `SetDate`) |
| `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo` | REQ-GR-09: with a cloud `DATABASE_URL` that refuses connections, the app still opens on the local database, the header says *⚠ Offline: local database*, and registering an account still works |
| `Startup_LocalDatabaseChosen_HeaderSaysLocalDatabase` | REQ-GR-09: when the local database is chosen deliberately (`PETSITTERS_DB=sqlite`), the header says *Local database* plainly instead of claiming to be offline |
| `Login_WithUnknownCredentials_ShowsGenericErrorAndStaysOnLogin` | Failed login shows the generic, non-enumerating message and stays put |

The journey covers: register **Sitter** → personal details → sitting profile →
register **Owner** → personal details → add pet → browse sitters → **book** →
owner sees *Pending* → sitter reviews **full job details** in the popup →
**accepts** → **chat opens and a message sends** → request leaves the pending list
→ appears under active chats → owner sees *Accepted* → owner **cancels** the
accepted booking → it shows *Cancelled* and leaves the owner's chats (REQ-PO-07).

Requirements exercised: FR-A1, FR-A2, FR-O1–FR-O4, FR-S1–FR-S5, plus the REQ-GR /
REQ-PO IDs in the table. **REQ-GR-09** (shared cloud database with local fallback
at launch) is a *proposed* ID, still to be added to the report.

Partly covered: **FR-O5** (owner-side chat) works in the app, but the suite only
sends messages from the sitter side; the owner's *Chats* tab is checked only to
confirm a cancelled booking leaves it.

Not covered here, by design: a *successful* cloud (MySQL) connection. The suite is
kept off the shared cloud database (see below); the data layer is checked against
a real MySQL server by the opt-in parity tests in `PetSitters.Tests`
(`TestCategory=MySql`).

Every test is labelled `System` + `Regression`, plus `Acceptance` / `Security` /
`Usability` and scenario labels (`Positive`, `Negative`, `Boundary`,
`InvalidInput`, `ErrorHandling`) where they apply. See "Test classification" in
`docs/UnitTests.md`.

## Clean database every run — core to the suite

Before launching the app, **every test** deletes the app's **local** SQLite
database at `%AppData%\PetSitters\petsitters.db` (`AppLocator.WipeDatabase`). The
app then recreates an empty schema on startup.

This is not incidental: without it, data left behind by an earlier run (duplicate
emails, stale bookings) silently changes what the UI shows and the results stop
meaning anything. The wipe therefore **verifies** the delete and fails loudly —
usually pointing at a `PetSitters.exe` from a previous run still holding the file
open. Because it runs per-test, the tests are independent and order-agnostic.

> ⚠️ It really does wipe your local Sitters4Us data (the file the app uses when it
> runs locally or falls back from the cloud). That's intentional — just don't run
> it against a local database you care about. Cloud data is never affected.

### The suite never touches the cloud database

The app is MySQL-first: when `DATABASE_URL` is configured (normally through the
`.env` that the build copies into `bin\Debug`), it opens on the shared cloud
database instead of the local file. That database can't be wiped between tests and
must never collect test accounts, so `PetSittersDriver` launches **every** app with
`PETSITTERS_DB=sqlite`. Real environment variables override `.env`, so the app
always opens on the local file the wipe just reset — even when `bin\Debug\.env`
exists — and the header reads *Local database*.

The one exception is `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo`,
which relaunches with `PETSITTERS_DB=auto` and a `DATABASE_URL` aimed at a refused
port on this machine (`127.0.0.1:1`, connect timeout 3 s). The connection attempt
fails, the app falls back to the same wiped local file, and the real cloud server
is never contacted.

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

No `.env` or database settings are needed: the app is forced onto the local
database (see above). The fallback test simulates an unreachable cloud with a
refused local port, so it needs no network either. It is slower than the others
because the app waits for the failed connection before showing the login screen
(about 7 s to open in testing).

> **Known issue — default window size.** Since the login/register redesign the
> 920×640 window clips the password box off screen, so tests can fail with FlaUI's
> `NoClickablePointException`. All 8 pass with the app window maximised. The fix
> belongs in the app's layout, not the driver — real users hit the same clipping.

### Build order (important)

Like `PetSitters.Tests`, this project has **no `<ProjectReference>`** to
`PetSitters.csproj`, because the .NET SDK cannot build that classic (non-SDK) WPF
project — adding one makes `dotnet build`/`dotnet test` fail outright. The app is
launched as a process, so no assembly reference is needed at all. Build the app
first:

```bash
msbuild PetSitters.csproj /t:Build /p:Configuration=Debug
```

A command-line build also runs the logic tests in `PetSitters.Tests`; add
`/p:SkipTests=true` to build only (see `docs/CI.md`). If a `.env` exists in the
repo root the build copies it next to the exe — harmless here, because the driver
forces the local database.

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

### Environment variables at a glance

| Variable | Who sets it | Effect |
|----------|-------------|--------|
| `PETSITTERS_DB` | **The driver**, on every launch (`sqlite`) | Forces the app onto the local database the wipe resets. It replaces any value in your own environment; don't change that for the normal tests. Only the fallback test overrides it (to `auto`), for its own launch. |
| `DATABASE_URL`, `DB_CONNECT_TIMEOUT_SECONDS` | The fallback test only, for its own launch | Point the app at a refused local port with a 3 s timeout, so the fallback path runs without contacting the cloud. |
| `PETSITTERS_EXE` | You (optional) | Full path to the `PetSitters.exe` to test. |
| `PETSITTERS_UI_DELAY_MS` | You (optional) | Pause after each action: `0` fast, `700` default, higher for demos. |

## Failure diagnostics

- Each step is logged to the test output (`STEP: ...`), so a failure shows exactly
  how far the journey got.
- On failure a **screenshot** is saved next to the test results and its path
  written to the output — which makes accidental desktop interference obvious
  rather than looking like a real defect.

## How controls are found

Every control the tests touch has an `x:Name` in the XAML, which WPF exposes to UI
Automation as the **AutomationId** (`EmailBox`, `RateBox`, `ChatInput`, the
header's `StorageStatus`, ...).
Buttons and tabs have no `x:Name`, so they're matched by visible text
(`"Create account"`, `"My Chats"`, ...). Rename either in the app and the matching
string here must change too.

Passwords are a special case: a WPF `PasswordBox` deliberately exposes no value
pattern, so the driver clicks it and types real keystrokes, confirming it holds
keyboard focus first.
