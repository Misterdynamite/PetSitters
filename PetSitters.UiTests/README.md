# PetSitters.UiTests

Automated **end-to-end UI tests** for the PetSitters WPF app, using
[FlaUI](https://github.com/FlaUI/FlaUI) (Windows UI Automation) + MSTest.

This is separate from the assignment's logic/unit-test project. Instead of calling
functions directly, these tests launch the *real* `PetSitters.exe` and drive it the
way a person would — clicking buttons, typing into boxes, switching tabs and reading
labels back. It's the slow, "does the whole app actually hang together" layer of the
testing pyramid.

## What the test does

`OwnerSitterJourneyTests.OwnerBooksSitter_AndSitterAcceptsTheRequest` runs one
continuous journey:

1. Register a **Sitter** account and save personal details (FR-1, FR-7)
2. Fill in the **sitting profile** — availability, experience, daily rate (FR-8)
3. Register an **Owner** account and save personal details (FR-1, FR-3)
4. Add a **pet** (FR-4)
5. Browse sitters and **book** the sitter for that pet (FR-5, FR-6)
6. Log in as the sitter and **accept** the request (sitter side of FR-6)
7. Log back in as the owner and confirm the booking now reads **Accepted**

## Fresh database every run

Before launching the app, the test **deletes** the live database at
`%AppData%\PetSitters\petsitters.db` (see `AppLocator.WipeDatabase`). The app then
recreates an empty schema on startup, so every run starts from a clean, known state
and the only data in play is what the test creates.

> ⚠️ It really does wipe your local PetSitters data. That's intentional for a clean
> end-to-end run — just don't run it against a database you care about.

## How to run

UI automation drives a real window, so this needs a normal interactive Windows
desktop (not a headless/locked session) and Visual Studio's full build tooling:

1. Open `PetSitters.sln` in Visual Studio 2022.
2. **Build the solution** (the tests depend on `PetSitters.exe` being built).
3. Open **Test Explorer** (`Test > Test Explorer`) and run
   `OwnerBooksSitter_AndSitterAcceptsTheRequest`.
4. Watch it drive the app. Don't touch the mouse/keyboard while it runs — it's
   controlling the same desktop you are.

Command line (from a *Developer* command prompt / PowerShell that has the full
.NET Framework build tooling, e.g. `dotnet test` won't build the classic-style app
on its own):

```bash
dotnet test PetSitters.UiTests\PetSitters.UiTests.csproj
```

### Pointing at a specific executable

By default the test finds `PetSitters.exe` under the solution's `bin\Debug`
(then `bin\Release`). To override, set an environment variable:

```bash
set PETSITTERS_EXE=P:\Github\PetSitters\bin\Debug\PetSitters.exe
```

## How controls are found

Every control the test touches has an `x:Name` in the XAML, which WPF exposes to UI
Automation as the **AutomationId** — so selectors match those names directly
(`EmailBox`, `PasswordBox`, `RateBox`, ...). Buttons and tabs have no `x:Name`, so
they're found by their visible text (`"Create account"`, `"My Pets"`, ...).

If you rename a control (or its button text) in the app, update the matching string
in `OwnerSitterJourneyTests` / `PetSittersDriver`.
