# Unit & Integration Test Suite — `PetSitters.Tests`

This document outlines the automated test suite for the Sitters4Us (PetSitters)
prototype. It describes what is tested, the test-design techniques used, a
requirements traceability matrix, and how to run everything.

- **Project:** `PetSitters.Tests` (MSTest, SDK-style, targets `net472`)
- **What it tests:** the UI-independent logic layer — `Services` (`AuthService`,
  `ValidationHelper`, `PasswordHasher`), the domain `Models`, and the SQLite
  `Data` repositories.
- **Result:** **48 test methods → 102 executed cases** (the difference is
  `[DataRow]` data-driven expansion). All passing.
- **Not covered here:** end-to-end GUI behaviour lives in the separate
  `PetSitters.UiTests` (FlaUI) project.

---

## How to run

**Visual Studio:** open `PetSitters.sln` → **Test → Run All Tests**. The app and
both test projects build, and results appear in Test Explorer.

**Command line** — build the app first (the classic WPF app cannot be built by
the .NET SDK), then run the tests:

```
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" PetSitters.csproj /t:Build /p:Configuration=Debug
dotnet test PetSitters.Tests -c Debug
```

---

## Test-design techniques (ENSE707 Lab 1–5)

| Lab | Technique | Where it appears |
|-----|-----------|------------------|
| 1 | MSTest, AAA structure, `Method_Scenario_ExpectedResult` naming, invalid-input tests | Every test |
| 1 / 2 | Security testing (salting, no plaintext, no user enumeration) | `PasswordHasherTests`, `AuthServiceTests` |
| 2 | Result-object assertions (`AuthResult.Success` / `ErrorMessage`) | `AuthServiceTests` |
| 4 | Each test tagged to a requirement ID (traceability) | Class summaries + RTM below |
| 5 | Equivalence partitioning + boundary-value analysis | `ValidationHelperTests`, `BookingCalculationTests` |
| 5 | Data-driven tests with `[DataRow]` | `ValidationHelperTests`, `BookingCalculationTests`, parts of `AuthServiceTests` |
| 5 | Component/integration tests against **real** SQLite persistence, each isolated | `*RepositoryTests`, `ChatPersistenceTests` |

### Test isolation (`DatabaseTestBase`)

Every test that needs a database gets its **own** temporary SQLite file, created
in `TestInitialize` and deleted in `TestCleanup`. Tests never share state and
never touch the real `%AppData%\PetSitters\petsitters.db` used by the running
app, so they can run in any order (or in parallel) safely.

---

## Test inventory

### Pure unit tests (no database)

#### `PasswordHasherTests` — 6 methods / 7 cases · security · FR-A1, FR-A2
| Test | What it verifies |
|------|------------------|
| `CreateHash_ThenVerifyWithCorrectPassword_ReturnsTrue` | A correct password verifies against its stored hash. |
| `Verify_WithWrongPassword_ReturnsFalse` | A wrong password is rejected. |
| `CreateHash_IsSalted_SamePasswordProducesDifferentHashes` | The same password hashes differently each time (random salt). |
| `CreateHash_DoesNotStorePasswordInPlainText` | The hash/salt never contain the raw password. |
| `Verify_WithTamperedHash_ReturnsFalse` | A modified hash fails verification. |
| `Verify_WithMissingStoredHashOrSalt_ReturnsFalse` `[DataRow ×2]` | A row with no credentials cannot authenticate. |

#### `ValidationHelperTests` — 7 methods / 40 cases · data quality · FR-A1, FR-O3, FR-S2
| Test | Technique | Cases |
|------|-----------|-------|
| `IsValidEmail_ClassifiesInputCorrectly` | Equivalence partitioning (valid vs. empty / no-@ / no-domain / no-local / spaces) | 9 |
| `IsValidPassword_EnforcesMinimumLengthBoundary` | Boundary-value analysis around 6 chars (5=fail, 6=pass, 7=pass) | 4 |
| `IsNonEmpty_DetectsBlankValues` | null / whitespace / empty vs. real value | 4 |
| `TryParseRate_AcceptsOnlyNonNegativeNumbers` | Boundary at 0; rejects negatives and non-numbers | 7 |
| `TryParseNonNegativeInt_AcceptsOnlyWholeNonNegativeNumbers` | Whole-number, non-negative rule | 6 |
| `TryParseAgeMonths_AcceptsBlankOrZeroToEleven` | Boundary-value analysis on the optional months field (−1/0 … 11/12) | 9 |
| `TryParseAgeMonths_TreatsNullAsNotSupplied` | Optional field defaults to 0 | 1 |

#### `PetAgeTests` — 3 methods / 10 cases · FR-O3
| Test | What it verifies | Cases |
|------|------------------|-------|
| `FormatAge_CombinesYearsAndMonths` | Years + optional months render correctly, incl. singular/plural ("1 year 1 month") and the 0/0 "Under 1 month" case | 8 |
| `AgeDisplay_UsesTheStoredYearsAndMonths` | A pet's display string reflects its stored age | 1 |
| `AgeMonths_DefaultsToZero_WhenNotSupplied` | Months are optional | 1 |

#### `BookingCalculationTests` — 2 methods / 8 cases · FR-O4
| Test | Technique | Cases |
|------|-----------|-------|
| `Nights_IsDateSpan_WithMinimumOfOne` | Boundary-value analysis on the "minimum 1 night" clamp (0→1, 1→1, 3→3, 7→7) | 4 |
| `EstimatedTotal_IsNightsTimesDailyRate` | nights × daily-rate cost, incl. the clamped case | 4 |

### Component / integration tests (real isolated SQLite)

#### `AuthServiceTests` — 13 methods / 19 cases · FR-A1, FR-A2
| Test | What it verifies |
|------|------------------|
| `Register_WithValidDetails_Succeeds` | Valid registration creates a user with a DB id and correct role. |
| `Register_WithInvalidEmail_Fails` `[DataRow ×3]` | Bad email formats are rejected. |
| `Register_WithWeakPassword_Fails` | Sub-6-character password is rejected (boundary). |
| `Register_WithEmptyName_Fails` | Blank full name is rejected. |
| `Register_WithEmptyPhone_Fails` `[DataRow ×2]` | Phone is a **required** field. |
| `Register_WithEmptyLocation_Fails` `[DataRow ×2]` | Location is a **required** field. |
| `Register_WithAllFieldsSupplied_PersistsPhoneAndLocation` | All supplied details are stored. |
| `Register_DuplicateEmail_Fails_CaseInsensitive` | A duplicate email (different casing) is rejected. |
| `Register_StoresHashedPassword_NotPlainText` | The persisted row stores a hash + salt, not the password. |
| `Login_WithCorrectCredentials_Succeeds` | Correct credentials log in. |
| `Login_WithWrongPassword_Fails` | Wrong password is rejected. |
| `Login_WithMissingInput_Fails` `[DataRow ×3]` | Empty email/password combinations are rejected. |
| `Login_DoesNotRevealWhetherEmailIsRegistered` | Wrong password and unknown email return the **same** message (no user enumeration). |

#### `UserRepositoryTests` — 4 methods · FR-O1, FR-O2, FR-S1
| Test | What it verifies |
|------|------------------|
| `Insert_AssignsId_AndCanBeFoundByEmailAndId` | Insert assigns an id; lookups by email and id work. |
| `EmailExists_IsCaseInsensitive` | Email uniqueness check ignores casing. |
| `GetByRole_ReturnsOnlyThatRole_OrderedByName` | Browsing sitters returns only sitters, name-ordered. |
| `UpdateDetails_PersistsEditedFields` | Edited personal details are saved. |

#### `PetRepositoryTests` — 4 methods · FR-O3
| Test | What it verifies |
|------|------------------|
| `Insert_ThenGetByOwner_ReturnsThePets` | An owner's pets are stored and returned (name-ordered). |
| `Insert_PersistsYearsAndOptionalMonths` | Both age parts round-trip through SQLite. |
| `Insert_DefaultsMonthsToZero_WhenNotSupplied` | Omitting months stores 0. |
| `Delete_RemovesOnlyTheSelectedPet` | Deleting one pet leaves the others intact. |

#### `SitterProfileRepositoryTests` — 3 methods · FR-S2
| Test | What it verifies |
|------|------------------|
| `Upsert_InsertsProfile_WhenNoneExists` | First save creates the sitter profile. |
| `Upsert_UpdatesInPlace_WhenProfileAlreadyExists` | A second save updates in place (1:1, no duplicate). |
| `GetByUserId_ReturnsNull_WhenSitterHasNoProfileYet` | Missing profile returns null. |

#### `BookingRepositoryTests` — 4 methods · FR-O4, FR-S4
| Test | What it verifies |
|------|------------------|
| `Insert_BookingIsVisibleToBothOwnerAndSitter` | A request appears in both the owner's and sitter's lists. |
| `UpdateStatus_Accept_IsPersisted` | Accepting a request persists the new status. |
| `GetForSitter_DoesNotReturnAnotherSittersBookings` | A sitter sees only their own requests (isolation). |
| `Insert_PreservesDailyRateSnapshot` | The rate captured at booking time is stored. |

#### `ChatPersistenceTests` — 3 methods · FR-O5, FR-S5
| Test | What it verifies |
|------|------------------|
| `Message_IsPersisted_AndReadBackByAFreshRepository` | A message survives being read back by a **new** repository instance (proves on-disk persistence). |
| `GetForBooking_ReturnsOnlyThatBookingsMessages` | Messages are scoped to their booking (not visible to unrelated bookings/users). |
| `GetForBooking_ReturnsMessagesInChronologicalOrder` | Messages return oldest-first. |

---

## Requirements Traceability Matrix (RTM)

Links each functional requirement to the test methods that provide evidence for
it. Traceability lets the team confirm every requirement has test coverage and,
when a requirement changes, quickly find the tests that must be reviewed.

| Req ID | Requirement | Test evidence | Status |
|--------|-------------|---------------|--------|
| FR-A1 | Account creation (Owner or Sitter) | `Register_*` (AuthServiceTests); `PasswordHasherTests`; email/password rows in `ValidationHelperTests` | ✅ Passing |
| FR-A2 | Pet sitter / owner login | `Login_*` (AuthServiceTests); `PasswordHasherTests` | ✅ Passing |
| FR-O1 | Owner sees / browses potential sitters | `GetByRole_ReturnsOnlyThatRole_OrderedByName` | ✅ Passing |
| FR-O2 | Owner registers personal details (incl. location) | `Insert_AssignsId_*`, `UpdateDetails_PersistsEditedFields` | ✅ Passing |
| FR-O3 | Owner registers pet details | `PetRepositoryTests`; `TryParseNonNegativeInt_*` (pet age) | ✅ Passing |
| FR-O4 | Owner requests a booking | `BookingRepositoryTests` (insert/visibility/rate); `BookingCalculationTests` | ✅ Passing |
| FR-O5 | Owner chats with sitter once accepted | `ChatPersistenceTests` (persistence + isolation) | ⚠️ Data layer tested; owner-side chat UI still WIP |
| FR-S1 | Sitter registers personal details (incl. location) | `UserRepositoryTests` (shared user table) | ✅ Passing |
| FR-S2 | Sitter registers availability, experience, prefs, quals, rate | `SitterProfileRepositoryTests`; `TryParseRate_*` | ✅ Passing |
| FR-S4 | Sitter accepts / declines a request | `UpdateStatus_Accept_IsPersisted`, `GetForSitter_DoesNotReturnAnotherSittersBookings` | ✅ Passing |
| FR-S5 | Sitter chats with owner once accepted | `ChatPersistenceTests` | ✅ Passing |

> FR-S3 (sitter views full job details before deciding) is UI-only presentation
> and is exercised by the `PetSitters.UiTests` end-to-end journey rather than by
> this logic suite.

---

## Quality attributes exercised

| Attribute | Evidence in the suite |
|-----------|-----------------------|
| **Security** | Salted PBKDF2 hashing, no plaintext storage, no user enumeration, per-booking chat isolation. |
| **Functional correctness** | Registration/login rules, booking visibility, cost calculations. |
| **Reliability** | Bookings and messages persist and read back intact; status changes are durable. |
| **Data quality** | Email/password/rate/age validation via boundary and equivalence tests. |
| **Maintainability / testability** | Logic is UI-independent and tested directly; isolated temp databases keep tests deterministic. |
