# CLAUDE.md

Guidance for Claude Code (and humans) working in this repository.

## What this is

**Sitters4Us** — a pet owner ↔ pet sitter marketplace ("Airbnb for pet sitting"),
built as a **.NET Framework 4.7.2 WPF desktop app** with a local **SQLite**
database. It is a university **Software Quality Assurance (ENSE707)** prototype;
QA artefacts and tests matter as much as features.

> Naming: the product/brand is **Sitters4Us** (window title, headers). The Visual
> Studio **assembly and root namespace are still `PetSitters`** — do not rename
> namespaces; only user-facing text says "Sitters4Us".

## Solution layout (3 projects)

| Project | Kind | Framework | Purpose |
|---------|------|-----------|---------|
| `PetSitters` | WPF app, **classic (non-SDK) csproj** | net4.7.2 | The application |
| `PetSitters.Tests` | MSTest, **SDK-style** | net472 | Logic + integration tests (75 cases) |
| `PetSitters.UiTests` | MSTest + FlaUI, SDK-style | net472 | End-to-end UI automation |

## Build, test, run — IMPORTANT tooling notes

The main app is a **classic WPF csproj that the .NET SDK (`dotnet build`) cannot
compile**. Use **Visual Studio MSBuild** for the app; the SDK-style test
projects build with `dotnet` *after* the app is built.

MSBuild path on this machine:
`C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`

Build the app (also restores its NuGet packages):
```bash
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" PetSitters.csproj /t:Restore,Build /p:Configuration=Debug
```

Run the app: launch `bin\Debug\PetSitters.exe` (a windowed app).

Build + run the logic tests (build the app first — see below):
```bash
dotnet test PetSitters.Tests -c Debug
```

In Visual Studio: open `PetSitters.sln`, then **Test → Run All Tests** (builds
everything and runs both test projects).

### Why the tests reference the built EXE (not a ProjectReference)
`PetSitters.Tests` and `PetSitters.UiTests` reference the **compiled**
`..\bin\$(Configuration)\PetSitters.exe` as an assembly, NOT via `<ProjectReference>`,
because the SDK build can't compile the classic WPF project. So: **build the app
first**, then build/run the tests. Adding the `System.Data.SQLite.Core`
PackageReference to `PetSitters.Tests` is what copies the native
`SQLite.Interop.dll` (x86/x64) into the test output so integration tests can open
a real database. (VS BuildTools MSBuild alone cannot resolve `Microsoft.NET.Sdk`
— use `dotnet` for the SDK-style projects.)

### Classic csproj gotcha
`PetSitters.csproj` does **not** auto-include files. When you add a `.cs`/`.xaml`,
you must add a `<Compile>` / `<Page>` item to `PetSitters.csproj` by hand or it
won't compile.

## Architecture

UI-independent logic is deliberately separated from WPF so it can be unit-tested
without launching a window:

```
Models/     POCOs: User, SitterProfile, Pet, Booking, ChatMessage, enums
Data/       SQLite: Database (schema + connection factory) + one repository per table
Services/   PasswordHasher (PBKDF2), ValidationHelper, AuthService (+AuthResult),
            AppServices (composition root; holds repos + CurrentUser)
Views/      WPF UserControls, one per screen, swapped into MainWindow
```

- `App.xaml.cs` builds `AppServices.CreateDefault()` at startup and injects it
  into `MainWindow`, which swaps views (login → register → role dashboard).
- Roles: `UserRole.Owner` / `UserRole.Sitter`. `AppServices.CurrentUser` is the
  session.
- Services return **result objects** (`AuthResult` with `Success`/`ErrorMessage`)
  rather than throwing, so the UI shows friendly messages and tests assert outcomes.

## Data & persistence

- **SQLite** file at `%AppData%\PetSitters\petsitters.db`, created on first run by
  `Database.Initialize()` (idempotent). Tables: `Users`, `SitterProfiles`, `Pets`,
  `Bookings`, `ChatMessages` (FKs enabled).
- `Database` takes the db path as a constructor arg, so tests point it at an
  isolated temp file (see `DatabaseTestBase`). **To reset app data, delete the
  `.db` file.**
- Dates are stored as ISO-8601 round-trip strings.
- **Security:** passwords are salted PBKDF2 (`PasswordHasher`) — never plaintext.
  Login uses one message for "unknown email" and "wrong password" (no user
  enumeration). Chat rows are scoped per booking.

## Conventions

- Match the existing style: XML-doc comments on public types/members; guard-clause
  validation; `using` blocks around every SQLite connection/command; parameterised
  SQL only (no string concatenation of user input).
- Tests use AAA structure, `Method_Scenario_ExpectedResult` names, `[DataRow]` for
  boundary/equivalence cases, and are tagged to requirement IDs (FR-O*/FR-S*/FR-A*)
  in comments. Put DB tests in classes deriving from `DatabaseTestBase`.

## Known state / WIP

- **Owner-side chat (FR-O5) is not implemented.** Chat works only from the sitter
  dashboard (My Chats / hidden Chat tab). The chat data layer (`ChatRepository`,
  `ChatMessages`) is complete and tested for both directions.
- Team TODOs noted in the report doc: owner-side chat, a "verified/unverified"
  sitter field, and a pet-card UI redesign.

## Docs

Project documentation lives in `docs/` — see `docs/README.md` (architecture in
`docs/Architecture.md`, test suite in `docs/UnitTests.md`).
