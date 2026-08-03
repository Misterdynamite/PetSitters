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

## Architecture

The code is deliberately layered so the **business/data logic is independent of the
WPF UI** — this is what makes the assignment's test cases straightforward to write
(you can test `AuthService`, `ValidationHelper`, `PasswordHasher` and the
repositories without launching a window).

```
Models/      Plain data objects (User, SitterProfile, Pet, Booking, enums)
Data/        SQLite persistence
             - Database.cs          schema creation + connection factory
             - *Repository.cs        CRUD for each table
Services/    UI-independent logic
             - PasswordHasher.cs     PBKDF2 password hashing (no plaintext)
             - ValidationHelper.cs   email/password/number validation
             - AuthService.cs        registration + login rules
             - AppServices.cs        composition root (wires everything together)
Views/       WPF UserControls (one per screen), swapped into MainWindow
```

`App.xaml.cs` builds `AppServices` at startup and injects it into `MainWindow`,
which swaps the active view (login → register → owner/sitter dashboard).

## Persistent data storage (SQLite)

Data is stored in a single **SQLite** database file — no server to install.

- **Location:** `%AppData%\PetSitters\petsitters.db`
  (e.g. `C:\Users\<you>\AppData\Roaming\PetSitters\petsitters.db`).
  Created automatically on first run.
- **Schema:** created by `Database.Initialize()` (idempotent — safe on every start).
  Tables: `Users`, `SitterProfiles`, `Pets`, `Bookings`, with foreign keys enabled.
- **Library:** `System.Data.SQLite.Core` (NuGet). The native `SQLite.Interop.dll`
  is copied into `bin\...\x86` and `x64` automatically at build time.
- **Testability:** `Database` takes the file path as a constructor argument, so
  tests can point it at a throwaway temp file (or `:memory:`) instead of the real
  AppData database. See the smoke test described below.

**To reset all data** during testing, just delete `petsitters.db` and relaunch —
the empty schema is recreated.

### Security note (quality attribute)

Passwords are **never stored in plain text**. `PasswordHasher` uses PBKDF2
(`Rfc2898DeriveBytes`, SHA-256, 100k iterations) with a random per-user salt, and
login uses a length-constant comparison. Login failures return the same message
whether the email is unknown or the password is wrong, so the app does not reveal
which emails are registered.

## Building & running

Open `PetSitters.sln` in Visual Studio 2022 and press **F5**, or from a command line:

```bash
msbuild PetSitters.csproj /t:Restore
msbuild PetSitters.csproj /t:Build /p:Configuration=Debug
```

The built app is `bin\Debug\PetSitters.exe`.

## Try it (happy path)

1. **Create a sitter account** (choose *Offer sitting*). On the sitter dashboard,
   open *My Sitting Profile*, set availability / experience / a daily rate, and save.
2. **Log out**, then **create an owner account** (choose *Find a sitter*).
3. On the owner dashboard, add a pet under *My Pets*.
4. Open *Find Sitters*, pick the sitter, choose dates and a pet, and send a request.
5. **Log out** and log back in as the sitter → *Booking Requests* → **Accept**.
6. Log back in as the owner → *My Bookings* shows the request now **Accepted**.

## Notes / possible next steps

- No dedicated unit-test project is included yet. The logic layer is structured for
  it — a good next step is an MSTest/xUnit project referencing this one, turning the
  smoke test into formal test cases mapped to the requirements traceability matrix.
- Location matching is textual (owners see all sitters). Distance-based search would
  be a sensible enhancement.
