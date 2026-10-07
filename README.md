# PetSitters

A WPF (.NET Framework 4.7.2) prototype for a Software Quality Assurance project.
It connects **pet owners** with nearby **pet sitters** — "basically Airbnb, but for
pet sitting". Owners register their pets and browse sitters; sitters advertise their
availability, experience and daily rate; owners then send booking requests that
sitters can accept or decline.

## Functional requirements covered

| ID | Requirement | Where it lives |
|----|-------------|----------------|
| FR-1 | Account creation (Owner or Sitter) | `Views/RegisterView`, `Services/AuthService.Register` |
| FR-2 | Login | `Views/LoginView`, `Services/AuthService.Login` |
| FR-3 | Owner registers personal details incl. location | `OwnerDashboardView` → *My Details* tab |
| FR-4 | Owner registers pet details | `OwnerDashboardView` → *My Pets* tab |
| FR-5 | Owner browses potential sitters | `OwnerDashboardView` → *Find Sitters* tab |
| FR-6 | Owner requests a booking (sitter accepts/declines) | `OwnerDashboardView` *Find Sitters*; `SitterDashboardView` *Booking Requests* |
| FR-7 | Sitter registers personal details incl. location | `SitterDashboardView` → *My Details* tab |
| FR-8 | Sitter registers availability, experience, preferences, qualifications, daily rate | `SitterDashboardView` → *My Sitting Profile* tab |
| FR-O5 / FR-S5 | Owner and sitter chat about an accepted booking | *Chats* / *My Chats* tabs in both dashboards, `Data/ChatRepository` |
| REQ-GR-09 (proposed) | Shared cloud database with local fallback at launch | `Services/DatabaseSelector`, `Services/AppConfig`, `MainWindow` → header status |

## Architecture

The code is deliberately layered so the **business/data logic is independent of the
WPF UI** — this is what makes the assignment's test cases straightforward to write
(you can test `AuthService`, `ValidationHelper`, `PasswordHasher` and the
repositories without launching a window).

```
Models/      Plain data objects (User, SitterProfile, Pet, Booking, ChatMessage, enums)
Data/        Persistence (cloud MySQL or local SQLite, same code for both)
             - Database.cs           provider-aware schema creation + connection factory
             - SqlDialect.cs         the only engine-specific SQL (last insert id,
                                     case-insensitive ORDER BY, upsert)
             - MySqlUrl.cs           parses DATABASE_URL into a MySQL connection string
             - DbCommandExtensions.cs  AddParameter (portable replacement for AddWithValue)
             - *Repository.cs        CRUD for each table
Services/    UI-independent logic
             - PasswordHasher.cs     PBKDF2 password hashing (no plaintext)
             - ValidationHelper.cs   email/password/number validation
             - AuthService.cs        registration + login rules
             - BookingService.cs     booking rules: request (form validation, no same-pet overlap),
                                     accept (no sitter overlap), owner cancel
             - AppConfig.cs          runtime settings: environment variables, then .env
             - DatabaseSelector.cs   picks the database at launch (cloud first, local fallback)
             - AppServices.cs        composition root (wires everything together)
Views/       WPF UserControls (one per screen), swapped into MainWindow
```

At startup `App.xaml.cs` opens `MainWindow` straight away with *Connecting to the
database…*, loads the settings (`AppConfig`), and chooses the database on a
background thread (`DatabaseSelector`) so the window never freezes. It then builds
`AppServices` around that database and hands it to `MainWindow.Attach`, which shows
the login screen and from then on swaps the active view (login → register →
owner/sitter dashboard). If the database fails mid-session (for example the
connection drops), the app shows a short message instead of crashing; on exit it
closes its pooled MySQL connections.

## Persistent data storage (MySQL first, SQLite fallback)

The app uses a **shared cloud MySQL 8 database** when one is configured, and a
**local SQLite file** otherwise (proposed requirement REQ-GR-09). The choice is
made **once, at launch**:

1. `PETSITTERS_DB=sqlite` → local database (cloud skipped).
2. No `DATABASE_URL` configured → local database.
3. Otherwise the app connects to the cloud database and creates any missing
   tables. If that works, everyone running the app with the same `.env` shares
   the same accounts, pets, bookings and chats.
4. If it can't connect (offline, timeout, wrong password, bad URL…) or the table
   setup fails, it **falls back to the local database** and says so in the header.

There is no switching or syncing afterwards: restart the app to try the cloud
again. How to configure it is in [Configuring the database (.env)](#configuring-the-database-env).

- **Local file:** still `%AppData%\PetSitters\petsitters.db`
  (e.g. `C:\Users\<you>\AppData\Roaming\PetSitters\petsitters.db`), created
  automatically on first use.
- **Schema:** created by `Database.Initialize()` (idempotent — safe on every
  start). Both engines get the same five tables — `Users`, `SitterProfiles`,
  `Pets`, `Bookings`, `ChatMessages` — with the same keys and the same
  `CASCADE` / `SET NULL` foreign-key rules. On MySQL a single check finds out
  whether any table is missing, so a normal launch costs one round trip. Column
  types differ only where MySQL needs it (`VARCHAR(255)` emails for the unique
  (Email, Role) key, `DECIMAL(10,2)` money, `MEDIUMTEXT` for long text); dates
  are ISO-8601 strings on both.
- **Libraries:** `System.Data.SQLite.Core` (the native `SQLite.Interop.dll` is
  copied into `bin\...\x86` and `x64` at build time) and `MySqlConnector` 2.4.0
  (MIT). Both come from NuGet on restore.
- **Testability:** `Database` takes the SQLite file path as a constructor argument,
  so tests point it at a throwaway temp file instead of the real AppData database.
  The same repository and service tests can also be run against a real MySQL
  server (opt-in; see [docs/UnitTests.md](docs/UnitTests.md)).

**To reset the local data** during testing, delete `petsitters.db` and relaunch —
the empty schema is recreated. This only resets the **local** store: the cloud
database is shared with everyone using the same `.env`, and deleting the file
does not touch it.

### Security note (quality attribute)

Passwords are **never stored in plain text**. `PasswordHasher` uses PBKDF2
(`Rfc2898DeriveBytes`, SHA-256, 100k iterations) with a random per-user salt, and
login uses a length-constant comparison. Login failures return the same message
whether the email is unknown or the password is wrong, so the app does not reveal
which emails are registered.

One email can have both an **owner and a sitter account** (REQ-GR-06), but not
two of the same role. If the same password opens both, the login screen asks
which one to use.

## Building & running

Open `PetSitters.sln` in Visual Studio 2022 and press **F5**, or from a command line:

```bash
msbuild PetSitters.csproj /t:Restore
msbuild PetSitters.csproj /t:Build /p:Configuration=Debug
```

The built app is `bin\Debug\PetSitters.exe`.

Without a `.env` the app runs on the local database only — see the next section
to connect it to the cloud database.

A command-line build **also runs the automated tests** (smoke set, then the full
suite), and fails if any test fails. Add `/p:SkipTests=true` to skip them. The
same checks run in GitHub Actions on every push, plus a check that no `.env` file
has been committed. Neither the build gate nor CI ever connects to MySQL. See
[docs/CI.md](docs/CI.md).

## Configuring the database (.env)

The cloud connection lives in a **`.env` file in the repo root**, which is
git-ignored and never committed. The committed `.env.example` (placeholders only)
documents every key.

1. **Create it** — copy `.env.example` to `.env`, next to `PetSitters.csproj`:

   ```bat
   copy .env.example .env
   ```

   (`copy` works in cmd and PowerShell; in Git Bash use `cp`.)

2. **Fill it in** — set `DATABASE_URL` to the connection URL you were given:

   ```
   DATABASE_URL=mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED
   ```

   Percent-encode special characters in the password (`@` → `%40`, `:` → `%3A`,
   `#` → `%23`). `ssl-mode=REQUIRED` (the default) encrypts the connection;
   `VERIFY_CA` also checks the server's certificate.

3. **Build** as usual (Visual Studio or the MSBuild commands above). The build
   copies `.env` next to the exe (`bin\Debug\.env` or `bin\Release\.env`), and
   that copy is what the app reads. If you created `.env` while the solution was
   already open in Visual Studio, reload the project (or reopen the solution) and
   rebuild so the copy is picked up; do the same after deleting it. Delete the
   repo-root `.env` and rebuild to remove the copy too.

4. **Run** `bin\Debug\PetSitters.exe`. The window shows *Connecting to the
   database…* for a moment (about 2.4 s to the cloud in our measurement), then
   the login screen.

**Which database am I on?** The right-hand side of the header says:

| Header | Meaning |
|--------|---------|
| ☁ Cloud database | Connected to the shared MySQL database. |
| Local database | No `DATABASE_URL`, or `PETSITTERS_DB=sqlite`. |
| ⚠ Offline: local database | Couldn't connect (no internet, timeout, refused, wrong password, unknown database, malformed URL), so it fell back to the local file. |
| ⚠ Cloud database error: local database | Connected, but setting up the tables failed — a real error, not a network problem. |

Hover over it for the reason and an error code (it never shows the host, user
name or password). In either fallback state the login screen also shows an amber
notice: cloud accounts won't work and nothing created now is shared. If the cloud
is unreachable the app waits up to `DB_CONNECT_TIMEOUT_SECONDS` before falling
back. **Restart the app to try the cloud again.**

**Settings** (all optional):

| Key | Values | Default |
|-----|--------|---------|
| `DATABASE_URL` | `mysql://…` as above | none → local database only |
| `PETSITTERS_DB` | `auto` (cloud first, then local) or `sqlite` (alias `local`: always local) | `auto` |
| `DB_CONNECT_TIMEOUT_SECONDS` | 1–60 | 8 |

**Real environment variables override `.env`.** For example, to force the local
database for one run without editing the file (PowerShell):

```powershell
$env:PETSITTERS_DB = "sqlite"; .\bin\Debug\PetSitters.exe
```

The UI test suite launches the app this way, so it never touches the shared
cloud database.

> **Security — this repository is public.**
> - Never commit `.env` (it is in `.gitignore`, and CI fails if one is tracked).
>   Don't force-add it, and don't paste the URL into an issue, a commit message or
>   a GitHub Actions secret.
> - After a build, `bin\Debug\.env` (or `bin\Release\.env`) holds the password in
>   plain text. **Never zip or share the repo folder or `bin\` while `.env` is in
>   it.** (GitHub's *Download ZIP* and `git archive` are safe: they only include
>   tracked files.)
> - Every copy of the app that has the `.env` has the full database credential —
>   see [Known limitations](#known-limitations).

## Try it (happy path)

1. **Create a sitter account** (choose *Offer sitting*). On the sitter dashboard,
   open *My Sitting Profile*, set availability / experience / a daily rate, and save.
2. **Log out**, then **create an owner account** (choose *Find a sitter*).
3. On the owner dashboard, add a pet under *My Pets*.
4. Open *Find Sitters*, pick the sitter, choose dates and a pet, and send a request.
5. **Log out** and log back in as the sitter → *Booking Requests* → **Accept**.
6. Log back in as the owner → *My Bookings* shows the request now **Accepted**.

## Known limitations

- **No sync.** The database is chosen once at launch. Anything created during a
  fallback session stays in the local file and is never uploaded, and existing
  local data is not migrated to the cloud.
- **Images are local only.** Profile and pet photos are copied to
  `%AppData%\PetSitters\UserImages` on the PC that uploaded them, and only that
  file path is stored. On the cloud database, other PCs see the record but not
  the picture (so pet images on the sitter side only show on the same PC).
- **Latency on the cloud database.** Each query takes about 195 ms (measured),
  and the dashboards currently make one query per row, so some screens take
  several seconds to load. Batching those queries is the next planned fix.
- **Authorisation is client-side only.** This is a two-tier design: every copy of
  the app connects to the database directly with the credential from `.env`, so
  rules such as "only the owner can cancel" or "chat is per booking" are enforced
  only in the app. Anyone with the `.env` can bypass them with a MySQL client.
  Mitigations: a dedicated database and a least-privilege user (SELECT / INSERT /
  UPDATE / DELETE on that one database only), the host's trusted-sources (IP
  allow-list) setting, rotating the password after the assignment, and
  `ssl-mode=VERIFY_CA` so the server certificate is checked (`REQUIRED` encrypts
  but does not verify it).

## Notes / possible next steps

- Tests: `PetSitters.Tests` (MSTest logic + integration, with a traceability matrix
  in [docs/UnitTests.md](docs/UnitTests.md)) and `PetSitters.UiTests` (FlaUI
  end-to-end regression, always on the local database). Build and CI gates are
  described in [docs/CI.md](docs/CI.md).
- MySQL parity tests: the repository and service tests can be re-run against a
  real MySQL server. They are **opt-in** and skipped otherwise: set
  `PETSITTERS_TEST_MYSQL_URL` to a `mysql://` URL for a server where databases can
  be created (or `PETSITTERS_TEST_MYSQL=1` to reuse the server from `.env`; its
  user must be allowed to create and drop databases, so a least-privilege app
  user can't run it), then
  run `dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql`. Each
  run uses its own throwaway database and drops it afterwards; it never touches
  the app's data.
- Location matching is textual (owners see all sitters). Distance-based search would
  be a sensible enhancement.
