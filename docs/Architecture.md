# Sitters4Us — Application Architecture & Design

Technical documentation for the Sitters4Us prototype: what it does, how it is
structured, its data model, and how the main workflows run.

> Product/brand name: **Sitters4Us**. Visual Studio assembly & namespace:
> **`PetSitters`**.

---

## 1. Overview

Sitters4Us connects **pet owners** with **pet sitters**. Owners register their
pets and browse sitters; sitters advertise availability, experience, and a daily
rate; owners send booking requests that sitters accept or decline; once a booking
is accepted the two parties can chat.

It is a single-user **desktop application** (WPF) storing all data locally in a
**SQLite** file — no server or internet connection is required.

### Technology stack

| Concern | Choice |
|---------|--------|
| Language / runtime | C# on .NET Framework 4.7.2 |
| UI | WPF (XAML), classic (non-SDK) project |
| Persistence | SQLite via `System.Data.SQLite.Core` (NuGet) |
| Password hashing | PBKDF2 (`Rfc2898DeriveBytes`, SHA-256, 100k iterations) |
| Tests | MSTest; FlaUI for UI automation |

---

## 2. Solution structure

```
PetSitters.sln
├── PetSitters/                 The WPF application (assembly "PetSitters")
│   ├── App.xaml(.cs)           Startup: builds AppServices, opens MainWindow
│   ├── MainWindow.xaml(.cs)    Shell: top bar + swaps the active view
│   ├── Models/                 Plain data objects (POCOs)
│   ├── Data/                   SQLite: Database + one repository per table
│   ├── Services/               UI-independent logic (auth, validation, hashing)
│   └── Views/                  One WPF UserControl per screen
├── PetSitters.Tests/           Logic + integration tests (MSTest)  → docs/UnitTests.md
└── PetSitters.UiTests/         End-to-end UI automation (FlaUI + MSTest)
```

### Layered architecture

The golden rule: **business/data logic never depends on WPF**, so it can be
tested without a window.

```mermaid
flowchart TD
    subgraph UI["Views (WPF)"]
        MW[MainWindow shell]
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
        APP[AppServices - composition root + CurrentUser]
    end
    subgraph DATA["Data (SQLite)"]
        DB[Database - schema + connections]
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
    DB --> SQLiteFile[(petsitters.db)]
```

- **`App.xaml.cs`** creates `AppServices.CreateDefault()` once at startup and
  injects it into `MainWindow`.
- **`AppServices`** is a simple composition root: it builds the `Database` and the
  repositories, exposes them plus `AuthService` and `BookingService`
  (as `BookingActions`), and holds the logged-in
  `CurrentUser` (the session).
- **`MainWindow`** hosts a `ContentControl` and swaps `UserControl` views:
  login → register → an owner or sitter dashboard depending on `CurrentUser.Role`.

---

## 3. Screens

| View | Role | Tabs / purpose |
|------|------|----------------|
| `LoginView` | anyone | Email + password login (asks Owner/Sitter if both accounts match) |
| `RegisterView` | anyone | Create an Owner or Sitter account (personal details incl. location) |
| `OwnerDashboardView` | Owner | **My Details** · **My Pets** · **Find Sitters** (browse + request booking) · **My Bookings** (status + cancel) |
| `SitterDashboardView` | Sitter | **My Details** · **My Sitting Profile** (availability, experience, rate…) · **Booking Requests** (accept/decline) · **My Chats** · hidden **Chat** panel |
| `JobDetailsWindow` | Sitter | Pop-up showing a request's full pet + owner details before deciding |

---

## 4. Data model

All data lives in one SQLite file at **`%AppData%\PetSitters\petsitters.db`**,
created on first run by `Database.Initialize()` (safe to call every startup).

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
        string CreatedUtc
    }
    SitterProfiles {
        int Id PK
        int UserId FK "UNIQUE (1:1)"
        string Availability
        int ExperienceYears
        string Preferences
        string Qualifications
        real DailyRate
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
        real DailyRateAtBooking "rate snapshot"
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
- **Foreign keys are enforced** (`ForeignKeys=True`), with `ON DELETE CASCADE`
  (and `SET NULL` for a booking's optional pet).
- **Pet age** is stored as whole years (`Age`, required) plus optional months
  (`AgeMonths`, 0–11). `Pet.AgeDisplay` renders the pair for the UI
  (e.g. "2 years 3 months", "5 months").
- Dates are stored as ISO-8601 round-trip strings.

### Schema migrations

`CREATE TABLE IF NOT EXISTS` only shapes a *new* database, so columns added after
a release are patched into existing files by `Database.ApplyMigrations()`, which
runs at the end of `Initialize()`. Each step checks `PRAGMA table_info` first and
is safe to re-run (e.g. `Pets.AgeMonths` is added via `ALTER TABLE` when missing,
defaulting existing pets to 0 months). Add new column changes there so existing
`petsitters.db` files keep working.
- Enums live in `Models/Enums.cs`: `UserRole`, `BookingStatus`.

### Repositories

One repository per aggregate, each taking the `Database`:

| Repository | Key methods |
|------------|-------------|
| `UserRepository` | `EmailExists` (any role / per role), `Insert`, `UpdateDetails`, `FindByEmail`, `FindAllByEmail`, `FindById`, `GetByRole` |
| `PetRepository` | `Insert`, `Delete`, `GetByOwner` |
| `SitterProfileRepository` | `GetByUserId`, `Upsert` (insert-or-update, 1:1) |
| `BookingRepository` | `Insert`, `UpdateStatus`, `GetForOwner`, `GetForSitter`, `GetById` |
| `ChatRepository` | `Insert`, `GetForBooking` (chronological, per-booking scoped) |

---

## 5. Key workflows

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
by the database's `UNIQUE (Email, Role)` constraint. At login, only accounts
whose password matches are considered, so different passwords select the account
by themselves. If the password matches both, the result has
`RequiresRoleChoice` set, and `LoginView` reveals an Owner/Sitter choice and
retries with that role. The "which role?" prompt only appears after a correct
password, so it reveals nothing about which emails are registered.

**Migration.** Databases created before REQ-GR-06 had `UNIQUE` on `Email`
alone. `Database.ApplyMigrations` detects that index and rebuilds `Users`
using SQLite's documented table-rebuild procedure, keeping every row and id.
Foreign keys are switched **off** for the rebuild; otherwise `DROP TABLE Users`
would fire the `ON DELETE CASCADE` rules and wipe every pet, booking, profile and
chat message. `DatabaseMigrationTests` guards this, and it was confirmed to fail
when that safeguard is removed.

### Owner requests a booking (FR-O4)

```mermaid
sequenceDiagram
    actor Owner
    participant OD as OwnerDashboardView
    participant BS as BookingService
    participant BR as BookingRepository
    participant DB as SQLite

    Owner->>OD: Find Sitters → pick sitter, pet, dates
    OD->>OD: validate dates (start ≥ today, end > start)
    OD->>BS: RequestBooking(Booking, rate snapshot)
    BS->>BR: GetForOwner → any live booking for the same pet overlapping?
    alt clash (REQ-PO-08)
        BS-->>OD: Fail("This pet already has a … booking …")
    else no clash
        BS->>BR: Insert(status=Pending)
        BR->>DB: INSERT INTO Bookings
        OD-->>Owner: "Request sent" + estimated total (nights × rate)
    end
```

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

### Sitter responds and chats (FR-S4 / FR-S5)

The sitter sees pending requests under **Booking Requests**, can open
**View details** (`JobDetailsWindow`), and **Accept**/**Decline** (updates
`Bookings.Status`). Accepted bookings appear under **My Chats**; opening one shows
the per-booking **Chat** panel backed by `ChatRepository`.

**Accept** goes through `BookingService.AcceptRequest` rather than the repository
directly, which enforces **REQ-GR-08**: a sitter cannot accept a request whose
dates overlap a booking they have already *accepted*. Ranges are half-open
`[StartDate, EndDate)` (`Booking.Overlaps`), so a booking handed back on the 13th
does not clash with one starting on the 13th. Pending, declined or cancelled
bookings never block. A refused request is left **pending** and the reason is
shown in the request panel; the list is deliberately not reloaded, so the
message stays visible. Owners may still *send* overlapping requests: the check
is applied only on acceptance.

> **Known WIP:** owner-side chat (FR-O5) is not yet built — only the sitter can
> open the chat UI. The chat data layer already supports both directions and is
> tested.

---

## 6. Security & quality properties

| Property | How it is achieved |
|----------|--------------------|
| No plaintext passwords | PBKDF2 salted hashing in `PasswordHasher`; only hash+salt stored |
| No account enumeration | Identical login failure message for unknown email vs wrong password |
| SQL injection resistance | All queries use parameterised SQLite commands |
| Chat privacy | Messages scoped to a booking; retrieval is per-`BookingId` |
| Referential integrity | SQLite foreign keys enabled with cascade rules |
| Testability | Logic layer has no WPF dependency; `Database` path is injectable |

See `docs/UnitTests.md` for the test suite that verifies these.

---

## 7. Building & running

The main app is a **classic WPF project that `dotnet build` cannot compile** — use
Visual Studio MSBuild. The SDK-style test projects build with `dotnet` afterwards.

```bash
# Build the app (Visual Studio MSBuild)
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" PetSitters.csproj /t:Restore,Build /p:Configuration=Debug

# Run
bin\Debug\PetSitters.exe

# Logic tests (after the app is built)
dotnet test PetSitters.Tests -c Debug
```

Or open `PetSitters.sln` in Visual Studio and use **Test → Run All Tests**.

**Reset all data:** delete `%AppData%\PetSitters\petsitters.db` and relaunch.

---

## 8. Extending the app

- **New screen:** add a `UserControl` under `Views/`, register it in
  `PetSitters.csproj` (`<Page>` + `<Compile>` — classic projects don't auto-include),
  and navigate to it from `MainWindow`.
- **New persisted data:** add a POCO in `Models/`, a `CREATE TABLE IF NOT EXISTS`
  in `Database.Initialize()`, and a repository in `Data/`; expose it from
  `AppServices`.
- **New logic:** put it in `Services/` (UI-independent) and add tests in
  `PetSitters.Tests` so it stays verifiable.
