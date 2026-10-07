# Sitters4Us — Documentation

Technical and QA documentation for the Sitters4Us (PetSitters) prototype — a WPF pet owner ↔
pet sitter marketplace that uses a shared cloud MySQL database with a local
SQLite fallback, built for ENSE707 Software Quality Assurance. Runtime settings
are documented in [`.env.example`](../.env.example).

| Document | Contents |
|----------|----------|
| [Architecture.md](Architecture.md) | Application architecture: layers, screens, data model (schema + ERD), key workflows, storage (cloud MySQL with local fallback: startup selection, configuration, MySQL vs SQLite schema, pool settings, failure handling), security and known limitations, and build/run instructions. |
| [CI.md](CI.md) | Development and integration workflow, CI pipeline (GitHub Actions), quality gates (including the no-secrets gate), the smoke set, the opt-in MySQL parity tests, and why the UI suite runs locally. |
| [UnitTests.md](UnitTests.md) | The `PetSitters.Tests` suite: test inventory, design techniques (ENSE707 Lab 1–5), MySQL parity and REQ-GR-09 tests, a requirements traceability matrix (RTM), test classification, and how to run the tests. |

See also [`CLAUDE.md`](../CLAUDE.md) in the repo root for a quick orientation and
the build/test tooling notes.
