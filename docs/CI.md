# Continuous Integration & quality gates

How Sitters4Us is built, tested and integrated, and which automated checks
must pass before a change counts as done. (Supports ENSE707 Task 5.)

---

## Development & integration workflow

```mermaid
flowchart LR
    A[Edit code + tests] --> B["Local build<br/>(MSBuild CLI)"]
    B -->|build runs smoke + full suite| C{All pass?}
    C -- no --> A
    C -- yes --> D["UI regression suite<br/>(FlaUI, local, before merge)"]
    D --> E[Commit to main + push]
    E --> F["GitHub Actions CI<br/>secrets check → build → smoke → full suite"]
    F -->|red| A
    F -->|green| G[Change accepted]
```

- The team works on `main`, with one commit per requirement
  (e.g. *"Addressed REQ-GR-04: …"*), and pulls before pushing. Merge conflicts
  are resolved locally and re-tested before pushing.
- **Locally**, building the app from the command line also runs the tests (see
  below), so a change can't "build" while breaking a test.
- **On every push and pull request to `main`**, the CI workflow
  [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) re-runs the gates on a
  clean Windows machine and reports the results on the commit or PR. It first
  checks that no `.env` file (which holds the database password) has been
  committed.
- **Neither the local gate nor CI ever connects to the shared cloud MySQL
  database.** Every gated test runs on throwaway SQLite files; see
  [No cloud database in the gates](#no-cloud-database-in-the-gates). The MySQL
  checks are a separate, opt-in run
  ([MySQL parity tests](#mysql-parity-tests-opt-in)).

## Quality gates

| # | Gate | Where | Fails when |
|---|------|-------|-----------|
| 0 | **No secrets committed** | CI | A `.env` or `.env.*` file other than `.env.example` is tracked in git. `.env` holds the database password and the repository is public. |
| 1 | **Successful build** | CI + local | The WPF app doesn't compile (Release in CI). |
| 2 | **Smoke tests**: 12 tests tagged `Smoke` | CI + local build | Any core path is broken: password hashing, reading `.env` and `DATABASE_URL`, choosing the database at launch (cloud when reachable, local fallback when not), register, log in (< 1 s, on SQLite), schema/migration start-up, booking request, accept, chat persistence. |
| 3 | **Full logic + integration suite**: 238 cases | CI + local build | Any unit or integration test fails. Runs on SQLite; the 95 MySQL parity cases are excluded (see below). |
| 4 | **UI regression suite**: 8 FlaUI tests | Local, before merge | An end-to-end journey breaks (see below for why it isn't in CI). |

Gates run in order and **stop at the first failure**. Smoke runs before the
full suite deliberately: in the 2026-10-07 run it took about 6.5 s against
about 39 s, so a badly broken build is reported quickly. About 2 s of the
smoke time is the cloud-unreachable fallback test, which waits for a real
refused connection.

**Gate 0 runs only in CI**, before anything is built. Locally, `.gitignore`
(`.env`, `.env.*`, `!.env.example`) keeps the file out of commits, but
`git add -f` gets past it. **Gate 0 catches that mistake only after the push**,
when the password is already in the public history. If it ever fails, rotate
the database password first, then remove the file: deleting it in a later
commit doesn't remove it from history. To check before pushing,
`git ls-files .env ".env.*"` must print nothing except `.env.example`.

**Evidence that the gate actually gates (2026-10-02):** the minimum password
length was temporarily changed from 6 to 5. The build ran the tests, two
boundary tests failed (`IsValidPassword_EnforcesMinimumLengthBoundary("12345")`,
`Register_WithWeakPassword_Fails`), and **the build failed**
(`error MSB3073 … exited with code 1`). The change was then reverted.

### No cloud database in the gates

The app is MySQL-first (see `Architecture.md`), but no gate touches the cloud
database:

- **Filters.** The local build gate (`RunTestsAfterBuild` in `PetSitters.csproj`)
  and CI both run smoke as `TestCategory=Smoke&TestCategory!=MySql` and the full
  suite as `TestCategory!=MySql`. The `!=MySql` on the smoke filter is needed:
  the MySQL parity classes inherit every test of the SQLite classes, `Smoke`
  tags included.
- **The cloud smoke tests don't use the network or a `.env`.**
  `Select_CloudReachable_UsesCloud` uses a temporary SQLite file as a stand-in
  "cloud". `Select_CloudUnreachable_FallsBackToLocal_AndExplains` makes a real
  MySQL connection to a closed port on the same machine (`127.0.0.1:1`), which is
  refused. Both build their settings in memory, so a developer's own `.env`
  doesn't change the gate's result.
- **CI has nothing to connect with.** `.env` is git-ignored, so the checkout
  has none, and no `DATABASE_URL` secret is configured.

**Do not add the production `DATABASE_URL` as a repository secret.** The
repository is public, so CI run logs and the `test-results` artifact are public
too. GitHub masks only a secret's exact value, so the password printed on its
own (inside a connection string, say) would not be masked. A test pointed at
it would also write to the database real users share.

### Gates not enforced (and why)

| Gate | Status | Reason / how to add |
|------|--------|---------------------|
| Linting | Not gated | The app is a classic .NET Framework csproj that `dotnet format` can't analyse. Roslyn analysers could be added later. |
| Code review | Not enforced by tooling | Two-person team working on `main`. It could be enforced with GitHub branch protection (require a PR, one review, and CI green before merge). That's a repository setting, not code. |
| Coverage threshold | Not gated | Coverage tooling for .NET Framework 4.7.2 adds setup cost. Requirement coverage is tracked instead through the traceability matrix in `UnitTests.md`. |
| MySQL integration (parity suite) | Opt-in, local only | Needs credentials for a server where databases can be created, takes about 6 minutes over the internet, and creates and drops a database on a shared server with a small connection limit. Run it by hand before merging a change to `Data/` or the schema ([below](#mysql-parity-tests-opt-in)). Moving it into CI would need a disposable MySQL server, never the production one (see above). |

## Smoke / sanity testing

The smoke set is a deliberately small slice through the main user journey, at
service level:

| Smoke test | Proves |
|-----------|--------|
| `CreateHash_ThenVerifyWithCorrectPassword_ReturnsTrue` | Password hashing works |
| `EnvFile_ParsesKeyValuePairs_IgnoringCommentsAndBlanks` | The `.env` settings file is read correctly |
| `MySqlUrl_ParsesEveryPart` | A `DATABASE_URL` becomes a valid MySQL connection (host, port, user, password, database, SSL mode, timeout, small pool) |
| `Select_CloudReachable_UsesCloud` | At launch, the cloud database is chosen when it can be reached |
| `Select_CloudUnreachable_FallsBackToLocal_AndExplains` | When it can't be reached, the app falls back to the local database and says so, without naming the server |
| `Initialize_RunTwice_IsIdempotent` | Database schema and migrations start up cleanly, including on an existing database |
| `Register_WithValidDetails_Succeeds` | An account can be created |
| `Login_WithCorrectCredentials_SucceedsWithinPerformanceBudget` | That account can log in, in under 1 s |
| `RequestBooking_ValidSubmission_IsStoredAsPending` | An owner can request a booking |
| `Insert_BookingIsVisibleToBothOwnerAndSitter` | The sitter sees it |
| `AcceptRequest_WithNoClash_AcceptsAndPersists` | The sitter can accept it |
| `Message_IsPersisted_AndReadBackByAFreshRepository` | Chat messages are saved |

The < 1 s login figure is measured on **local SQLite**, like the rest of the
gate. The MySQL parity copy of the same performance test also passed against
the cloud server, at about 195 ms per query.

Run just the smoke set with:
```
dotnet test PetSitters.Tests -c Debug --filter "TestCategory=Smoke&TestCategory!=MySql"
```

## MySQL parity tests (opt-in)

The gates prove the app on SQLite. `PetSitters.Tests/MySqlParityTests.cs` proves
the same behaviour on MySQL. It defines 9 test classes tagged
`TestCategory("MySql")` (`AuthServiceTests_MySql`, `BookingRepositoryTests_MySql`,
`BookingServiceTests_MySql`, `BookingValidationTests_MySql`,
`ChatPersistenceTests_MySql`, `UserRepositoryTests_MySql`,
`PetRepositoryTests_MySql`, `SitterProfileRepositoryTests_MySql`,
`SharedEmailTests_MySql`). Each inherits **every** test of the matching SQLite
class and swaps only the database (`DatabaseTestBase.CreateDatabase()`), giving
**95 executed cases**. They catch SQL only one engine accepts (the
engine-specific SQL lives in `Data/SqlDialect.cs`), values MySQL's strict mode
rejects, and collation or ordering differences.

**They are strictly opt-in.** Without one of the variables below, all 95 are
reported **Skipped** (Inconclusive), never failed. The opt-in lives in the test
code rather than in a filter because Visual Studio's *Run All Tests* ignores
command-line filters.

| Variable | Effect |
|----------|--------|
| `PETSITTERS_TEST_MYSQL_URL` | A `mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED` URL for a server where that user can create databases (the URL must still name a database, but it is ignored). |
| `PETSITTERS_TEST_MYSQL=1` | Reuse the server in the app's `DATABASE_URL` (environment variable, or the repo-root `.env`). Its user must be allowed to create and drop databases. |

Run (PowerShell; build the app first, as for any test run):
```
$env:PETSITTERS_TEST_MYSQL = "1"
dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql
```

- **It never touches the app's database.** Each run creates its own database,
  `sitters4us_test_<UTC yyyyMMddTHHmm>_<8 hex>`, empties every table before each
  test, and drops the database when the run ends. A crashed run's leftover is
  dropped by the next run once it is more than 6 hours old. The unique names let
  two people run the suite at the same time.
- **It is slow:** about 6 minutes, because every query is a round trip to the
  server.
- **Last result (2026-10-07): 95/95 passed** against the real cloud MySQL server.

## Why the UI suite runs locally, not in CI (alternative workflow)

The FlaUI suite drives the **real desktop**: it moves the mouse, types and
reads the screen. Hosted CI runners have no reliable interactive desktop.
The suite is also currently affected by a known layout defect (the login screen
is clipped at the default 920×640 window) and fails if anyone uses the mouse
mid-run. Running it in CI would make the pipeline red for reasons unrelated to
the change being tested. All 8 tests pass with the window maximised.

So the UI suite is a **manual pre-merge gate**: whoever changes a view runs it
locally before pushing (`dotnet test PetSitters.UiTests`), and it saves a
screenshot of any failure. A self-hosted Windows runner with an
auto-logged-in desktop session would allow it to move into CI later.

**The UI suite doesn't use the cloud database either.** The driver launches the
app with `PETSITTERS_DB=sqlite`, and an environment variable beats `.env`. So a
developer's `.env` never points the suite at the shared database, and the
suite's database wipe between tests still resets everything. Two tests cover
the start-up choice (REQ-GR-09, proposed):

- `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo` relaunches with
  `PETSITTERS_DB=auto` and a `DATABASE_URL` pointing at a closed local port
  (connect timeout 3 s). It checks that the header reads *Offline: local
  database* and that registration still works. The real cloud server is never
  contacted.
- `Startup_LocalDatabaseChosen_HeaderSaysLocalDatabase` checks that the
  forced-local header says *Local database*.

## How to run everything

| What | Command |
|------|---------|
| Build + smoke + full suite (local gate) | `MSBuild.exe PetSitters.csproj /t:Restore,Build /p:Configuration=Debug` |
| Build only | add `/p:SkipTests=true` |
| Full suite only | `dotnet test PetSitters.Tests -c Debug --filter "TestCategory!=MySql"` (without the filter, the 95 MySQL cases are listed as Skipped unless opted in) |
| MySQL parity suite (opt-in) | set `PETSITTERS_TEST_MYSQL=1` or `PETSITTERS_TEST_MYSQL_URL`, then `dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql` (about 6 min) |
| UI regression suite | `dotnet test PetSitters.UiTests -c Debug` (interactive desktop; don't touch the mouse) |
| CI | Automatic on push or PR to `main`; manual via **Actions → CI → Run workflow** |

Building inside **Visual Studio** does *not* run the tests: Test Explorer does
that, and running the full suite on every F5 would slow development down. Test
results are written as `.trx` files to `TestResults/` (git-ignored). CI uploads
them as the `test-results` artifact and summarises them on the run page.
