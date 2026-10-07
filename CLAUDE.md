# CLAUDE.md

Guidance for Claude Code (and humans) working in this repository.

## What this is

**Sitters4Us** — a pet owner ↔ pet sitter marketplace ("Airbnb for pet sitting"),
built as a **.NET Framework 4.7.2 WPF desktop app**. It uses a shared **cloud MySQL**
database when one is configured and reachable at launch, and falls back to a local
**SQLite** database otherwise. It is a university **Software Quality Assurance
(ENSE707)** prototype; QA artefacts and tests matter as much as features.

> Naming: the product/brand is **Sitters4Us** (window title, headers). The Visual
> Studio **assembly and root namespace are still `PetSitters`** — do not rename
> namespaces; only user-facing text says "Sitters4Us".

## Solution layout (3 projects)

| Project | Kind | Framework | Purpose |
|---------|------|-----------|---------|
| `PetSitters` | WPF app, **classic (non-SDK) csproj** | net4.7.2 | The application |
| `PetSitters.Tests` | MSTest, **SDK-style** | net472 | Logic + integration tests (245 cases on SQLite, plus 102 opt-in MySQL parity cases) |
| `PetSitters.UiTests` | MSTest + FlaUI, SDK-style | net472 | End-to-end UI automation (8 tests) |

## Build, test, run — IMPORTANT tooling notes

The main app is a **classic WPF csproj that the .NET SDK (`dotnet build`) cannot
compile**. Use **Visual Studio MSBuild** for the app; the SDK-style test
projects build with `dotnet` *after* the app is built.

MSBuild path on this machine:
`C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`

Build the app (also restores its NuGet packages). **A command-line build also
runs the tests** (smoke set, then the full `PetSitters.Tests` suite, about 40 s;
both exclude `TestCategory=MySql`), and a failing test fails the build. Add
`/p:SkipTests=true` to build only. Builds inside Visual Studio skip this. See
`docs/CI.md`.
```bash
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" PetSitters.csproj /t:Restore,Build /p:Configuration=Debug
```

CI: `.github/workflows/ci.yml` runs a no-secrets check (fails if any `.env` /
`.env.*` other than `.env.example` is tracked) → build → smoke → full suite on every
push or PR to `main` (windows-latest). CI never connects to MySQL. **Do not add the
real `DATABASE_URL` as a repository secret** — the repo is public, so logs and
artifacts are too. The UI suite is not in CI; run it locally before pushing.

### Database configuration (`.env`)
1. Copy `.env.example` to `.env` **in the repo root** and fill in
   `DATABASE_URL=mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED`.
2. Build (VS or MSBuild). The csproj copies the root `.env` next to the exe
   (`bin\Debug\` / `bin\Release\`) only if it exists, and deletes a stale copy there
   once the root `.env` is gone. In VS, if `.env` was created after the project
   loaded (or deleted while it's loaded), reload the project, then rebuild.
3. Run `bin\Debug\PetSitters.exe`.

No `.env` = no settings = local SQLite only. Real environment variables override
`.env`. `.env` is git-ignored (`.env`, `.env.*`, `!.env.example`); `.env.example`
(placeholders only) documents every key. **`bin\` then holds the password in plain
text — never zip or share the repo folder or `bin\` with `.env` in it** (GitHub
"Download ZIP" / `git archive` exclude it).

Run the app: launch `bin\Debug\PetSitters.exe` (a windowed app).

Build + run the logic tests (build the app first — see below):
```bash
dotnet test PetSitters.Tests -c Debug
```

MySQL parity tests are **opt-in** (otherwise all 102 report Skipped/Inconclusive).
Set `PETSITTERS_TEST_MYSQL_URL` to a `mysql://` URL for a server where databases
can be created, or `PETSITTERS_TEST_MYSQL=1` to reuse the server from
`DATABASE_URL`/`.env` (that user must be allowed to create and drop databases),
then:
```bash
dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql
```
Each run creates its own `sitters4us_test_<UTC yyyyMMddTHHmm>_<8 hex>` database,
empties every table before each test, drops it at the end, and drops leftovers older
than 6 hours on the next run. It never touches the app's database. About 6 minutes
against the cloud server.

The UI tests launch the app with `PETSITTERS_DB=sqlite` (an environment variable,
so it beats `.env`): the suite never touches the shared cloud database.

In Visual Studio: open `PetSitters.sln`, then **Test → Run All Tests** (builds
everything and runs both test projects).

### Why the tests reference the built EXE (not a ProjectReference)
`PetSitters.Tests` and `PetSitters.UiTests` reference the **compiled**
`..\bin\$(Configuration)\PetSitters.exe` as an assembly, NOT via `<ProjectReference>`,
because the SDK build can't compile the classic WPF project. So: **build the app
first**, then build/run the tests. Adding the `System.Data.SQLite.Core`
PackageReference to `PetSitters.Tests` is what copies the native
`SQLite.Interop.dll` (x86/x64) into the test output so integration tests can open
a real database. Likewise `PetSitters.Tests` references **`MySqlConnector` 2.4.0,
the same version as the app**, so the test output gets the assembly and its
dependencies (`System.Memory`, `DiagnosticSource`, …) with consistent binding
redirects (`AutoGenerateBindingRedirects` + `GenerateBindingRedirectsOutputType`).
Keep the two versions in step. (VS BuildTools MSBuild alone cannot resolve
`Microsoft.NET.Sdk` — use `dotnet` for the SDK-style projects.)

### Classic csproj gotcha
`PetSitters.csproj` does **not** auto-include files. When you add a `.cs`/`.xaml`,
you must add a `<Compile>` / `<Page>` item to `PetSitters.csproj` by hand or it
won't compile.

## Architecture

UI-independent logic is deliberately separated from WPF so it can be unit-tested
without launching a window:

```
Models/     POCOs: User, SitterProfile, Pet, Booking, ChatMessage, enums
Data/       Database (provider-aware: SQLite or MySQL; schema + connection factory)
            + one repository per table
            SqlDialect (the only engine-specific SQL; DatabaseProvider enum),
            DbCommandExtensions (AddParameter, portable AddWithValue),
            MySqlUrl (mysql:// URL -> connection string; errors never contain the URL)
Services/   PasswordHasher (PBKDF2), ValidationHelper, AuthService (+AuthResult),
            BookingService (+BookingResult): request (REQ-GR-04 validation, REQ-PO-08 same-pet overlap),
            accept (REQ-GR-08 sitter overlap), cancel (REQ-PO-07),
            AppConfig (+EnvFile): env vars override .env next to the exe,
            DatabaseSelector (+DatabaseSelection): cloud-or-local decision at launch,
            AppServices (composition root; holds repos + CurrentUser)
Views/      WPF UserControls, one per screen, swapped into MainWindow
```

- Startup (`App.xaml.cs`): `MainWindow` opens immediately showing "Connecting to the
  database…"; `AppServices.CreateDefault()` runs on a background thread
  (`Task.Run`), where `DatabaseSelector` picks the database; then
  `MainWindow.Attach(services)` shows the login screen. MainWindow swaps views
  (login → register → role dashboard).
- The database is chosen **once per launch** — no switching or syncing afterwards;
  restart the app to retry the cloud. The header's `StorageStatus` shows
  "☁ Cloud database", "Local database", "⚠ Offline: local database" (couldn't
  connect) or "⚠ Cloud database error: local database" (connected, schema step
  failed). The tooltip never shows host, URL, user or password. In fallback the login
  screen shows an amber notice that cloud accounts won't work.
- `App.DispatcherUnhandledException` turns mid-session database exceptions into a
  message ("couldn't reach its database" vs "couldn't save this change … report this
  code: …") instead of a crash; exception text is never shown. Pooled MySQL
  connections are closed on exit (`ClearAllPools`).
- Roles: `UserRole.Owner` / `UserRole.Sitter`. `AppServices.CurrentUser` is the
  session.
- Services return **result objects** (`AuthResult` with `Success`/`ErrorMessage`)
  rather than throwing, so the UI shows friendly messages and tests assert outcomes.

## Data & persistence

- **Two stores, same five tables** (`Users`, `SitterProfiles`, `Pets`, `Bookings`,
  `ChatMessages`, FKs enabled), created by `Database.Initialize()` (idempotent; once
  per `Database` instance):
  - **Cloud MySQL** (`Database.ForMySql`) — used when `DATABASE_URL` is set and the
    connect + schema step succeeds at launch.
  - **Local SQLite** (`Database.CreateLocalSqlite()`) at
    `%AppData%\PetSitters\petsitters.db` — used otherwise.
- **Settings** (environment variables override `.env`; see `.env.example`):
  `DATABASE_URL` (`mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED`;
  ssl-mode DISABLED/PREFERRED/REQUIRED/VERIFY_CA/VERIFY_IDENTITY, default REQUIRED;
  port default 3306), `PETSITTERS_DB` = `auto` (default) | `sqlite` (alias `local`:
  skip MySQL), `DB_CONNECT_TIMEOUT_SECONDS` (1–60, default 8).
- **MySQL schema differences** (`InitializeMySql`): `INT AUTO_INCREMENT PRIMARY KEY`
  (the server requires a primary key on every table); `Email VARCHAR(255)` (the
  `(Email, Role)` unique key can't use TEXT); multi-line fields `MEDIUMTEXT`, other
  text `TEXT` (the server runs STRICT mode — over-long values are rejected, not
  truncated); money `DECIMAL(10,2)` (SQLite: REAL); dates `VARCHAR(40)`; collation
  `utf8mb4_0900_as_ci` (case-insensitive like NOCASE, though MySQL also folds
  non-ASCII case such as É/é, while NOCASE folds ASCII only); InnoDB with the same named
  CASCADE / SET NULL FKs. Tables are created in one command, only if a single
  `information_schema` check finds any missing.
- **Pool settings** (`MySqlUrl`): max 5 (the server's connection limit is small and
  shared), min 1 (a new TLS connection costs ~1.2–1.9 s), `ConnectionReset=false`
  (measured 392 → 195 ms per pooled query; safe because the app sets no session
  state). Don't change these without re-measuring.
- `Database` takes the db path as a constructor arg, so tests point it at an
  isolated temp file (see `DatabaseTestBase`, whose virtual `CreateDatabase()` the
  MySQL parity subclasses override). **Deleting the `.db` file resets only the
  local store**, not the cloud database.
- Dates are stored as ISO-8601 round-trip strings in both engines (shared
  read/write code; ordering is chronological).
- **Accounts are unique per (Email, Role)** (REQ-GR-06): one email may have an
  Owner and a Sitter account, never two of one role. `Users` used to be
  `UNIQUE(Email)`; `ApplyMigrations` rebuilds old databases with foreign keys
  OFF (with them on, dropping `Users` cascade-deletes everything). Never rebuild a
  table here with FKs on. These migrations (PRAGMA column adds, the Users rebuild)
  are **SQLite-only**; the MySQL schema is created in its current shape.
- **Security:** passwords are salted PBKDF2 (`PasswordHasher`) — never plaintext.
  Login uses one message for "unknown email" and "wrong password" (no user
  enumeration). Chat rows are scoped per booking. **Never commit, log or display
  `DATABASE_URL`** or any part of it (host, user, password) — not in code, docs,
  tests, error messages or UI; use placeholders.

## Conventions

- Match the existing style: XML-doc comments on public types/members; guard-clause
  validation; `using` blocks around every connection/command (`DbConnection` from
  `Database.OpenConnection()`); parameterised SQL only via `AddParameter` (no string
  concatenation of user input).
- SQL must run on **both** engines. Never write double-quoted string literals — the
  MySQL server runs in `ANSI_QUOTES` mode; use single quotes. Put anything
  engine-specific (last-insert-id, case-insensitive ORDER BY, upsert) in `SqlDialect`,
  not in a repository.
- Tests use AAA structure, `Method_Scenario_ExpectedResult` names, `[DataRow]` for
  boundary/equivalence cases, and are tagged to requirement IDs (FR-O*/FR-S*/FR-A*)
  in comments. Put DB tests in classes deriving from `DatabaseTestBase`.
  Cloud-database tests are tagged REQ-GR-09 (proposed: "Shared cloud database with
  local fallback at launch").
- **Every test carries `[TestCategory]` labels on two axes:** a type
  (`Unit`/`Integration`/`System`/`Acceptance`/`Regression`/`Security`/`Performance`/`Usability`)
  and a scenario (`Positive`/`Negative`/`Boundary`/`InvalidInput`/`ErrorHandling`).
  Label new tests the same way. The definitions, counts and per-test list are in
  `docs/UnitTests.md` → "Test classification". Regenerate that appendix if you add tests.
- **`TestCategory("MySql")` is opt-in and excluded from the build gate and CI.**
  MySQL parity classes live in `MySqlParityTests.cs` as `<SqliteClass>_MySql`
  subclasses that inherit every test and swap only the database; new DB test
  classes should get one. Assert on `DbException`, not an engine-specific type
  (MSTest `ThrowsException` is exact-type).

## Known state / WIP

- **UI regression suite fails at the default window size (since the login/register
  redesign, 503e9af/6f33d67).** The 920×640 window clips the password box off
  screen, so FlaUI throws `NoClickablePointException`. All 8 UI tests pass with the
  window maximised (checked 2026-10-07). Fix the layout (or add a ScrollViewer)
  rather than maximising in the driver, because real users hit the same clipping.
- **No sync between the cloud and local stores**, and existing local data is not
  migrated to the cloud. A fallback session's data stays on that PC.
- **Profile/pet images are local-only.** They are copied to
  `%AppData%\PetSitters\UserImages` on the uploading PC and only that path is stored,
  so on the cloud database other PCs don't see them (pet images on the sitter side
  only work on the same PC).
- **Cloud latency:** ~195 ms per query, so screens must load each list with ONE
  query: `BookingRepository.GetDetailsForOwner/GetDetailsForSitter`,
  `UserRepository.GetSittersWithProfiles`, `ChatRepository.GetForBookingWithSenderNames`
  (JOINs). Never add per-row lookups (`FindById`/`GetByOwner` inside a loop) to a
  view: tests assert single round trips via `Database.ConnectionsOpened`. Measured
  owner dashboard 11.0 s -> 1.2 s, accept 14.3 s -> 1.2 s after this change. The
  dashboards' `BookingStatusChanged` listeners reload after status changes and are
  detached on `Unloaded` (they used to leak one per login). Queries still run on
  the UI thread.
- **2-tier security:** every copy of the app holds the database credential in plain
  text and talks to the database directly, so authorisation rules (owner-only
  cancel, chat per booking, …) are enforced only in the client; anyone with the
  `.env` can bypass them. Mitigations: a dedicated database and least-privilege user
  (SELECT/INSERT/UPDATE/DELETE on that database only), trusted sources on the server,
  rotating the password after the assignment, `ssl-mode=VERIFY_CA` (REQUIRED encrypts
  but doesn't verify the certificate).
- Team TODOs noted in the report doc: a "verified/unverified" sitter field and a
  pet-card UI redesign.

## Docs

Project documentation lives in `docs/` — see `docs/README.md` (architecture in
`docs/Architecture.md`, test suite in `docs/UnitTests.md`, build gate and CI in
`docs/CI.md`). Every runtime setting is documented in `.env.example`.
