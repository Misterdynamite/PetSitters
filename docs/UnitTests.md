# Unit & Integration Test Suite — `PetSitters.Tests`

This document outlines the automated test suite for the Sitters4Us (PetSitters)
prototype. It describes what is tested, the test-design techniques used, a
requirements traceability matrix, and how to run everything.

- **Project:** `PetSitters.Tests` (MSTest, SDK-style, targets `net472`)
- **What it tests:** the UI-independent logic layer — `Services` (`AuthService`,
  `BookingService`, `ValidationHelper`, `PasswordHasher`, and the launch-time
  database configuration: `AppConfig`/`EnvFile` and `DatabaseSelector`), the
  domain `Models`, the `Data` repositories, and `MySqlUrl` (turning
  `DATABASE_URL` into a connection string).
- **Result:** **148 test methods → 288 executed cases on SQLite** (the
  difference is `[DataRow]` data-driven expansion). All passing (last run
  2026-10-07).
- **Plus, opt-in:** **121 MySQL parity cases** that re-run the database tests
  against a real MySQL server. All 121 passing against the real server on
  2026-10-07 (on a rerun: the run before it lost 3 cases to network timeouts;
  see [CI.md](CI.md#mysql-parity-tests-opt-in)). They are skipped unless you ask
  for them (see
  [MySQL parity](#mysql-parity-opt-in)).
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

A command-line MSBuild build of the app already runs this suite as a gate
(smoke set, then everything except the `MySql` category); add
`/p:SkipTests=true` to build only. See [CI.md](CI.md).

The tests never need the cloud database or a `.env` file. The MySQL parity
run is the one exception, and it only happens when you opt in:

```
# PowerShell: reuse the server named in DATABASE_URL (environment or the repo-root .env)
$env:PETSITTERS_TEST_MYSQL = "1"
dotnet test PetSitters.Tests -c Debug --filter TestCategory=MySql
```

---

## Test-design techniques (ENSE707 Lab 1–5)

| Lab | Technique | Where it appears |
|-----|-----------|------------------|
| 1 | MSTest, AAA structure, `Method_Scenario_ExpectedResult` naming, invalid-input tests | Every test |
| 1 / 2 | Security testing (salting, no plaintext, no user enumeration, the database password never shown in an error or tooltip) | `PasswordHasherTests`, `AuthServiceTests`, `DatabaseConfigurationTests`, `DatabaseSelectorTests` |
| 2 | Result-object assertions (`AuthResult.Success` / `ErrorMessage`, `DatabaseSelection.Summary` / `FellBack`) | `AuthServiceTests`, `DatabaseSelectorTests` |
| 4 | Each test tagged to a requirement ID (traceability) | Class summaries + RTM below |
| 5 | Equivalence partitioning + boundary-value analysis | `ValidationHelperTests`, `BookingCalculationTests`, `DatabaseConfigurationTests` (1–60 s connect timeout, `PETSITTERS_DB` modes) |
| 5 | Data-driven tests with `[DataRow]` | `ValidationHelperTests`, `BookingCalculationTests`, `DatabaseConfigurationTests`, parts of `AuthServiceTests` |
| 5 | Component/integration tests against **real** SQLite persistence, each isolated | `*RepositoryTests`, `ChatPersistenceTests` |
| 5 | The same component tests re-run on a second engine (MySQL) by inheritance, so one set of assertions proves both behave alike | `MySqlParityTests` (opt-in) |

### Test isolation (`DatabaseTestBase`)

Every test that needs a database gets its **own** temporary SQLite file, created
in `TestInitialize` and deleted in `TestCleanup`. Tests never share state and
never touch the real `%AppData%\PetSitters\petsitters.db` used by the running
app, or the cloud database, so they can run in any order (or in parallel)
safely. Apart from the opt-in MySQL run, they don't read the app's `.env`
either: a test that needs a configuration builds its own, in memory
(`AppConfig.FromValues`) or as a throwaway temp `.env` file.

The database comes from `DatabaseTestBase.CreateDatabase()`, which is
`virtual`: it returns a temp SQLite file by default, and the MySQL parity
subclasses override it to return the run's temporary MySQL database instead.
Nothing else about the tests changes. `TestCleanup` only deletes a file when
there is one, so the MySQL run skips it.

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

#### `ValidationHelperTests` — 9 methods / 48 cases · data quality · FR-A1, FR-O3, FR-S2
| Test | Technique | Cases |
|------|-----------|-------|
| `IsValidEmail_ClassifiesInputCorrectly` | Equivalence partitioning (valid vs. empty / no-@ / no-domain / no-local / spaces) | 9 |
| `IsValidEmail_EnforcesMaximumLengthBoundary` | Boundary-value analysis on the 254-character email cap (253 and 254 accepted, 255 rejected), which keeps every valid email inside MySQL's `VARCHAR(255)` | 3 |
| `IsValidPassword_EnforcesMinimumLengthBoundary` | Boundary-value analysis around 6 chars (5=fail, 6=pass, 7=pass) | 4 |
| `IsNonEmpty_DetectsBlankValues` | null / whitespace / empty vs. real value | 4 |
| `TryParseRate_AcceptsOnlyNonNegativeNumbers` | Boundary at 0; rejects negatives and non-numbers | 7 |
| `TryParseRate_FitsTheMoneyColumn` | Boundary-value analysis on the DECIMAL(10,2) money column: 99,999,999.99 accepted, 100,000,000 rejected; at most 2 decimal places (45.555 rejected) so both engines store the same value | 5 |
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

#### `BookingOverlapTests` — 5 methods / 22 cases · REQ-GR-08, REQ-PO-08
| Test | Technique | Cases |
|------|-----------|-------|
| `RangesOverlap_AgainstAcceptedBooking` | Boundary-value analysis around a 10th→13th booking: inside, surrounding, each edge, and back-to-back on both sides (hand-back day = next start day is **not** a clash) | 11 |
| `RangesOverlap_IsSymmetric` | Overlap does not depend on which booking is checked first | 1 |
| `RangesOverlap_IgnoresTimeOfDay` | Only dates are compared, since the form captures dates, not times | 1 |
| `SharesPetWith_TreatsAllMyPetsAsEveryPet` | REQ-PO-08 pet matching: same / different pet, and "All my pets" (null) against a pet or itself | 5 |
| `IsActive_OnlyForPendingAndAccepted` | Equivalence partition over statuses: only live bookings hold a pet's or sitter's time | 4 |

### Component / integration tests (real isolated SQLite)

Every class in this section except `DatabaseMigrationTests` (the migrations
are SQLite-only) also runs on MySQL in the opt-in
[parity run](#mysql-parity-opt-in).

#### `SharedEmailTests` — 10 methods / 13 cases · REQ-GR-06, FR-A1, FR-A2
| Test | What it verifies |
|------|------------------|
| `Register_SameEmailForTheOtherRole_Succeeds` `[DataRow ×2]` | Owner→sitter and sitter→owner with one email both succeed (two accounts). |
| `Register_SameEmailSameRole_IsRejectedWithRoleSpecificWarning` `[DataRow ×2]` | Same email + same role (any casing) is refused with "An owner/A sitter account … already exists"; no second account. |
| `Register_WhenBothRolesExist_RejectsEitherRole` | With both roles taken, neither can be registered again. |
| `Insert_DuplicateEmailAndRole_IsRejectedByTheDatabase` | The `UNIQUE (Email, Role)` constraint holds even if `AuthService` is bypassed, and the error is recognised by `Database.IsUniqueViolation` (what turns a simultaneous duplicate sign-up into the normal message). |
| `IsUniqueViolation_OtherConstraintErrors_AreNotDuplicates` | A different constraint failure (a foreign key) is not mistaken for a duplicate account. |
| `Login_SharedEmailSamePassword_AsksWhichRole` | Login doesn't guess; it returns `RequiresRoleChoice`. |
| `Login_SharedEmailWithChosenRole_OpensThatAccount` `[DataRow ×2]` | Supplying the role opens that account. |
| `Login_SharedEmailDifferentPasswords_OpensTheMatchingAccount` | Different passwords pick the account without a prompt. |
| `Login_SharedEmailFailures_UseTheGenericMessage` | Wrong password / role with no account give the same message as an unknown email (no enumeration). |
| `SharedEmail_AccountsAreSeparate` | The two accounts have separate ids and data. |

#### `BookingValidationTests` — 10 methods / 17 cases · REQ-GR-04
Uses a **fixed clock** (noon, 10 Mar 2030) injected into `BookingService`, so results never depend on the run date.

| Test | Technique | Cases |
|------|-----------|-------|
| `Validate_StartDate_RelativeToToday` | Boundary on "today": yesterday rejected; today and tomorrow allowed (judged by day) | 3 |
| `Validate_EndNotAfterStart_IsRejected` | End equal to / before start | 2 |
| `Validate_MinimumDuration_Boundary` | 59 / 60 / 61 minutes around the 1-hour minimum | 3 |
| `Validate_MaximumDuration_Boundary` | 13 / 14 / 15 days around the 14-day maximum | 3 |
| `Validate_FourteenDaysAndOneMinute_IsRejected` | Just over the maximum | 1 |
| `Validate_AllMyPets_WhenOwnerHasNoPets_IsRejected` | "No pet selected" | 1 |
| `Validate_AllMyPets_WhenOwnerHasPets_IsAllowed` | "All my pets" is a valid selection when pets exist | 1 |
| `Validate_PetBelongingToAnotherOwner_IsRejected` | Another owner's (or a deleted) pet isn't a valid selection | 1 |
| `RequestBooking_ValidSubmission_IsStoredAsPending` | "A valid submission is accepted" | 1 |
| `RequestBooking_InvalidSubmission_IsRejectedAndNotStored` | A rejected request saves nothing | 1 |

#### `DatabaseMigrationTests` — 2 methods · REQ-GR-06 (data-loss guard)
| Test | What it verifies |
|------|------------------|
| `Initialize_OnPreGr06Database_KeepsAllDataAndAllowsSecondRole` | An old `UNIQUE(Email)` database is rebuilt to `UNIQUE (Email, Role)` keeping users, ids, pets, bookings and links, and then accepts a second role. Mutation-checked: it fails if the rebuild leaves foreign keys on. |
| `Initialize_RunTwice_IsIdempotent` | Startup migration is a no-op the second time. |

#### `BookingServiceTests` — 26 methods / 35 cases · REQ-GR-08, REQ-PS-03, REQ-PO-08, REQ-PO-07
| Test | What it verifies |
|------|------------------|
| `AcceptRequest_WithNoClash_AcceptsAndPersists` | A non-clashing request is accepted and persisted. |
| `AcceptRequest_OverlappingAnAcceptedBooking_IsRejectedAndStaysPending` | FR-07: overlap with an accepted booking is refused with a "stay pending" message; neither booking changes. |
| `AcceptRequest_BackToBackWithAcceptedBooking_IsAccepted` | Boundary: ending and starting on the same day is allowed. |
| `AcceptRequest_OverlappingANonAcceptedBooking_IsAccepted` `[DataRow ×3]` | Pending / declined / cancelled bookings do not block (only accepted ones do). |
| `AcceptRequest_OverlapWithAnotherSittersBooking_IsAccepted` | The rule is per sitter. |
| `AcceptRequest_ForAnotherSittersBooking_IsRejected` | A sitter cannot accept a booking addressed to someone else. |
| `AcceptRequest_ForANonPendingBooking_IsRejected` | A cancelled booking cannot be revived by accepting it. |
| `RequestBooking_WithNoClash_IsStoredAsPending` | A clash-free request is saved as Pending. |
| `RequestBooking_SamePetOverlappingLiveBooking_IsRejectedAndNotStored` `[DataRow ×2]` | REQ-PO-08: same pet, *different* sitter, overlapping a pending or accepted booking, is refused and nothing is saved. |
| `RequestBooking_SamePetOverlappingInactiveBooking_IsAllowed` `[DataRow ×2]` | Declined / cancelled bookings free the pet. |
| `RequestBooking_DifferentPetSameDates_IsAllowed` | The rule is per pet. |
| `RequestBooking_SamePetBackToBack_IsAllowed` | Boundary: hand-back day = next start day. |
| `RequestBooking_AllMyPets_ClashesWithAnyPet` `[DataRow ×2]` | "All my pets" clashes with a specific pet, in both directions. |
| `RequestBooking_AnotherOwnersBooking_DoesNotBlock` | The rule is per owner. |
| `CancelBooking_FromPendingOrAccepted_IsCancelled` `[DataRow ×2]` | REQ-PO-07: both allowed stages persist as Cancelled. |
| `CancelBooking_FromDeclinedOrCancelled_IsRejected` `[DataRow ×2]` | Finished bookings can't be cancelled; status unchanged. |
| `CancelBooking_ByAnyoneButTheOwner_IsRejected` | Authorisation: neither another owner nor the sitter can cancel. |
| `CancelBooking_ThenRebookSamePetAndDates_IsAllowed` | REQ-PO-07 + REQ-PO-08: cancelling frees the pet to be rebooked. |
| `DeclineRequest_Pending_IsDeclined` | REQ-PS-03: declining now goes through the service. |
| `DeclineRequest_AfterTheOwnerCancelled_IsRefused_AndStaysCancelled` | Shared database: the owner acted first, so the sitter's decline changes nothing, and the result is flagged `ChangedElsewhere` so the view reloads its stale list. |
| `AcceptRequest_AfterTheOwnerCancelled_ReportsChangedElsewhere` | The same for accepting: the sitter's list still showed the request as pending; nothing changes and the result is flagged `ChangedElsewhere`. |
| `CancelBooking_AfterTheSitterDeclined_ReportsChangedElsewhere` | The same for the owner: the message says what the booking is now ("it is now declined"). |
| `DeclineRequest_NotYours_IsAPlainRefusal` | Security: someone else's booking is refused but NOT flagged `ChangedElsewhere`, so it gives no reload hint about a booking that isn't theirs. |
| `DeclineRequest_ForAnotherSittersBooking_IsRefused` | Authorisation: only the booking's sitter can decline. |
| `CancelAsSitter_OnlyFromAccepted` `[DataRow ×3]` | The sitter's cancel (My Chats) works only on an accepted booking. |
| `CancelAsSitter_ByAnotherSitter_IsRefused` | Authorisation: only the booking's sitter can cancel it. |

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
| `Register_DuplicateEmail_Fails_CaseInsensitive` | A duplicate email for the **same role** (different casing) is rejected. |
| `Register_StoresHashedPassword_NotPlainText` | The persisted row stores a hash + salt, not the password. |
| `Login_WithCorrectCredentials_SucceedsWithinPerformanceBudget` | Correct credentials log in, within the sign-in time budget (REQ-NFR-02). |
| `Login_WithWrongPassword_Fails` | Wrong password is rejected. |
| `Login_WithMissingInput_Fails` `[DataRow ×3]` | Empty email/password combinations are rejected. |
| `Login_DoesNotRevealWhetherEmailIsRegistered` | Wrong password and unknown email return the **same** message (no user enumeration). |

#### `UserRepositoryTests` — 6 methods · FR-O1, FR-O2, FR-S1
| Test | What it verifies |
|------|------------------|
| `Insert_AssignsId_AndCanBeFoundByEmailAndId` | Insert assigns an id; lookups by email and id work. |
| `EmailExists_IsCaseInsensitive` | Email uniqueness check ignores casing. |
| `GetByRole_ReturnsOnlyThatRole_OrderedByName` | Browsing sitters returns only sitters, name-ordered. |
| `UpdateDetails_PersistsEditedFields` | Edited personal details are saved. |
| `UpdateDetails_DoesNotTouchTheProfilePicture` | Shared database: on a PC where the picture path reads back as "no image", saving name/phone/location doesn't write that empty value back and erase the picture set on another PC (pictures are saved separately, by `UpdateProfileImage`). |
| `GetSittersWithProfiles_OneQuery_IncludesSittersWithoutProfiles` | Find Sitters loads every sitter with their profile in ONE query (asserted via `Database.ConnectionsOpened`); a sitter without a profile is listed with a null profile; owners aren't listed; joined users carry no password hash. |

#### `PetRepositoryTests` — 6 methods / 7 cases · FR-O3, security
| Test | What it verifies |
|------|------------------|
| `Insert_ThenGetByOwner_ReturnsThePets` | An owner's pets are stored and returned (name-ordered). |
| `Insert_PersistsYearsAndOptionalMonths` | Both age parts round-trip through the database. |
| `Insert_DefaultsMonthsToZero_WhenNotSupplied` | Omitting months stores 0. |
| `Delete_RemovesOnlyTheSelectedPet` | Deleting one pet leaves the others intact. |
| `ImagePath_OutsideTheLocalImageFolder_ReadsBackAsNull` `[DataRow ×2]` | Security: a planted network-share path or other file read from the (shared) database comes back as "no image". |
| `ImagePath_InsideTheLocalImageFolder_IsKept` | A real imported picture's path is kept. |

#### `SitterProfileRepositoryTests` — 3 methods · FR-S2
| Test | What it verifies |
|------|------------------|
| `Upsert_InsertsProfile_WhenNoneExists` | First save creates the sitter profile. |
| `Upsert_UpdatesInPlace_WhenProfileAlreadyExists` | A second save updates in place (1:1, no duplicate). |
| `GetByUserId_ReturnsNull_WhenSitterHasNoProfileYet` | Missing profile returns null. |

#### `BookingRepositoryTests` — 16 methods / 17 cases · FR-O4, FR-S4, REQ-PO-07
| Test | What it verifies |
|------|------------------|
| `Insert_BookingIsVisibleToBothOwnerAndSitter` | A request appears in both the owner's and sitter's lists. |
| `UpdateStatus_Accept_IsPersisted` | Accepting a request persists the new status. |
| `UpdateStatus_Decline_IsPersisted` | Declining a request persists the new status (tested apart from accept so a failure names the branch). |
| `UpdateStatus_Cancel_IsPersistedFromEitherStage` `[DataRow ×2]` | REQ-PO-07: cancelling from pending or accepted persists as Cancelled; the record is kept, not deleted. |
| `UpdateStatus_Cancel_RemovesBookingFromSittersPendingQueue` | A cancelled booking leaves the sitter's pending requests but stays on the owner's record. |
| `GetForSitter_DoesNotReturnAnotherSittersBookings` | A sitter sees only their own requests (isolation). |
| `Insert_PreservesDailyRateSnapshot` | The rate captured at booking time is stored. |
| `GetDetailsForOwner_IncludesSitterAndPet` | The owner's one-query loader returns each booking with its sitter, owner and pet. |
| `GetDetailsForSitter_AllMyPetsBooking_HasNoPet_ButIsStillListed` | Boundary: an "All my pets" booking (no pet) still comes back from the LEFT JOIN, with the owner's details. |
| `GetDetails_JoinedUsers_DoNotCarryPasswordHashes` | Security: joined owner/sitter rows never include another person's password hash or salt. |
| `GetDetailsForSitter_ExcludesOtherSittersBookings` | Isolation: a sitter's loader returns only their bookings. |
| `GetDetails_IsOneRoundTrip_RegardlessOfRowCount` | Performance: with 6 bookings, each loader is still exactly ONE database round trip (the old screens made 1 + 2 per booking). |
| `TryUpdateStatus_FromAnExpectedStatus_Changes_AndRaisesTheEvent` | The conditional status change works and notifies listeners. |
| `TryUpdateStatus_WhenAlreadyChangedByTheOtherParty_ChangesNothing` | The race guard at SQL level: a stale transition changes nothing and raises no event. |
| `Insert_StoresBookingDatesWithoutATimeZoneOffset` | Booking dates are stored offset-free and come back as the same calendar value, so they don't shift between time zones. |
| `GetById_LegacyRowWithAnOffset_ReadsTheDateAsWritten` | Boundary: a row written before that change, with a `+14:00` offset (ahead of every real time zone), still reads back as the date the user picked, not the previous day. |

#### `ChatPersistenceTests` — 4 methods · FR-O5, FR-S5
| Test | What it verifies |
|------|------------------|
| `Message_IsPersisted_AndReadBackByAFreshRepository` | A message survives being read back by a **new** repository instance (proves the message is persisted, not cached). |
| `GetForBooking_ReturnsOnlyThatBookingsMessages` | Messages are scoped to their booking (not visible to unrelated bookings/users). |
| `GetForBooking_ReturnsMessagesInChronologicalOrder` | Messages return oldest-first. |
| `GetForBookingWithSenderNames_OneQuery_InOrder_WithNames` | The chat panel's loader returns messages in order WITH sender names, in one query (it used to look up each sender separately). |

### Database configuration & launch-time choice (REQ-GR-09, proposed)

The app tries the shared cloud MySQL database first and falls back to the local
SQLite file if it can't use it, decided once at launch. `DatabaseConfigurationTests`
and `DatabaseSelectorTests` test how that choice is configured and made;
`LocalImagesTests` tests the guard on image paths read back from the shared
database. Every URL in them uses made-up credentials; none contacts the real
cloud server.

#### `LocalImagesTests` — 4 methods / 18 cases · security
| Test | What it verifies |
|------|------------------|
| `TrustedPathOrNull_FileDirectlyInTheImageFolder_IsTrusted` | A file directly in this PC's image folder is accepted. |
| `TrustedPathOrNull_FolderInDifferentCase_IsTrusted` | Boundary: the folder comparison ignores case, as Windows paths do (and nothing else is relaxed). |
| `TrustedPathOrNull_AnythingElse_IsNull` `[DataRow ×10]` | UNC/network shares (backslashes, forward slashes, and both mixed-separator orders with a short name), a Win32 device path, another folder, a relative path, an alternate data stream, empty and null are all rejected. |
| `TrustedPathOrNull_StartsInsideButEscapesOrNests_IsNull` `[DataRow ×6]` | Boundary: paths that start in the folder but aren't a plain file directly in it: a `..` escape, a sub-folder, a device name (`CON.png`), a trailing dot (Windows strips it), an alternate data stream, a forward slash. |

#### `DatabaseConfigurationTests` — 15 methods / 39 cases · REQ-GR-09 · security
Covers the `.env` format (`EnvFile`), which source wins (`AppConfig`), and turning
`DATABASE_URL` into a MySQL connection string (`MySqlUrl`). Pure unit tests: no
database is opened.

| Test | What it verifies (technique) | Cases |
|------|------------------------------|-------|
| `EnvFile_ParsesKeyValuePairs_IgnoringCommentsAndBlanks` | `KEY=value` lines are read; `#` comments, blank lines and surrounding spaces are ignored (Smoke) | 1 |
| `EnvFile_ValueFormats` | Equivalence partitioning over value formats: double / single quotes stripped, only the first `=` splits, leading `export`, empty value, a lone quote kept | 6 |
| `EnvFile_MalformedLines_AreIgnored` | Invalid input: a line with no `=` or no key is skipped, so a typo can't stop the app starting | 1 |
| `EnvFile_MissingFile_MeansNoSettings` | Error handling: no `.env` means no settings (local database only), not an exception | 1 |
| `AppConfig_EnvironmentVariable_OverridesEnvFile` | Precedence: a real environment variable beats the `.env`, and keys only in the file are still read (this is how the UI tests force SQLite) | 1 |
| `AppConfig_DatabaseMode` | Equivalence partitioning on `PETSITTERS_DB`: `sqlite` (any casing) and `local` force the local database; `auto`, unset or a typo keep the normal MySQL-first behaviour | 6 |
| `AppConfig_ConnectTimeout_IsBounded` | Boundary-value analysis on `DB_CONNECT_TIMEOUT_SECONDS` (1–60): 1 and 60 accepted; 0, 61 and non-numbers fall back to the 8 s default | 5 |
| `AppConfig_NoUrl_MeansNoCloudDatabase` | A blank `DATABASE_URL` counts as not configured | 1 |
| `AppConfig_EnvironmentVariableNames_AreCaseInsensitive` | Boundary: a lower-case `petsitters_db=sqlite` still forces the local database, because Windows variable names are case-insensitive | 1 |
| `MySqlUrl_ParsesEveryPart` | Host, port, user, password, database, ssl-mode and connect timeout all reach the connection string, and the pool stays capped (the server's connection limit is shared) (Smoke) | 1 |
| `MySqlUrl_NoPort_UsesMySqlDefault_AndNoSslMode_RequiresTls` | Defaults: no port → 3306; no ssl-mode → `REQUIRED`, so a URL is never silently unencrypted | 1 |
| `MySqlUrl_PercentEncodedCredentials_AreDecoded` | `%`-encoded user and password are decoded; an encoded `:` stays inside the password | 1 |
| `MySqlUrl_SslModes` | Each of `DISABLED` / `PREFERRED` / `REQUIRED` / `VERIFY_CA` / `VERIFY_IDENTITY` maps to the driver's mode; case-insensitive, dash or underscore | 5 |
| `MySqlUrl_SslCa_IsPassedToTheDriver` | `ssl-ca=` (percent-encoded path) reaches the driver's `SslCa`, for `VERIFY_CA` when Windows doesn't trust the server's CA | 1 |
| `MySqlUrl_InvalidUrl_FailsWithoutLeakingThePassword` | Security + invalid input: empty, wrong scheme, no database, two path segments, no password, unknown ssl-mode, not a URL → a `FormatException` that names `DATABASE_URL` and never contains the password | 7 |

#### `DatabaseSelectorTests` — 8 methods / 13 cases · REQ-GR-09 · reliability, security
Covers `DatabaseSelector`, the launch-time choice. The cloud database is passed
in as a factory, so "cloud reachable" is simulated with a temp SQLite file and
"cloud unreachable" uses the real MySQL driver against a closed local port,
which is refused (after about 2 s on Windows, which retries a refused connection). The header text asserted here is what the user sees.

| Test | What it verifies (technique) | Cases |
|------|------------------------------|-------|
| `Select_CloudReachable_UsesCloud` | The cloud database is used when it initialises; header "Cloud database", not flagged as a fallback (Smoke) | 1 |
| `Select_CloudUnreachable_FallsBackToLocal_AndExplains` | The core rule: a refused connection opens the local database, header "Offline: local database", and the tooltip says changes won't be seen by other users without naming the server (Smoke) | 1 |
| `Select_FailureAfterConnecting_IsReportedAsError_NotOffline` | Error handling: a failure after connecting (e.g. creating tables) still falls back, but reads "Cloud database error: local database" so a real bug isn't passed off as a network problem | 1 |
| `IsConnectivityFailure_Classification` | Equivalence partitioning of failures: bad URL, socket error, timeout (bare or wrapped by the driver) and a failed TLS handshake mean "couldn't connect"; anything else is a real error | 6 |
| `Select_CloudFailureMessage_NeverContainsThePassword` | Security: even if a driver error echoes the URL and password, the tooltip contains neither | 1 |
| `Select_MalformedUrl_FallsBackToLocal` | Invalid input: a non-`mysql://` `DATABASE_URL` doesn't stop the app opening, and the tooltip names the expected form | 1 |
| `Select_ForcedLocal_NeverTriesTheCloud` | `PETSITTERS_DB=sqlite`: the cloud is never attempted; header "Local database", not a fallback | 1 |
| `Select_NoUrl_UsesLocalWithoutWarning` | No `DATABASE_URL`: local database, no warning, cloud never attempted | 1 |

*Removed:* `Redact_RemovesUrlAndPassword` (2 cases), with the `Redact` method it
tested. Every tooltip reason is now fixed wording, so there is nothing to
redact, and blanking the password inside fixed text could itself reveal a short
password (see `docs/Architecture.md` §5.1). The tooltip test above still proves
the password never appears.

### MySQL parity (opt-in)

**9 classes / 121 cases · REQ-GR-09 · all passing against the real server on
2026-10-07 (4 m 15 s).** Network stalls show up as timeout failures, not wrong
answers; the run history is in [CI.md](CI.md#mysql-parity-tests-opt-in).

`MySqlParityTests.cs` defines nine `[TestClass, TestCategory("MySql")]`
subclasses. Each one inherits **every** test of a SQLite-backed class and
overrides only `CreateDatabase()`, so the same assertions run unchanged on MySQL.
Passing both proves the two engines behave alike where the app relies on them:
schema and foreign keys, the case-insensitive `UNIQUE (Email, Role)` rule,
upserts, name ordering, new-row ids, date ordering, and the REQ-GR-04 /
REQ-GR-08 / REQ-PO-08 booking rules.

| Parity class | Inherits all tests of | Cases |
|--------------|-----------------------|------:|
| `AuthServiceTests_MySql` | `AuthServiceTests` | 19 |
| `BookingRepositoryTests_MySql` | `BookingRepositoryTests` | 17 |
| `BookingServiceTests_MySql` | `BookingServiceTests` | 35 |
| `BookingValidationTests_MySql` | `BookingValidationTests` | 17 |
| `ChatPersistenceTests_MySql` | `ChatPersistenceTests` | 4 |
| `UserRepositoryTests_MySql` | `UserRepositoryTests` | 6 |
| `PetRepositoryTests_MySql` | `PetRepositoryTests` | 7 |
| `SitterProfileRepositoryTests_MySql` | `SitterProfileRepositoryTests` | 3 |
| `SharedEmailTests_MySql` | `SharedEmailTests` | 13 |
| **Total** | | **121** |

Not mirrored: `DatabaseMigrationTests` (the migrations it guards are SQLite-only)
and the pure unit tests (no database). `SharedEmailTests`' duplicate-insert test
expects any `DbException`, because the two drivers throw different exception
types for the same constraint violation.

**Its own temporary database.** The run never touches the app's database. It
creates a fresh database named `sitters4us_test_<UTC yyyyMMddTHHmm>_<8 hex>`,
empties every table before each test (as isolated as the per-test SQLite
file), and drops the database when the run ends (`AssemblyCleanup`). If a run
crashes, the next run drops any leftover test database older than 6 hours.
Unique names mean two people can run it at the same time.

**Strictly opt-in.** It needs a reachable server and takes minutes over the
internet, so it only runs when one of these is set:

| Variable | Meaning |
|----------|---------|
| `PETSITTERS_TEST_MYSQL_URL` | A `mysql://` URL for a server where you may create databases (the URL must name a database, but that name isn't used). |
| `PETSITTERS_TEST_MYSQL=1` | Reuse the server from the app's `DATABASE_URL` (environment variable, or the repo-root `.env`). |

The account needs permission to create and drop databases, so a least-privilege
app account can't run it. Without either variable, the 121 cases are reported
**Skipped** (Inconclusive), never failed: that is what happens in Visual
Studio's Run All Tests. The build gate and CI go further and filter the
category out (`TestCategory!=MySql`), so they never contact a MySQL server.
Never add the real `DATABASE_URL` as a CI secret: the repository is public, and
so are its logs.

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
| FR-O5 | Owner chats with sitter once accepted | `ChatPersistenceTests` (persistence + isolation, both directions); UI: `BookingJourney_*` checks the owner's Chats list | ✅ Passing. The owner-side Chats tab is implemented; no UI test sends a message from the owner side yet |
| FR-S1 | Sitter registers personal details (incl. location) | `UserRepositoryTests` (shared user table) | ✅ Passing |
| FR-S2 | Sitter registers availability, experience, prefs, quals, rate | `SitterProfileRepositoryTests`; `TryParseRate_*` | ✅ Passing |
| FR-S4 | Sitter accepts / declines a request | `UpdateStatus_Accept_IsPersisted`, `GetForSitter_DoesNotReturnAnotherSittersBookings`, `AcceptRequest_*` guards (BookingServiceTests) | ✅ Passing |
| REQ-GR-08 | Sitter cannot accept overlapping bookings (FR-07) | `BookingOverlapTests`; `AcceptRequest_Overlap*`, `AcceptRequest_BackToBack*` (BookingServiceTests); UI: `OverlappingRequests_*` | ✅ Passing |
| REQ-GR-04 | Booking dates/duration/pet validated with specific messages | `BookingValidationTests`; UI: `BookingForm_*` | ✅ Passing |
| REQ-GR-06 | One account per email per role; login asks which | `SharedEmailTests`; `DatabaseMigrationTests`; `Register_DuplicateEmail_*` (AuthServiceTests); UI: `SharedEmail_*` | ✅ Passing |
| REQ-PO-08 | No overlapping bookings for the same pet | `SharesPetWith_*`, `IsActive_*` (BookingOverlapTests); `RequestBooking_*` (BookingServiceTests); UI: `SamePetDoubleBooking_*` | ✅ Passing |
| REQ-PO-07 | Owner cancels from pending or accepted (DEF-003) | `UpdateStatus_Cancel_*` (BookingRepositoryTests); `CancelBooking_*` (BookingServiceTests); UI: `SamePetDoubleBooking_*` (pending), `BookingJourney_*` (accepted) | ✅ Passing |
| REQ-GR-09 *(proposed)* | Shared cloud database with local fallback at launch | `DatabaseSelectorTests`; `DatabaseConfigurationTests`; `MySqlParityTests` (121 cases, opt-in); UI: `Startup_*` | ✅ Passing (parity run 2026-10-07) |
| FR-S5 | Sitter chats with owner once accepted | `ChatPersistenceTests` | ✅ Passing |

> FR-S3 (sitter views full job details before deciding) is UI-only presentation
> and is exercised by the `PetSitters.UiTests` end-to-end journey rather than by
> this logic suite.
>
> REQ-GR-09 is a **proposed** requirement ID, not yet in the report. The tests
> are already tagged with it so the link holds once it is added. Its UI evidence
> is `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo` (an
> unreachable cloud URL opens on the local database, says so in the header, and
> registration still works) and `Startup_LocalDatabaseChosen_HeaderSaysLocalDatabase`.

---

## Quality attributes exercised

| Attribute | Evidence in the suite |
|-----------|-----------------------|
| **Security** | Salted PBKDF2 hashing, no plaintext storage, no user enumeration, per-booking chat isolation; the database password never appears in an error message or the header tooltip, and an unspecified ssl-mode still requires TLS. |
| **Functional correctness** | Registration/login rules, booking visibility, cost calculations. |
| **Reliability** | Bookings and messages persist and read back intact; status changes are durable; the app still opens (on the local database) when the cloud database is unreachable or misconfigured. |
| **Portability (database engines)** | The same 121 data-layer cases pass on SQLite and on MySQL (opt-in parity run). |
| **Data quality** | Email/password/rate/age validation via boundary and equivalence tests. |
| **Maintainability / testability** | Logic is UI-independent and tested directly; isolated temp databases keep tests deterministic; the cloud database is injected into `DatabaseSelector`, so fallback is tested without a network. |


---

## Test classification (type × scenario)

Every automated test, in both `PetSitters.Tests` and `PetSitters.UiTests`,
carries MSTest `[TestCategory]` labels on two axes, so the suite can be
reported and run by kind. Labels were assigned on 2026-10-01 from what each
test actually asserts, and on 2026-10-07 for the REQ-GR-09 database tests. The
MySQL parity subclasses inherit their base class's labels and add `MySql`.

**Test type**: what level or quality attribute the test exercises:

| Label | Meaning here |
|-------|--------------|
| `Unit` | Pure logic, no database or UI (`ValidationHelper`, `PasswordHasher`, `Booking` rules, pet age, `.env`/`AppConfig` parsing, `MySqlUrl`, most of `DatabaseSelector`). |
| `Integration` | Runs against a real database: an isolated SQLite file, or (`MySql` category) a temporary MySQL database (repositories, `AuthService`, `BookingService`, migration, `DatabaseSelector`). |
| `System` | Drives the real `PetSitters.exe` end to end through UI Automation (FlaUI). |
| `Acceptance` | System tests that walk a requirement's acceptance criteria (REQ-xx-nn) as a user would. |
| `Regression` | Re-run after every build to catch breakage: the whole UI suite, plus both migration (data-loss guard) tests. |
| `Security` | Hashing/salting, no plaintext, no user enumeration, data isolation and authorisation (acting on someone else's data), and the database password never appearing in errors or the tooltip. |
| `Performance` | Checked against a budget: sign-in time (< 1 s, REQ-NFR-02), or database round trips (a list loads in ONE query, counted by `Database.ConnectionsOpened`, since round trips decide speed on the cloud database). |
| `Usability` | Checks the user is told what to do (specific validation messages, the Owner/Sitter prompt). |
| `Smoke` | The 12-case sanity slice through the core journey and the launch-time database choice, run first by the build and CI (see [CI.md](CI.md)). SQLite only: the gate's filter is `TestCategory=Smoke&TestCategory!=MySql`. |
| `MySql` | Opt-in MySQL parity run against a remote MySQL server (see [MySQL parity](#mysql-parity-opt-in)). Excluded from the build gate and CI; skipped unless opted in. |

**Scenario**: what kind of input or condition the test covers:

| Label | Meaning here |
|-------|--------------|
| `Positive` | Valid input → expected success. |
| `Negative` | Well-formed input that a business rule correctly refuses (wrong password, overlap, duplicate, not your booking). |
| `Boundary` | Values on and either side of a limit (6-char password, 0–11 months, 1 hour, 14 days, back-to-back dates, today). |
| `InvalidInput` | Malformed, missing or out-of-range input (bad email, blank fields, non-numbers, negative values, no pet). |
| `ErrorHandling` | Genuine error conditions handled safely: null/missing/tampered stored data, missing records, a database constraint violation, failed sign-in, nothing saved on rejection, and an unreachable or misconfigured cloud database (fallback). |

A test can carry several labels; data-driven tests that mix valid and invalid
rows are labelled with each kind they contain.

### Coverage by label

| Label | Methods | Executed cases |
|-------|--------:|---------------:|
| Unit | 50 | 163 |
| Integration | 98 | 125 |
| System | 8 | 8 |
| Acceptance | 6 | 6 |
| Regression | 10 | 10 |
| Security | 29 | 54 |
| Performance | 4 | 4 |
| Usability | 2 | 2 |
| Smoke | 12 | 12 |
| Accessibility | 0 | 0 |
| Positive | 96 | 197 |
| Negative | 47 | 89 |
| Boundary | 30 | 110 |
| InvalidInput | 30 | 107 |
| ErrorHandling | 19 | 29 |

(Totals across labels exceed the 288 logic + 8 UI cases because tests carry several labels. The 121 inherited MySQL parity cases are not counted here: they repeat these tests' labels.)

**Known gaps (stated, not hidden):**
- **Accessibility: none.** REQ-NFR-04 isn't implemented or assessed yet; the
  planned method is a Nielsen-heuristic review, which is manual.
- **Usability is thin.** The two UI tests only check that messages are shown;
  the planned KLM benchmark for REQ-NFR-01 is not automated.
- **Performance covers sign-in time and query counts only.** The high-load E2E test in the milestone
  plan does not exist yet. The build gate checks the sign-in budget on SQLite
  only; the opt-in parity run also checks it on MySQL. Cloud-mode screen times
  are not measured by an automated test: the before/after figures in
  docs/Architecture.md were measured once with a temporary harness. What IS
  automated is the cause: tests assert that each list loads in a single query
  (`Database.ConnectionsOpened`).
- **MySQL parity is not in the build gate or CI.** It is opt-in, so a
  MySQL-only regression is caught only when someone runs it (last run
  2026-10-07, 121/121 passing). It also depends on the network: two runs that
  day lost cases to connection timeouts and passed when re-run.
- **The real cloud path at launch is not automated.** `DatabaseSelectorTests`
  simulate a reachable cloud with SQLite, and the UI tests only exercise the
  local and unreachable cases. Launching against the real cloud database was
  checked by hand (the cloud header appeared about 2.4 s after launch).
- **Not tested:** the mid-session database-failure message
  (`App.DispatcherUnhandledException`), and the build step that copies `.env`
  next to the app.

### Running by label

```
dotnet test PetSitters.Tests -c Debug --filter "TestCategory=Boundary"
dotnet test PetSitters.Tests -c Debug --filter "TestCategory=Security|TestCategory=ErrorHandling"
dotnet test PetSitters.Tests -c Debug --filter "TestCategory=Unit&TestCategory=InvalidInput"
dotnet test PetSitters.Tests -c Debug --filter "TestCategory!=MySql"    # what the build gate and CI run
dotnet test PetSitters.Tests -c Debug --filter "TestCategory=MySql"     # parity run; needs the opt-in variable
```

In Visual Studio, Test Explorer → **Group By → Traits** shows the same labels.

### Appendix: every test and its labels

Cases = executed cases (`[DataRow]` count, or 1). The UI suite's extra `EndToEnd` tag is omitted.

| Class | Test | Cases | Type | Scenario |
|-------|------|------:|------|----------|
| `AuthServiceTests` | `Register_WithValidDetails_Succeeds` | 1 | Smoke, Integration | Positive |
| `AuthServiceTests` | `Register_WithInvalidEmail_Fails` | 3 | Integration | InvalidInput, Negative |
| `AuthServiceTests` | `Register_WithWeakPassword_Fails` | 1 | Integration, Security | Boundary, InvalidInput |
| `AuthServiceTests` | `Register_WithEmptyName_Fails` | 1 | Integration | InvalidInput |
| `AuthServiceTests` | `Register_WithEmptyPhone_Fails` | 2 | Integration | InvalidInput |
| `AuthServiceTests` | `Register_WithEmptyLocation_Fails` | 2 | Integration | InvalidInput |
| `AuthServiceTests` | `Register_WithAllFieldsSupplied_PersistsPhoneAndLocation` | 1 | Integration | Positive |
| `AuthServiceTests` | `Register_DuplicateEmail_Fails_CaseInsensitive` | 1 | Integration | Negative |
| `AuthServiceTests` | `Register_StoresHashedPassword_NotPlainText` | 1 | Integration, Security | Positive |
| `AuthServiceTests` | `Login_WithCorrectCredentials_SucceedsWithinPerformanceBudget` | 1 | Smoke, Integration, Performance | Positive |
| `AuthServiceTests` | `Login_WithWrongPassword_Fails` | 1 | Integration, Security | Negative |
| `AuthServiceTests` | `Login_WithMissingInput_Fails` | 3 | Integration | InvalidInput, Negative |
| `AuthServiceTests` | `Login_DoesNotRevealWhetherEmailIsRegistered` | 1 | Integration, Security | Negative, ErrorHandling |
| `BookingCalculationTests` | `Nights_IsDateSpan_WithMinimumOfOne` | 4 | Unit | Positive, Boundary |
| `BookingCalculationTests` | `EstimatedTotal_IsNightsTimesDailyRate` | 4 | Unit | Positive, Boundary |
| `BookingOverlapTests` | `RangesOverlap_AgainstAcceptedBooking` | 11 | Unit | Positive, Negative, Boundary |
| `BookingOverlapTests` | `RangesOverlap_IsSymmetric` | 1 | Unit | Positive |
| `BookingOverlapTests` | `SharesPetWith_TreatsAllMyPetsAsEveryPet` | 5 | Unit | Positive, Negative |
| `BookingOverlapTests` | `IsActive_OnlyForPendingAndAccepted` | 4 | Unit | Positive, Negative |
| `BookingOverlapTests` | `RangesOverlap_IgnoresTimeOfDay` | 1 | Unit | Boundary |
| `BookingRepositoryTests` | `Insert_BookingIsVisibleToBothOwnerAndSitter` | 1 | Smoke, Integration | Positive |
| `BookingRepositoryTests` | `UpdateStatus_Accept_IsPersisted` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `UpdateStatus_Decline_IsPersisted` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `UpdateStatus_Cancel_IsPersistedFromEitherStage` | 2 | Integration | Positive |
| `BookingRepositoryTests` | `UpdateStatus_Cancel_RemovesBookingFromSittersPendingQueue` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `GetForSitter_DoesNotReturnAnotherSittersBookings` | 1 | Integration, Security | Negative |
| `BookingRepositoryTests` | `Insert_PreservesDailyRateSnapshot` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `GetDetailsForOwner_IncludesSitterAndPet` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `GetDetailsForSitter_AllMyPetsBooking_HasNoPet_ButIsStillListed` | 1 | Integration | Positive, Boundary |
| `BookingRepositoryTests` | `GetDetails_JoinedUsers_DoNotCarryPasswordHashes` | 1 | Integration, Security | Negative |
| `BookingRepositoryTests` | `GetDetailsForSitter_ExcludesOtherSittersBookings` | 1 | Integration, Security | Negative |
| `BookingRepositoryTests` | `GetDetails_IsOneRoundTrip_RegardlessOfRowCount` | 1 | Integration, Performance | Positive |
| `BookingRepositoryTests` | `TryUpdateStatus_FromAnExpectedStatus_Changes_AndRaisesTheEvent` | 1 | Integration | Positive |
| `BookingRepositoryTests` | `TryUpdateStatus_WhenAlreadyChangedByTheOtherParty_ChangesNothing` | 1 | Integration | Negative, ErrorHandling |
| `BookingRepositoryTests` | `Insert_StoresBookingDatesWithoutATimeZoneOffset` | 1 | Integration | Positive, Boundary |
| `BookingRepositoryTests` | `GetById_LegacyRowWithAnOffset_ReadsTheDateAsWritten` | 1 | Integration | Boundary, Positive |
| `BookingServiceTests` | `AcceptRequest_WithNoClash_AcceptsAndPersists` | 1 | Smoke, Integration | Positive |
| `BookingServiceTests` | `AcceptRequest_OverlappingAnAcceptedBooking_IsRejectedAndStaysPending` | 1 | Integration | Negative |
| `BookingServiceTests` | `AcceptRequest_BackToBackWithAcceptedBooking_IsAccepted` | 1 | Integration | Positive, Boundary |
| `BookingServiceTests` | `AcceptRequest_OverlappingANonAcceptedBooking_IsAccepted` | 3 | Integration | Positive |
| `BookingServiceTests` | `AcceptRequest_OverlapWithAnotherSittersBooking_IsAccepted` | 1 | Integration | Positive |
| `BookingServiceTests` | `AcceptRequest_ForAnotherSittersBooking_IsRejected` | 1 | Integration, Security | Negative |
| `BookingServiceTests` | `AcceptRequest_ForANonPendingBooking_IsRejected` | 1 | Integration | Negative |
| `BookingServiceTests` | `RequestBooking_WithNoClash_IsStoredAsPending` | 1 | Integration | Positive |
| `BookingServiceTests` | `RequestBooking_SamePetOverlappingLiveBooking_IsRejectedAndNotStored` | 2 | Integration | Negative |
| `BookingServiceTests` | `RequestBooking_SamePetOverlappingInactiveBooking_IsAllowed` | 2 | Integration | Positive |
| `BookingServiceTests` | `RequestBooking_DifferentPetSameDates_IsAllowed` | 1 | Integration | Positive |
| `BookingServiceTests` | `RequestBooking_SamePetBackToBack_IsAllowed` | 1 | Integration | Positive, Boundary |
| `BookingServiceTests` | `RequestBooking_AllMyPets_ClashesWithAnyPet` | 2 | Integration | Negative |
| `BookingServiceTests` | `RequestBooking_AnotherOwnersBooking_DoesNotBlock` | 1 | Integration | Positive |
| `BookingServiceTests` | `CancelBooking_FromPendingOrAccepted_IsCancelled` | 2 | Integration | Positive |
| `BookingServiceTests` | `CancelBooking_FromDeclinedOrCancelled_IsRejected` | 2 | Integration | Negative |
| `BookingServiceTests` | `CancelBooking_ByAnyoneButTheOwner_IsRejected` | 1 | Integration, Security | Negative |
| `BookingServiceTests` | `CancelBooking_ThenRebookSamePetAndDates_IsAllowed` | 1 | Integration | Positive |
| `BookingServiceTests` | `DeclineRequest_Pending_IsDeclined` | 1 | Integration | Positive |
| `BookingServiceTests` | `DeclineRequest_AfterTheOwnerCancelled_IsRefused_AndStaysCancelled` | 1 | Integration | Negative |
| `BookingServiceTests` | `AcceptRequest_AfterTheOwnerCancelled_ReportsChangedElsewhere` | 1 | Integration | Negative |
| `BookingServiceTests` | `CancelBooking_AfterTheSitterDeclined_ReportsChangedElsewhere` | 1 | Integration | Negative |
| `BookingServiceTests` | `DeclineRequest_NotYours_IsAPlainRefusal` | 1 | Integration, Security | Negative |
| `BookingServiceTests` | `DeclineRequest_ForAnotherSittersBooking_IsRefused` | 1 | Integration, Security | Negative |
| `BookingServiceTests` | `CancelAsSitter_OnlyFromAccepted` | 3 | Integration | Positive, Negative |
| `BookingServiceTests` | `CancelAsSitter_ByAnotherSitter_IsRefused` | 1 | Integration, Security | Negative |
| `BookingValidationTests` | `Validate_StartDate_RelativeToToday` | 3 | Integration | Positive, Boundary, InvalidInput |
| `BookingValidationTests` | `Validate_EndNotAfterStart_IsRejected` | 2 | Integration | Boundary, InvalidInput |
| `BookingValidationTests` | `Validate_MinimumDuration_Boundary` | 3 | Integration | Positive, Boundary, InvalidInput |
| `BookingValidationTests` | `Validate_MaximumDuration_Boundary` | 3 | Integration | Positive, Boundary, InvalidInput |
| `BookingValidationTests` | `Validate_FourteenDaysAndOneMinute_IsRejected` | 1 | Integration | Boundary, InvalidInput |
| `BookingValidationTests` | `Validate_AllMyPets_WhenOwnerHasNoPets_IsRejected` | 1 | Integration | InvalidInput, Negative |
| `BookingValidationTests` | `Validate_AllMyPets_WhenOwnerHasPets_IsAllowed` | 1 | Integration | Positive |
| `BookingValidationTests` | `Validate_PetBelongingToAnotherOwner_IsRejected` | 1 | Integration, Security | InvalidInput |
| `BookingValidationTests` | `RequestBooking_ValidSubmission_IsStoredAsPending` | 1 | Smoke, Integration | Positive |
| `BookingValidationTests` | `RequestBooking_InvalidSubmission_IsRejectedAndNotStored` | 1 | Integration | InvalidInput, ErrorHandling |
| `ChatPersistenceTests` | `Message_IsPersisted_AndReadBackByAFreshRepository` | 1 | Smoke, Integration | Positive |
| `ChatPersistenceTests` | `GetForBooking_ReturnsOnlyThatBookingsMessages` | 1 | Integration, Security | Negative |
| `ChatPersistenceTests` | `GetForBooking_ReturnsMessagesInChronologicalOrder` | 1 | Integration | Positive |
| `ChatPersistenceTests` | `GetForBookingWithSenderNames_OneQuery_InOrder_WithNames` | 1 | Integration, Performance | Positive |
| `DatabaseConfigurationTests` | `EnvFile_ParsesKeyValuePairs_IgnoringCommentsAndBlanks` | 1 | Unit, Smoke | Positive |
| `DatabaseConfigurationTests` | `EnvFile_ValueFormats` | 6 | Unit | Positive, Boundary |
| `DatabaseConfigurationTests` | `EnvFile_MalformedLines_AreIgnored` | 1 | Unit | InvalidInput, ErrorHandling |
| `DatabaseConfigurationTests` | `EnvFile_MissingFile_MeansNoSettings` | 1 | Unit | ErrorHandling |
| `DatabaseConfigurationTests` | `AppConfig_EnvironmentVariable_OverridesEnvFile` | 1 | Unit | Positive |
| `DatabaseConfigurationTests` | `AppConfig_EnvironmentVariableNames_AreCaseInsensitive` | 1 | Unit | Positive, Boundary |
| `DatabaseConfigurationTests` | `AppConfig_DatabaseMode` | 6 | Unit | Positive, InvalidInput |
| `DatabaseConfigurationTests` | `AppConfig_ConnectTimeout_IsBounded` | 5 | Unit | Boundary, InvalidInput |
| `DatabaseConfigurationTests` | `AppConfig_NoUrl_MeansNoCloudDatabase` | 1 | Unit | Positive |
| `DatabaseConfigurationTests` | `MySqlUrl_ParsesEveryPart` | 1 | Unit, Smoke | Positive |
| `DatabaseConfigurationTests` | `MySqlUrl_NoPort_UsesMySqlDefault_AndNoSslMode_RequiresTls` | 1 | Unit | Boundary |
| `DatabaseConfigurationTests` | `MySqlUrl_SslCa_IsPassedToTheDriver` | 1 | Unit | Positive |
| `DatabaseConfigurationTests` | `MySqlUrl_PercentEncodedCredentials_AreDecoded` | 1 | Unit | Positive |
| `DatabaseConfigurationTests` | `MySqlUrl_SslModes` | 5 | Unit | Positive |
| `DatabaseConfigurationTests` | `MySqlUrl_InvalidUrl_FailsWithoutLeakingThePassword` | 7 | Unit, Security | InvalidInput, ErrorHandling |
| `DatabaseMigrationTests` | `Initialize_OnPreGr06Database_KeepsAllDataAndAllowsSecondRole` | 1 | Integration, Regression | Positive |
| `DatabaseMigrationTests` | `Initialize_RunTwice_IsIdempotent` | 1 | Smoke, Integration, Regression | Positive |
| `DatabaseSelectorTests` | `Select_CloudReachable_UsesCloud` | 1 | Integration, Smoke | Positive |
| `DatabaseSelectorTests` | `Select_CloudUnreachable_FallsBackToLocal_AndExplains` | 1 | Integration, Smoke | Negative, ErrorHandling |
| `DatabaseSelectorTests` | `Select_FailureAfterConnecting_IsReportedAsError_NotOffline` | 1 | Unit | ErrorHandling, Negative |
| `DatabaseSelectorTests` | `IsConnectivityFailure_Classification` | 6 | Unit | Positive, Negative |
| `DatabaseSelectorTests` | `Select_CloudFailureMessage_NeverContainsThePassword` | 1 | Unit, Security | ErrorHandling |
| `DatabaseSelectorTests` | `Select_MalformedUrl_FallsBackToLocal` | 1 | Unit | InvalidInput, ErrorHandling |
| `DatabaseSelectorTests` | `Select_ForcedLocal_NeverTriesTheCloud` | 1 | Unit | Positive |
| `DatabaseSelectorTests` | `Select_NoUrl_UsesLocalWithoutWarning` | 1 | Unit | Positive |
| `LocalImagesTests` | `TrustedPathOrNull_FileDirectlyInTheImageFolder_IsTrusted` | 1 | Unit | Positive |
| `LocalImagesTests` | `TrustedPathOrNull_AnythingElse_IsNull` | 10 | Unit, Security | InvalidInput, Negative |
| `LocalImagesTests` | `TrustedPathOrNull_StartsInsideButEscapesOrNests_IsNull` | 6 | Unit, Security | Boundary |
| `LocalImagesTests` | `TrustedPathOrNull_FolderInDifferentCase_IsTrusted` | 1 | Unit | Positive, Boundary |
| `PasswordHasherTests` | `CreateHash_ThenVerifyWithCorrectPassword_ReturnsTrue` | 1 | Smoke, Unit, Security | Positive |
| `PasswordHasherTests` | `Verify_WithWrongPassword_ReturnsFalse` | 1 | Unit, Security | Negative |
| `PasswordHasherTests` | `CreateHash_IsSalted_SamePasswordProducesDifferentHashes` | 1 | Unit, Security | Positive |
| `PasswordHasherTests` | `CreateHash_DoesNotStorePasswordInPlainText` | 1 | Unit, Security | Positive |
| `PasswordHasherTests` | `Verify_WithTamperedHash_ReturnsFalse` | 1 | Unit, Security | Negative, ErrorHandling |
| `PasswordHasherTests` | `Verify_WithMissingStoredHashOrSalt_ReturnsFalse` | 2 | Unit, Security | InvalidInput, ErrorHandling |
| `PetAgeTests` | `FormatAge_CombinesYearsAndMonths` | 8 | Unit | Positive, Boundary |
| `PetAgeTests` | `AgeDisplay_UsesTheStoredYearsAndMonths` | 1 | Unit | Positive |
| `PetAgeTests` | `AgeMonths_DefaultsToZero_WhenNotSupplied` | 1 | Unit | Positive |
| `UserRepositoryTests` | `Insert_AssignsId_AndCanBeFoundByEmailAndId` | 1 | Integration | Positive |
| `UserRepositoryTests` | `EmailExists_IsCaseInsensitive` | 1 | Integration | Positive, Negative |
| `UserRepositoryTests` | `GetByRole_ReturnsOnlyThatRole_OrderedByName` | 1 | Integration | Positive |
| `UserRepositoryTests` | `GetSittersWithProfiles_OneQuery_IncludesSittersWithoutProfiles` | 1 | Integration, Performance | Positive |
| `UserRepositoryTests` | `UpdateDetails_DoesNotTouchTheProfilePicture` | 1 | Integration | Negative |
| `UserRepositoryTests` | `UpdateDetails_PersistsEditedFields` | 1 | Integration | Positive |
| `PetRepositoryTests` | `ImagePath_OutsideTheLocalImageFolder_ReadsBackAsNull` | 2 | Integration, Security | Negative |
| `PetRepositoryTests` | `ImagePath_InsideTheLocalImageFolder_IsKept` | 1 | Integration | Positive |
| `PetRepositoryTests` | `Insert_ThenGetByOwner_ReturnsThePets` | 1 | Integration | Positive |
| `PetRepositoryTests` | `Insert_PersistsYearsAndOptionalMonths` | 1 | Integration | Positive |
| `PetRepositoryTests` | `Insert_DefaultsMonthsToZero_WhenNotSupplied` | 1 | Integration | Positive |
| `PetRepositoryTests` | `Delete_RemovesOnlyTheSelectedPet` | 1 | Integration | Positive |
| `SitterProfileRepositoryTests` | `Upsert_InsertsProfile_WhenNoneExists` | 1 | Integration | Positive |
| `SitterProfileRepositoryTests` | `Upsert_UpdatesInPlace_WhenProfileAlreadyExists` | 1 | Integration | Positive |
| `SitterProfileRepositoryTests` | `GetByUserId_ReturnsNull_WhenSitterHasNoProfileYet` | 1 | Integration | ErrorHandling |
| `SharedEmailTests` | `Register_SameEmailForTheOtherRole_Succeeds` | 2 | Integration | Positive |
| `SharedEmailTests` | `Register_SameEmailSameRole_IsRejectedWithRoleSpecificWarning` | 2 | Integration | Negative |
| `SharedEmailTests` | `Register_WhenBothRolesExist_RejectsEitherRole` | 1 | Integration | Negative |
| `SharedEmailTests` | `Insert_DuplicateEmailAndRole_IsRejectedByTheDatabase` | 1 | Integration | Negative, ErrorHandling |
| `SharedEmailTests` | `IsUniqueViolation_OtherConstraintErrors_AreNotDuplicates` | 1 | Integration | Negative, ErrorHandling |
| `SharedEmailTests` | `Login_SharedEmailSamePassword_AsksWhichRole` | 1 | Integration | Positive |
| `SharedEmailTests` | `Login_SharedEmailWithChosenRole_OpensThatAccount` | 2 | Integration | Positive |
| `SharedEmailTests` | `Login_SharedEmailDifferentPasswords_OpensTheMatchingAccount` | 1 | Integration | Positive |
| `SharedEmailTests` | `Login_SharedEmailFailures_UseTheGenericMessage` | 1 | Integration, Security | Negative |
| `SharedEmailTests` | `SharedEmail_AccountsAreSeparate` | 1 | Integration, Security | Positive |
| `ValidationHelperTests` | `IsValidEmail_ClassifiesInputCorrectly` | 9 | Unit | Positive, Boundary, InvalidInput |
| `ValidationHelperTests` | `IsValidEmail_EnforcesMaximumLengthBoundary` | 3 | Unit | Boundary, InvalidInput |
| `ValidationHelperTests` | `IsValidPassword_EnforcesMinimumLengthBoundary` | 4 | Unit, Security | Positive, Boundary, InvalidInput |
| `ValidationHelperTests` | `IsNonEmpty_DetectsBlankValues` | 4 | Unit | Positive, InvalidInput, ErrorHandling |
| `ValidationHelperTests` | `TryParseRate_AcceptsOnlyNonNegativeNumbers` | 7 | Unit | Positive, Boundary, InvalidInput |
| `ValidationHelperTests` | `TryParseRate_FitsTheMoneyColumn` | 5 | Unit | Boundary, InvalidInput, Positive |
| `ValidationHelperTests` | `TryParseAgeMonths_AcceptsBlankOrZeroToEleven` | 9 | Unit | Positive, Boundary, InvalidInput |
| `ValidationHelperTests` | `TryParseAgeMonths_TreatsNullAsNotSupplied` | 1 | Unit | ErrorHandling |
| `ValidationHelperTests` | `TryParseNonNegativeInt_AcceptsOnlyWholeNonNegativeNumbers` | 6 | Unit | Positive, Boundary, InvalidInput |
| `UiRegressionTests` | `BookingJourney_OwnerBooksSitterAndSitterAccepts_CompletesWithChatOpen` | 1 | System, Acceptance, Regression | Positive |
| `UiRegressionTests` | `OverlappingRequests_SitterAcceptsOne_SecondIsRefusedAndStaysPending` | 1 | System, Acceptance, Regression | Negative |
| `UiRegressionTests` | `SamePetDoubleBooking_IsRefused_UntilOwnerCancelsTheFirst` | 1 | System, Acceptance, Regression | Positive, Negative |
| `UiRegressionTests` | `SharedEmail_OwnerAlsoRegistersAsSitter_LoginAsksWhichRole` | 1 | System, Acceptance, Usability, Regression | Positive, Negative |
| `UiRegressionTests` | `BookingForm_InvalidRequests_AreRejectedWithSpecificMessages` | 1 | System, Acceptance, Usability, Regression | Positive, Boundary, InvalidInput |
| `UiRegressionTests` | `Startup_CloudDatabaseUnreachable_FallsBackToLocalAndSaysSo` | 1 | System, Acceptance, Regression | ErrorHandling, Negative |
| `UiRegressionTests` | `Startup_LocalDatabaseChosen_HeaderSaysLocalDatabase` | 1 | System, Regression | Positive |
| `UiRegressionTests` | `Login_WithUnknownCredentials_ShowsGenericErrorAndStaysOnLogin` | 1 | System, Security, Regression | Negative, ErrorHandling |
