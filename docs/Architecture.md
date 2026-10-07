# Sitters4Us — Application Architecture & Design

Technical documentation for the Sitters4Us prototype: what it does, how it is
structured, its data model, where its data is stored, and how the main
workflows run.

> Product/brand name: **Sitters4Us**. Visual Studio assembly & namespace:
> **`PetSitters`**.

---

## 1. Overview

Sitters4Us connects **pet owners** with **pet sitters**. Owners register their
pets and browse sitters; sitters advertise availability, experience, and a daily
rate; owners send booking requests that sitters accept or decline; once a booking
is accepted the two parties can chat.

It is a **desktop application** (WPF) with **two possible data stores, one of
which is chosen once at launch**:

- **Cloud database (primary):** a shared **MySQL 8** server, configured by
  `DATABASE_URL` in a `.env` file. When it is reachable, every copy of the app
  sees the same users, pets, bookings and chats, so an owner and a sitter on
  different computers can actually exchange requests and messages.
- **Local database (fallback):** a **SQLite** file on this computer, used when
  no cloud database is configured, when it is switched off on purpose
  (`PETSITTERS_DB=sqlite`), or when it can't be reached at launch. It needs no
  server or internet connection, but nothing in it is shared.

The window header always says which store is in use. There is no switching or
syncing after launch (§5).

### Technology stack

| Concern | Choice |
|---------|--------|
| Language / runtime | C# on .NET Framework 4.7.2 |
| UI | WPF (XAML), classic (non-SDK) project |
| Persistence (primary) | MySQL 8 (a managed cloud server) via `MySqlConnector` 2.4.0 (NuGet, MIT licence) |
| Persistence (fallback) | SQLite via `System.Data.SQLite.Core` (NuGet) |
| Configuration | `.env` file beside `PetSitters.exe`, overridden by environment variables (`Services/AppConfig.cs`) |
| Password hashing | PBKDF2 (`Rfc2898DeriveBytes`, SHA-256, 100k iterations) |
| Tests | MSTest; FlaUI for UI automation; repository/service suites re-run on MySQL (opt-in) |

---

## 2. Solution structure

```
PetSitters.sln
├── PetSitters/                 The WPF application (assembly "PetSitters")
│   ├── App.xaml(.cs)           Startup: opens MainWindow, picks the database in the
│   │                           background, attaches AppServices; global database-error handler
│   ├── MainWindow.xaml(.cs)    Shell: header (logo, database status, session bar) + swaps the active view
│   ├── Models/                 Plain data objects (POCOs)
│   ├── Data/                   Database (SQLite or MySQL), SqlDialect, MySqlUrl + one repository per table
│   ├── Services/               UI-independent logic (auth, booking rules, validation, hashing,
│   │                           configuration, database selection)
│   ├── Views/                  One WPF UserControl per screen
│   └── .env.example            Committed template for the git-ignored .env (placeholders only, §5.2)
├── PetSitters.Tests/           Logic + integration tests (MSTest), incl. opt-in MySQL parity → docs/UnitTests.md
└── PetSitters.UiTests/         End-to-end UI automation (FlaUI + MSTest), always on the local database
```

### Layered architecture

The golden rule: **business/data logic never depends on WPF**, so it can be
tested without a window. A second rule came with the cloud database:
**repositories never depend on the engine**, so one copy of each query serves
both MySQL and SQLite.

```mermaid
flowchart TD
    subgraph UI["Views (WPF)"]
        MW["MainWindow shell + database status"]
        LV[LoginView / RegisterView]
        OD[OwnerDashboardView]
        SD[SitterDashboardView]
        JD[JobDetailsWindow]
    end
    subgraph SVC["Services (no WPF)"]
        AS[AuthService]
        BS[BookingService]
        VH[ValidationHelper]
        PH[PasswordHasher]
        APP["AppServices - composition root + CurrentUser + Storage"]
        CFG["AppConfig - environment variables over .env"]
        SEL["DatabaseSelector - MySQL first, SQLite fallback"]
    end
    subgraph DATA["Data (provider-neutral DbConnection)"]
        DB["Database - provider, schema + connections"]
        DIA["SqlDialect - the engine-specific SQL"]
        URL["MySqlUrl - DATABASE_URL to connection string"]
        UR[UserRepository]
        PR[PetRepository]
        SPR[SitterProfileRepository]
        BR[BookingRepository]
        CR[ChatRepository]
    end
    subgraph MODELS["Models (POCOs)"]
        M[User, SitterProfile, Pet, Booking, ChatMessage, enums]
    end

    UI --> SVC
    UI --> DATA
    SVC --> DATA
    DATA --> DB
    DATA --> MODELS
    SVC --> MODELS
    CFG --> SEL
    SEL --> URL
    SEL --> DB
    DB --> DIA
    DB -->|"primary"| MySQLServer[("Cloud MySQL 8 server")]
    DB -->|"fallback"| SQLiteFile[(petsitters.db)]
```

- **`App.xaml.cs`** shows `MainWindow` straight away in a "Connecting to the
  database…" state, runs `AppServices.CreateDefault()` on a background thread
  (`Task.Run`, because a cloud connection can take seconds), then calls
  `MainWindow.Attach(services)`. It also installs the global database-error
  handler and closes pooled cloud connections on exit (§5.5).
- **`AppServices`** is a simple composition root. `CreateDefault()` loads the
  settings (`AppConfig`), asks `DatabaseSelector` for a database, then builds the
  repositories on it and exposes them plus `AuthService` and `BookingService`
  (as `BookingActions`), the **`Storage`** selection (which database is in use,
  and why), and the logged-in `CurrentUser` (the session). Tests use
  `new AppServices(database)` with a temporary SQLite file instead.
- **`MainWindow`** hosts a `ContentControl` and swaps `UserControl` views:
  login → register → an owner or sitter dashboard depending on `CurrentUser.Role`.
  `Attach` fills the header's database status (`StorageStatus`, on the right of
  the header before the session bar; the header grid has two columns so it
  doesn't overlap the logo) and then shows the login screen.

---

## 3. Screens

| View | Role | Tabs / purpose |
|------|------|----------------|
| `LoginView` | anyone | Email + password login (asks Owner/Sitter if both accounts match); amber notice when running on the local fallback |
| `RegisterView` | anyone | Create an Owner or Sitter account (personal details incl. location) |
| `OwnerDashboardView` | Owner | **My Details** · **My Pets** · **Find Sitters** (browse + request booking) · **Chats** · hidden **Chat** panel · **My Bookings** (status + cancel) |
| `SitterDashboardView` | Sitter | **My Details** · **My Sitting Profile** (availability, experience, rate…) · **Booking Requests** (accept/decline) · **My Chats** · hidden **Chat** panel |
| `JobDetailsWindow` | Sitter | Pop-up showing a request's full pet + owner details before deciding |

Every screen sits under the `MainWindow` header, which shows the database in
use (§5.1).

---

## 4. Data model

The same five tables exist in whichever store the app chose at launch: the
shared **MySQL** database (primary) or the local SQLite file at
**`%AppData%\PetSitters\petsitters.db`** (fallback). Each is created on first use
by `Database.Initialize()` (safe to call every startup). The diagram uses
logical types; the column types per engine are in §5.3.

```mermaid
erDiagram
    Users ||--o| SitterProfiles : "has (if sitter)"
    Users ||--o{ Pets : owns
    Users ||--o{ Bookings : "requests (owner)"
    Users ||--o{ Bookings : "receives (sitter)"
    Pets  ||--o{ Bookings : "for"
    Bookings ||--o{ ChatMessages : "has"
    Users ||--o{ ChatMessages : sends

    Users {
        int Id PK
        string Email "UNIQUE per Role (Email, Role), case-insensitive"
        string PasswordHash
        string PasswordSalt
        int Role "0=Owner, 1=Sitter"
        string FullName
        string Phone
        string Location
        string ProfileImagePath "local file path, this PC only"
        string CreatedUtc
    }
    SitterProfiles {
        int Id PK
        int UserId FK "UNIQUE (1:1)"
        string Availability
        int ExperienceYears
        string Preferences
        string Qualifications
        decimal DailyRate
        string Bio
    }
    Pets {
        int Id PK
        int OwnerUserId FK
        string Name
        string Species
        string Breed
        int Age "whole years"
        int AgeMonths "optional 0-11"
        string ImagePath "local file path, this PC only"
        string Notes
    }
    Bookings {
        int Id PK
        int OwnerUserId FK
        int SitterUserId FK
        int PetId FK "nullable"
        string StartDate
        string EndDate
        string Message
        int Status "0=Pending,1=Accepted,2=Declined,3=Cancelled"
        decimal DailyRateAtBooking "rate snapshot"
        string CreatedUtc
    }
    ChatMessages {
        int Id PK
        int BookingId FK
        int SenderUserId FK
        string MessageText
        string CreatedUtc
    }
```

Notes:
- **Shared `Users` table** for both roles; role-specific data lives in
  `SitterProfiles` (sitters, 1:1) and `Pets` (owners, 1:many).
- **`DailyRateAtBooking`** snapshots the sitter's rate at request time so the
  owner's estimated cost is stable even if the sitter later changes their rate.
- **Foreign keys are enforced on both engines** (SQLite: `ForeignKeys=True` in
  the connection string; MySQL: named InnoDB constraints), with
  `ON DELETE CASCADE` (and `SET NULL` for a booking's optional pet).
- **Pet age** is stored as whole years (`Age`, required) plus optional months
  (`AgeMonths`, 0–11). `Pet.AgeDisplay` renders the pair for the UI
  (e.g. "2 years 3 months", "5 months").
- Dates are stored as ISO-8601 round-trip strings, on both engines.
- **Images** (`Users.ProfileImagePath`, `Pets.ImagePath`): the picture is copied
  to `%AppData%\PetSitters\UserImages` on the computer that uploaded it, and only
  that local path is stored, so on the cloud database other computers can't
  show it (§5.6).
- Enums live in `Models/Enums.cs`: `UserRole`, `BookingStatus`.

### Schema migrations

**SQLite only.** `CREATE TABLE IF NOT EXISTS` only shapes a *new* database, so
columns added after a release are patched into existing files by
`Database.ApplyMigrations()`, which runs at the end of the SQLite initialisation.
Each step checks `PRAGMA table_info` first and is safe to re-run (e.g.
`Pets.AgeMonths` is added via `ALTER TABLE` when missing, defaulting existing pets
to 0 months). Add new column changes there so existing `petsitters.db` files keep
working.

The MySQL schema started at the current version, so it has no migrations yet.
When it changes, add idempotent steps to `InitializeMySql` that check
`information_schema.COLUMNS` / `STATISTICS` first, the MySQL equivalent of the
`PRAGMA` checks.

### Repositories

One repository per aggregate, each taking the `Database`. They hold **one copy
of each query for both engines**: every value goes through a parameter
(`DbCommandExtensions.AddParameter`, the portable stand-in for SQLite's
`AddWithValue`, which the generic `DbParameterCollection` lacks), and the few
engine-specific fragments come from `Database.Dialect` (§5.3).

| Repository | Key methods |
|------------|-------------|
| `UserRepository` | `EmailExists` (any role / per role), `Insert`, `UpdateDetails`, `FindByEmail`, `FindAllByEmail`, `FindById`, `GetByRole`, `GetSittersWithProfiles` (one JOIN, §5.8) |
| `PetRepository` | `Insert`, `Delete`, `GetByOwner` |
| `SitterProfileRepository` | `GetByUserId`, `Upsert` (insert-or-update, 1:1) |
| `BookingRepository` | `Insert`, `UpdateStatus`, `GetForOwner`, `GetForSitter`, `GetById`, `GetDetailsForOwner` / `GetDetailsForSitter` (booking + owner + sitter + pet in one JOIN, §5.8) |
| `ChatRepository` | `Insert`, `GetForBooking` (chronological, per-booking scoped), `GetForBookingWithSenderNames` (with sender names, one JOIN, §5.8) |

---

## 5. Storage: cloud database with local fallback

**REQ-GR-09 (proposed requirement ID, to be added to the report): shared cloud
database with local fallback at launch.** The app uses the MySQL server when it
can, and the local SQLite file when it can't. The decision is made **once per
launch** by `Services/DatabaseSelector.cs`; nothing switches or syncs afterwards.

### 5.1 Database selection at startup

```mermaid
flowchart TD
    START(["PetSitters.exe launched"]) --> SHOW["UI thread: MainWindow.Show()<br/>header 'Connecting…', body 'Connecting to the database…'"]
    SHOW --> BG["Background thread (Task.Run): AppServices.CreateDefault()<br/>AppConfig.Load: environment variables override the .env beside the exe"]
    BG --> FORCED{"PETSITTERS_DB = sqlite?"}
    FORCED -- yes --> LOCAL["Local SQLite<br/>'Local database'"]
    FORCED -- no --> URLSET{"DATABASE_URL set?"}
    URLSET -- no --> LOCAL
    URLSET -- yes --> TRY["MySqlUrl.ToConnectionString, Database.ForMySql<br/>Initialize(): connect (TLS + login), check tables, create any missing"]
    TRY -- success --> CLOUD["MySQL<br/>'☁ Cloud database'"]
    TRY -- "couldn't connect" --> OFF["Local SQLite<br/>'⚠ Offline: local database'"]
    TRY -- "connected, schema step failed" --> ERR["Local SQLite<br/>'⚠ Cloud database error: local database'"]
    LOCAL --> ATTACH["UI thread: MainWindow.Attach(services)<br/>header status + tooltip"]
    CLOUD --> ATTACH
    OFF --> ATTACH
    ERR --> ATTACH
    ATTACH --> LOGIN["LoginView<br/>amber notice if the app fell back"]
```

Connecting and creating the schema **is** the reachability check: there is no
separate ping. `Database.Initialize()` runs its work once per instance, so when
`AppServices` calls it again on the chosen database it costs nothing.

| Situation | Header (`StorageStatus`) | Store |
|-----------|--------------------------|-------|
| `PETSITTERS_DB=sqlite` (or `local`) | Local database | SQLite |
| No `DATABASE_URL` | Local database | SQLite |
| Connected and schema ready | **☁ Cloud database** | MySQL |
| Couldn't connect: offline, DNS, timeout, refused, wrong password, unknown database, malformed URL | **⚠ Offline: local database** (warning colour) | SQLite |
| Connected, but the schema step failed | **⚠ Cloud database error: local database** (warning colour) | SQLite |

The two fallback cases are told apart on purpose, so a real database error isn't
passed off as being offline. The header's tooltip explains the choice in fixed
wording plus an error code where one exists (e.g. "the server couldn't be
reached (offline, blocked, or timed out)", or "MySQL error 1064 (ParseError)").
It **never** shows the host name, URL, user name or password: raw driver
messages are not displayed because they can name the server and the user, and
the text is also run through `DatabaseSelector.Redact` (URL and password, raw
and percent-decoded) in case a future message echoes them. On a fallback the
login screen also shows an amber notice: cloud accounts won't work here and
nothing created will be shared. **Restart the app to try the cloud database
again.**

**Measured:** the cloud header appeared about **2.4 s** after launch. In the UI
test, with an unreachable cloud URL and a 3 s connect timeout, the app opened on
the local database in about **7 s**. The default timeout is 8 s, so a real
offline launch waits a little longer; the worst case is a server that accepts
the connection and then stalls, which costs the connect timeout plus the 10 s
command timeout.

### 5.2 Configuration

Settings are read once at launch by `Services/AppConfig.cs`.

| Key | Values | Default | Effect |
|-----|--------|---------|--------|
| `DATABASE_URL` | `mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED` | unset | The cloud database. Unset or blank = local database only. Port defaults to 3306; percent-encode special characters in the password (`@` → `%40`, `:` → `%3A`, `#` → `%23`). `ssl-mode`: `REQUIRED` (default), `VERIFY_CA`, `VERIFY_IDENTITY`, `PREFERRED`, `DISABLED`. |
| `PETSITTERS_DB` | `auto`, `sqlite` (alias `local`) | `auto` | `sqlite` always uses the local database and skips MySQL entirely. Any other value, including a typo, means `auto`. |
| `DB_CONNECT_TIMEOUT_SECONDS` | 1–60 | 8 | How long the launch-time connection may take before falling back. Out of range or not a number = 8. |

**Precedence:** a real environment variable (when non-empty) overrides the
`.env` file, which overrides the defaults. This is how the UI tests, CI or a
shell can force the local database without editing any file.

**`.env` format:** `KEY=value` lines; blank lines and `#` comments are ignored;
an optional leading `export ` is allowed; the value may be wrapped in single or
double quotes; everything after the first `=` is the value (URLs and passwords
may contain `=`); a line that isn't `KEY=value` is skipped rather than failing
the launch. A missing `.env` simply means no settings (local database only).

**How `.env` reaches the app:** the app reads `.env` from **its own folder**
(beside `PetSitters.exe`). `PetSitters.csproj` copies the repo-root `.env` there
on every build (`bin\Debug` or `bin\Release`), but **only if it exists**
(`Condition="Exists('.env')"`, `PreserveNewest`, hidden in Solution Explorer), so
CI and fresh clones build without it. The `RemoveStaleDotEnv` target deletes the
copy in the output folder once the repo-root `.env` has been deleted, so the app
can't keep using the cloud database from a stale file. In Visual Studio the
condition is evaluated when the project loads: if `.env` is created while the
solution is open, reload the project (or reopen the solution) and rebuild.
Likewise after deleting `.env` while the solution is open: reload first, or the
build may fail trying to copy a file that no longer exists (inferred from how
the condition is evaluated; not tested).

**Keeping the secret out of the repository** (the GitHub repository is public):
- `.gitignore` excludes `.env` and `.env.*`, except `.env.example`, which is
  committed with placeholders only and documents every key.
- CI **Gate 0 "No secrets committed"** fails the build if any `.env` or `.env.*`
  file other than `.env.example` is tracked (see `docs/CI.md`).
- CI never connects to MySQL: the checkout has no `.env`, no secret is
  configured, and the `MySql` test category is excluded. **Do not** add the
  production `DATABASE_URL` as a repository secret: the repository, its workflow
  logs and its artefacts are public.
- **Warning:** after a build, `bin\Debug\.env` (or `bin\Release\.env`) holds the
  password in plain text. Never zip or share the repo folder or `bin\` while
  `.env` is in it. GitHub's "Download ZIP" and `git archive` only contain tracked
  files, so they exclude it.

Step-by-step setup is in §8.

### 5.3 MySQL schema vs SQLite

`Database.InitializeMySql` creates the same five tables, columns and keys as
the SQLite schema, in MySQL's types:

| Aspect | SQLite (local) | MySQL (cloud) | Why |
|--------|----------------|---------------|-----|
| Primary keys | `INTEGER PRIMARY KEY AUTOINCREMENT` | `INT NOT NULL AUTO_INCREMENT PRIMARY KEY` | The server requires a primary key on every table. |
| `Email` | `TEXT COLLATE NOCASE` | `VARCHAR(255)` | MySQL can't put a UNIQUE key on `TEXT`. `ValidationHelper.IsValidEmail` caps emails at 254 characters (the RFC maximum), so a valid email always fits and STRICT mode never rejects one; boundary-tested at 253/254/255. |
| Email + role uniqueness (REQ-GR-06) | `UNIQUE (Email, Role)` | `UNIQUE KEY UX_Users_Email_Role (Email, Role)` | Same rule on both engines. |
| Case and accents | `NOCASE` on `Email`; `COLLATE NOCASE` in case-insensitive `ORDER BY` | Table collation `utf8mb4_0900_as_ci` | Case-insensitive like SQLite's `NOCASE`, accent-sensitive like SQLite, so "jose@" and "josé@" stay different on both. One difference: MySQL also folds non-ASCII case (É/é), while `NOCASE` folds ASCII letters only. |
| Multi-line text (`Notes`, `Bio`, `Availability`, `Preferences`, `Qualifications`, `Message`, `MessageText`) | `TEXT` | `MEDIUMTEXT` (16 MB) | The server runs in STRICT mode, which **rejects** over-long values instead of truncating them, and the UI sets no length limits, so a pasted wall of text must still fit. |
| Other text | `TEXT` | `TEXT` (64 KB); hash and salt `VARCHAR(255)` | |
| Money (`DailyRate`, `DailyRateAtBooking`) | `REAL` | `DECIMAL(10,2)` | No floating point for money on the server. |
| Dates | ISO-8601 round-trip strings in `TEXT` | The same strings in `VARCHAR(40)` | Both engines share the repositories' read/write code, and `ORDER BY` on them still sorts chronologically. |
| Foreign keys | `FOREIGN KEY …` (enabled by `ForeignKeys=True`) | Named constraints (`FK_Pets_Owner`, …) | Same `CASCADE` / `SET NULL` rules. |
| Engine / character set | — | InnoDB, `utf8mb4` | InnoDB enforces foreign keys; `utf8mb4` is full Unicode. |
| Creating the schema | `CREATE TABLE IF NOT EXISTS` on every launch, then `ApplyMigrations` | One `information_schema` count; only if any of the five tables is missing, all `CREATE TABLE IF NOT EXISTS` statements in **one** command | One round trip on a normal launch. It also lets a least-privilege user without `CREATE` run the app once the tables exist (MySQL checks the privilege even for `IF NOT EXISTS`). |
| Migrations | `ApplyMigrations` (PRAGMA-based column adds, REQ-GR-06 `Users` rebuild) | None yet | See "Schema migrations" in §4. |

All SQL is written to work under the server's **`ANSI_QUOTES`** mode: string
literals use single quotes only, because there `"x"` means an identifier. Table
names are case-sensitive on the server, so queries use the schema's exact casing.

`Data/SqlDialect.cs` holds **the only engine-specific SQL**:

| Need | SQLite | MySQL |
|------|--------|-------|
| The new row's id after an `INSERT` (same command, scoped to the connection) | `SELECT last_insert_rowid();` | `SELECT LAST_INSERT_ID();` |
| Case-insensitive `ORDER BY` | `Name COLLATE NOCASE` | `Name` (the collation is already case-insensitive) |
| Upsert (`SitterProfileRepository.Upsert`, keyed on `UserId`) | `ON CONFLICT(UserId) DO UPDATE SET X = excluded.X` | `AS new ON DUPLICATE KEY UPDATE X = new.X` (the 8.0.19+ row-alias form; the older `VALUES()` form is deprecated) |

Any new engine-specific fragment belongs in `SqlDialect`, never inline in a
repository.

### 5.4 Connection pool settings

`Data/MySqlUrl.cs` turns `DATABASE_URL` into a MySqlConnector connection string
(percent-decoding the user name, password and database; its error messages are
fixed text that never contains the URL). It also sets the pool, tuned by
measurement against the cloud server:

| Setting | Value | Reason |
|---------|-------|--------|
| `SslMode` | From the URL; `REQUIRED` if absent | Encrypt by default, rather than MySQL's own default of `PREFERRED`: the app sends passwords and personal details over the internet. |
| `ConnectionTimeout` | `DB_CONNECT_TIMEOUT_SECONDS` (default 8 s) | A new TLS connection measured about **1.2–1.9 s**, so 8 s leaves headroom without making an offline launch wait too long. |
| `DefaultCommandTimeout` | 10 s | Queries run on the UI thread, so a stalled one must give up well before the driver's 30 s default. |
| `MaximumPoolSize` | 5 | The server's connection limit is small and shared with other services on the same server, so each running copy of the app stays a small client. |
| `MinimumPoolSize` | 1 | Keeps one connection open and warm: without it, the first click after a pause would pay the 1.2–1.9 s TLS handshake. |
| `ConnectionIdleTimeout` | 600 s | Idle connections above the minimum are closed after 10 minutes. |
| `ConnectionReset` | `false` | **Measured:** 392 ms per pooled open + query with the reset, 195 ms without, so skipping it halves the cost of every database call. Safe because the app sets no session state (variables, temporary tables, open transactions) that a reset would need to clear. |
| `Keepalive` | 60 s | TCP keepalive, so home routers don't silently drop the idle pooled connection. |
| `CharacterSet` | `utf8mb4` | Matches the table character set. |

### 5.5 Failure handling

| When | What happens |
|------|--------------|
| **At launch, the cloud database fails** | Always falls back to the local database (§5.1). `DatabaseSelector.IsConnectivityFailure` decides the label: a bad URL (`FormatException`), socket, timeout, TLS (`AuthenticationException`) or I/O error, or a MySQL "unable to connect", "access denied" or "unknown database" error means **Offline**; anything else means **Cloud database error**. |
| **At launch, the local database can't be opened either** (e.g. `%AppData%` not writable) | Nothing is left to fall back to: a message box says the local database couldn't be opened (exception type name only) and the app exits with code 1. |
| **Mid-session, a database call throws** | `App.DispatcherUnhandledException` turns any exception whose chain contains a `DbException`, `SocketException` or `TimeoutException` into a message instead of a crash, and the app keeps running. Two kinds: connectivity or transient failures (`IsConnectivityFailure`, or MySqlConnector's `IsTransient`) say the app **"couldn't reach its database"** and the last action may not have been saved (on the cloud database: check the connection, and restart to fall back to local if it stays down); data or schema errors say **"The database couldn't save this change"** and give a code to report, e.g. `MySQL 1406 (DataTooLong)` or `SQLite Constraint`. Exception text is never shown, because it can include server and user names. Non-database exceptions are left alone, as before. |
| **On exit** | When on the cloud database, `MySqlConnection.ClearAllPools()` closes the pooled connections politely (a protocol "quit" per session), so the shared server doesn't log an aborted connection every time someone closes the app. |

### 5.6 Security & limitations

This is a **2-tier** design: each copy of the app talks to the database
directly, with no server of our own in between. That keeps the prototype to
one deployable program, but it has consequences a production system would not
accept, and they are stated here rather than hidden.

**Security trade-offs**

1. **Every copy of the app holds the database credential in plain text.** The
   `.env` beside `PetSitters.exe` contains the user name and password; anyone
   with the build folder has them.
2. **Authorisation is enforced only in the client.** Only-the-owner-can-cancel
   (REQ-PO-07), chat scoped to a booking's two parties, the sitter-overlap rule
   (REQ-GR-08), the REQ-GR-04 booking validation and the one-account-per-role
   check in `AuthService` are all C# in the app. Anyone with the `.env` can
   connect with any MySQL client and bypass them: read or change every row,
   including other users' contact details and password hashes (salted PBKDF2,
   so not plaintext, but open to offline guessing). The database itself still
   enforces the keys, `NOT NULL`, the `UNIQUE (Email, Role)` rule, foreign keys
   and STRICT-mode lengths.
3. **TLS encrypts, but by default doesn't verify the server.** `ssl-mode=REQUIRED`
   refuses an unencrypted connection but doesn't check the server's
   certificate, so someone able to intercept the network path could
   impersonate the server. `VERIFY_CA` / `VERIFY_IDENTITY` check it, but (inferred
   from the driver's documented behaviour, not yet tried against the cloud
   server) need the server's CA certificate to be trusted on each PC, because
   `DATABASE_URL` has no option for a CA file yet.

**Recommended mitigations** (configuration only, no code changes):
- A **dedicated database** for this app and a **least-privilege user** with only
  `SELECT`, `INSERT`, `UPDATE`, `DELETE` on that one database. Create the tables
  once with an administrative account; after that the existence check means the
  app never issues `CREATE`. A leaked `.env` then exposes this app's data only.
- Restrict which IP addresses can reach the server with the provider's
  **trusted sources** (allow-list) setting.
- **Rotate the password** after the assignment is marked, and whenever a `.env`
  may have leaked.
- Use **`ssl-mode=VERIFY_CA`** once the CA certificate is trusted on the PCs that
  run the app.
- The structural fix is a 3-tier design (a small web API that holds the
  credential and enforces authorisation on the server, with the desktop app as
  its client). It was not attempted for this prototype.

**Functional limitations**
- **No sync, and no migration.** The choice is made once per launch. Anything
  saved on the local fallback stays on that computer and is never uploaded
  later; data that was in the local database before the cloud database was
  introduced was not migrated to it. Accounts in one store don't exist in the
  other, hence the login screen's notice.
- **No switch mid-session.** If the cloud connection drops after launch, the app
  stays on the cloud database and actions fail with the "couldn't reach its
  database" message until it comes back or the app is restarted.
- **Images are local-only.** Profile and pet pictures are copied to
  `%AppData%\PetSitters\UserImages` on the uploading PC and only that path is
  stored, so on the cloud database other PCs don't see them: the "pet images on
  the sitter side" feature only works when owner and sitter use the same PC.
- **Latency.** Each query to the cloud server costs about **195 ms** (measured,
  pooled), and queries run on the UI thread, so every click that touches the
  database pauses for its round trips. Lists are loaded with one query each (see
  "Performance on the cloud database" below), which keeps that to about a second.
- **Notice wording.** The login screen's notice starts with "Offline" in both
  fallback cases, including "Cloud database error"; the header and tooltip give
  the accurate reason.

### 5.7 How this is verified

- **`DatabaseConfigurationTests`**: `.env` parsing, environment-over-file
  precedence, `PETSITTERS_DB` modes, timeout bounds, URL parsing (percent-encoding,
  default port, every `ssl-mode`), and a Security test that a malformed URL
  fails **without** the password in the error.
- **`DatabaseSelectorTests`**: cloud reachable; a real refused connection falls
  back as Offline; an error after connecting is reported as an error, not
  Offline; the password and URL never appear in the tooltip; malformed URL;
  forced local; no URL; connectivity classification; `Redact`.
- **MySQL parity (`MySqlParityTests.cs`)**: nine `*_MySql` test classes inherit
  **every** test of the SQLite repository and service classes and swap only the
  database (`DatabaseTestBase.CreateDatabase()` is virtual). 95 executed cases,
  all passed against the real cloud server (about 6 minutes). Strictly opt-in;
  each run uses its own throwaway database and never touches the app's (§8).
- **UI tests**: `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo`
  (an unreachable `DATABASE_URL` opens on the local database, says so in the
  header, and registration still works) and
  `Startup_LocalDatabaseChosen_HeaderSaysLocalDatabase`.

Counts and the per-test list are in `docs/UnitTests.md`.

### 5.8 Performance on the cloud database

Every query to the cloud server is a network round trip of about **195 ms**,
and queries run on the UI thread, so a screen's speed is decided by how MANY
queries it makes. The original dashboards looked related rows up one at a time:
the sitter of each booking, the pet list again for each booking, the profile of
each sitter, the sender of each chat message. On SQLite that was invisible; on
the cloud database it froze the window for seconds, long enough that UI
Automation's own calls timed out while measuring it.

**Fix: one query per list.** The repositories gained JOIN loaders
(`GetDetailsForOwner` / `GetDetailsForSitter` return `BookingDetails`: a booking
plus its owner, sitter and pet; `GetSittersWithProfiles` returns
`SitterListing`s; `GetForBookingWithSenderNames` fills `ChatMessage.SenderName`).
Joined tables' columns are aliased with a prefix (`o_`, `s_`, `p_`, `u_`, `sp_`)
so one row maps to several objects, and joined users never include password
hashes. The views build their lists from these, derive the Chats tab from the
same result as the bookings, check chat participation with a single
`GetById`, and reuse the owner's already-loaded pet list for the booking form.
`BookingService.AcceptRequest` uses one query for both its guard and its
overlap check.

**A listener leak, also fixed.** Each dashboard subscribed to
`BookingRepository.BookingStatusChanged` and never unsubscribed, so every past
login's dashboard kept reloading its lists (for the current user) on every
status change: the more logins in a session, the slower each Accept. The
handler is now detached on `Unloaded`, and the explicit reloads it duplicated
after a status change were removed.

**Measured** in the real app with a temporary UI-automation harness, against a
throwaway database on the cloud server seeded with realistic data, before and
after the change (same data, same machine, 2026-10-07):

| Action (real app, cloud database, 10 sitters / 10 bookings / 20-message chat) | Before | After |
|---|--:|--:|
| Owner: log in until the dashboard responds | 11.0 s | 1.2 s |
| Sitter: log in until the dashboard responds | 4.9 s | 0.9 s |
| Open a 20-message chat | 4.5 s | 0.6 s |
| Sitter: accept a request (until its chat is open) | 14.3 s | 1.2 s |
| Owner: select a sitter in Find Sitters | 0.47 s | 0.28 s |
| Send a chat message | 0.93 s | 0.74 s |

The structural cause is guarded by tests: `Database.ConnectionsOpened` counts
round trips, and repository tests assert each loader is ONE query however many
rows there are (they also run against MySQL in the parity suite). The screen
timings themselves are not an automated test.

---

## 6. Key workflows

### Account creation & login (FR-A1 / FR-A2)

`AuthService.Register` validates input — **all registration fields are required**:
a valid email, a password of at least 6 characters, full name, phone, and
location. It then rejects a duplicate email **for the same role**
(case-insensitive), hashes the password, and inserts the user. `AuthService.Login`
verifies the password hash and returns the same error for "unknown email" and
"wrong password" (no user enumeration). Both return an `AuthResult`
(`Success` / `ErrorMessage` / `User` / `RequiresRoleChoice`).

**REQ-GR-06, one account per email per role.** The same email can hold one Owner
*and* one Sitter account (separate users, ids, pets and bookings), but never two
of the same role. The rule is enforced twice: by `AuthService.Register` (a
friendly "An owner/A sitter account with that email already exists" warning) and
by the database's `UNIQUE (Email, Role)` constraint (on both engines). At login,
only accounts whose password matches are considered, so different passwords
select the account by themselves. If the password matches both, the result has
`RequiresRoleChoice` set, and `LoginView` reveals an Owner/Sitter choice and
retries with that role. The "which role?" prompt only appears after a correct
password, so it reveals nothing about which emails are registered.

**Migration (SQLite).** Databases created before REQ-GR-06 had `UNIQUE` on
`Email` alone. `Database.ApplyMigrations` detects that index and rebuilds `Users`
using SQLite's documented table-rebuild procedure, keeping every row and id.
Foreign keys are switched **off** for the rebuild; otherwise `DROP TABLE Users`
would fire the `ON DELETE CASCADE` rules and wipe every pet, booking, profile and
chat message. `DatabaseMigrationTests` guards this, and it was confirmed to fail
when that safeguard is removed. The MySQL schema was created with
`(Email, Role)` from the start, so it never needs this step.

### Owner requests a booking (FR-O4)

```mermaid
sequenceDiagram
    actor Owner
    participant OD as OwnerDashboardView
    participant BS as BookingService
    participant BR as BookingRepository
    participant DB as Database (MySQL or SQLite)

    Owner->>OD: Find Sitters → pick sitter, pet, dates
    OD->>BS: RequestBooking(Booking, rate snapshot)
    BS->>BS: ValidateRequest (REQ-GR-04: dates, 1 h–14 days, pet selected)
    BS->>BR: GetForOwner → any live booking for the same pet overlapping?
    alt invalid (REQ-GR-04)
        BS-->>OD: Fail(specific message, e.g. "Start date cannot be in the past.")
    else clash (REQ-PO-08)
        BS-->>OD: Fail("This pet already has a … booking …")
    else no clash
        BS->>BR: Insert(status=Pending)
        BR->>DB: INSERT INTO Bookings
        OD-->>Owner: "Request sent" + estimated total (nights × rate)
    end
```

**REQ-GR-04:** `BookingService.ValidateRequest` checks, in order: the start is not
before today; the end is after the start; the duration is at least **1 hour**
and at most **14 days** (exactly 14 is allowed); and a pet is selected (one of
*this owner's* pets, or "All my pets" provided they have at least one). Each rule
has its own message, and nothing is saved when one fails. The form only captures
**dates**, so "in the past" is judged by day (a booking starting today is allowed)
and the 1-hour minimum can't be reached from the form; both are written to also
hold if time pickers are added later. The service takes an injectable clock so
tests don't depend on the current date. These checks used to live in the form's
code-behind; they were moved here so they apply to every caller and can be
unit-tested.

**REQ-PO-08:** a pet cannot have two *live* (pending or accepted) bookings over
overlapping dates, with the same sitter or different ones. "All my pets"
(`PetId` null) counts as every pet, so it clashes with any of that owner's
bookings (`Booking.SharesPetWith`). Declined and cancelled bookings free the pet.
A pending request blocks too, so to switch sitters the owner cancels it first.

### Owner cancels a booking (REQ-PO-07)

**My Bookings → Cancel booking** asks for confirmation, then calls
`BookingService.CancelBooking`. Only the booking's owner can cancel, and only
from Pending or Accepted. The row is kept with status **Cancelled**, so it
stays on the owner's list but leaves the sitter's request queue and both
parties' chat lists. If its chat is open, the owner's Chat tab is closed. Both
dashboards' **Send** re-checks that the booking is still Accepted, so a chat
left open after a cancellation cannot keep receiving messages.

### Sitter responds; both parties chat (FR-S4 / FR-S5 / FR-O5)

The sitter sees pending requests under **Booking Requests**, can open
**View details** (`JobDetailsWindow`), and **Accept**/**Decline** (updates
`Bookings.Status`). Accepted bookings appear under the sitter's **My Chats** and
the owner's **Chats**; opening one shows the per-booking **Chat** panel backed by
`ChatRepository`, so either party can message the other. On the cloud database
the two can be on different computers; on the local database both must use the
same one.

**Accept** goes through `BookingService.AcceptRequest` rather than the repository
directly, which enforces **REQ-GR-08**: a sitter cannot accept a request whose
dates overlap a booking they have already *accepted*. Ranges are half-open
`[StartDate, EndDate)` (`Booking.Overlaps`), so a booking handed back on the 13th
does not clash with one starting on the 13th. Pending, declined or cancelled
bookings never block. A refused request is left **pending** and the reason is
shown in the request panel; the list is deliberately not reloaded, so the
message stays visible. Owners may still *send* overlapping requests: the check
is applied only on acceptance.

---

## 7. Security & quality properties

| Property | How it is achieved |
|----------|--------------------|
| No plaintext passwords | PBKDF2 salted hashing in `PasswordHasher`; only hash+salt stored |
| No account enumeration | Identical login failure message for unknown email vs wrong password |
| SQL injection resistance | All queries use parameterised commands (`AddParameter`) on both engines; the only text joined into SQL is hard-coded (table names, `SqlDialect` fragments) |
| Chat privacy | Messages scoped to a booking; retrieval is per-`BookingId` (enforced by the app, not the database: §5.6) |
| Referential integrity | Foreign keys with cascade rules on both engines (SQLite `ForeignKeys=True`; MySQL InnoDB constraints) |
| Encryption in transit | Cloud connections use TLS (`ssl-mode=REQUIRED` by default); the certificate is only verified with `VERIFY_CA` / `VERIFY_IDENTITY` (§5.6) |
| Database credential never displayed | Header tooltip and error messages use fixed wording plus error codes; `MySqlUrl` errors never echo the URL; `DatabaseSelector.Redact` as a backstop; covered by Security-category tests |
| Secrets kept out of the public repository | `.env` git-ignored; `.env.example` holds placeholders only; CI Gate 0 fails if any other `.env*` file is tracked (§5.2) |
| Availability | Local fallback at launch; database errors mid-session become messages, not crashes (§5.5) |
| Testability | Logic layer has no WPF dependency; `Database` path is injectable; `DatabaseSelector` takes injectable constructors; `DatabaseTestBase.CreateDatabase()` is virtual, so the MySQL parity classes re-run the suites on MySQL |

The 2-tier design's limits (credential on every client, client-side-only
authorisation) are set out in §5.6. See `docs/UnitTests.md` for the test suite
that verifies these.

---

## 8. Building & running

The main app is a **classic WPF project that `dotnet build` cannot compile** — use
Visual Studio MSBuild. The SDK-style test projects build with `dotnet` afterwards.

**Cloud database (optional, one-time):** without this step the app simply uses
the local database.
1. In the repo root (next to `PetSitters.csproj`), copy `.env.example` to `.env`.
2. Set `DATABASE_URL=mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED`
   in `.env`. The real value is shared privately; never commit it.
3. Build (Visual Studio or MSBuild). The build copies `.env` next to
   `PetSitters.exe`. In Visual Studio, if you created `.env` while the solution
   was open, reload the project and rebuild.
4. Run the app: the header should read **☁ Cloud database**. If it says
   **⚠ Offline: local database**, hover over it for the reason.

```bash
# Build the app (Visual Studio MSBuild); also runs the smoke + full logic suites (MySQL tests excluded)
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" PetSitters.csproj /t:Restore,Build /p:Configuration=Debug

# Run
bin\Debug\PetSitters.exe

# Logic tests (after the app is built). Runs on SQLite; MySQL parity tests report Skipped.
dotnet test PetSitters.Tests -c Debug
```

```powershell
# Run on the local database for one session, even with .env present (environment beats .env)
$env:PETSITTERS_DB = "sqlite"; .\bin\Debug\PetSitters.exe

# MySQL parity tests (opt-in; about 6 minutes over the internet). Either give a server
# where databases can be created (the database part of the URL is ignored) ...
$env:PETSITTERS_TEST_MYSQL_URL = "mysql://USER:PASSWORD@HOST:PORT/DATABASE?ssl-mode=REQUIRED"
# ... or reuse the server from DATABASE_URL / the repo-root .env:
$env:PETSITTERS_TEST_MYSQL = "1"
dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql
```

Each parity run creates its own database (`sitters4us_test_<UTC yyyyMMddTHHmm>_<8 hex>`),
empties its tables before each test, and drops it when the run ends; leftovers
from a crashed run are dropped by the next run once they are over 6 hours old.
It never touches the app's own tables. The build gate and CI exclude the `MySql`
category (`TestCategory!=MySql`); see `docs/CI.md`.

The FlaUI UI suite launches the app with `PETSITTERS_DB=sqlite`, so it never
touches the shared cloud database and its data wipe still resets everything.

Or open `PetSitters.sln` in Visual Studio and use **Test → Run All Tests** (the
MySQL parity tests show as Skipped unless opted in as above).

**Reset all data:**
- Local database: delete `%AppData%\PetSitters\petsitters.db` and relaunch.
- Cloud database: its data is shared with everyone using the app, so don't wipe
  it to test something. Use `PETSITTERS_DB=sqlite`, or the parity tests'
  throwaway database, instead.

---

## 9. Extending the app

- **New screen:** add a `UserControl` under `Views/`, register it in
  `PetSitters.csproj` (`<Page>` + `<Compile>` — classic projects don't auto-include),
  and navigate to it from `MainWindow`.
- **New persisted data:** add a POCO in `Models/`; a `CREATE TABLE IF NOT EXISTS`
  in **both** `InitializeSqlite` and `InitializeMySql` (MySQL types per §5.3,
  with a primary key, and the table name added to the `information_schema`
  existence check); and a repository in `Data/` that uses `AddParameter` and
  `Database.Dialect`. Expose it from `AppServices`. A column added to an existing
  table needs a migration step for each engine (§4, "Schema migrations").
- **New engine-specific SQL:** add it to `SqlDialect`, never inline in a
  repository, and keep string literals single-quoted (`ANSI_QUOTES`).
- **New logic:** put it in `Services/` (UI-independent) and add tests in
  `PetSitters.Tests` so it stays verifiable. For a new database test class, add
  a `_MySql` subclass in `MySqlParityTests.cs` so it also runs on MySQL.
- **New setting:** add the key to `AppConfig` (and to the environment-override
  list in `AppConfig.Load`), document it in `.env.example` and §5.2, and test it
  in `DatabaseConfigurationTests`.
