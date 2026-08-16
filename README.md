# LogLens

**Local-first Windows log analysis tool built with C#, .NET 10 and WPF.**

LogLens is a Windows desktop application designed to safely analyse `.log` and `.txt` files without uploading, executing, or modifying the original source.

It uses an explicit read-only source pipeline, parses log entries into a structured model, provides searchable evidence, detects deterministic patterns, exports bounded reports, and can restore the latest session using local application storage.

> **Analyse. Explain. Show evidence. Never modify the source.**

---

## Overview

LogLens was designed around one core idea:

**A log-analysis tool should help users understand evidence without becoming another risk to the source data.**

The application separates file access, parsing, querying, pattern analysis, export, persistence, and destructive actions into clearly defined boundaries.

LogLens V1 supports:

- `.log` files
- `.txt` files
- One selected source at a time
- Source files up to **25 MiB**
- Up to **100,000 parsed entries**
- Local Windows processing only

---

## Features

### Safe File Analysis

- Strict read-only source loading
- Local files only
- One source file at a time
- `.log` and `.txt` support
- 25 MiB source limit
- UNC/network/device path protections
- Reparse-point protections
- SHA-256 source inspection
- Before/after source metadata checks
- Asynchronous loading
- Cancellation support
- Strict text-encoding handling
- Friendly failure states

### Dashboard

The Dashboard presents information derived from the selected log:

- Total parsed entries
- Information entries
- Warning entries
- Error entries
- Critical / Fatal entries
- Debug / Trace entries
- Unclassified entries
- Timestamped entry counts
- Source filename
- Source size
- Detected encoding
- Read-only status
- Integrity status
- Parsing status

### Entry Explorer

The Entry Explorer allows users to investigate already-parsed log entries without reopening the source file.

Features include:

- Raw-text search
- Case-insensitive substring matching
- Severity filtering
- Multiple severity selections
- Timestamp-presence filtering
- Combined search and filters
- Accurate `Showing X of Y` counts
- Original line numbers
- Parsed timestamps
- Severity information
- Read-only raw-text inspection
- Keyboard navigation
- Search/filter state preservation during navigation

Search and filtering operate on the in-memory parsed model.

---

## Deterministic Pattern Analysis

LogLens includes explainable pattern detection based on deterministic rules.

### Repeated Messages

LogLens can identify exact repeated raw log lines.

Matching is intentionally conservative:

- Case-sensitive
- Whitespace-sensitive
- Timestamps are not removed
- Identifiers are not removed
- Numbers are not removed
- Paths are not removed

This reduces the chance of unrelated messages being grouped together.

### Severity Bursts

LogLens can identify concentrated activity within an inclusive 60-second window.

Current V1 thresholds:

- **Error / Critical:** 3 entries within 60 seconds
- **Warning:** 4 entries within 60 seconds

These findings describe **activity**, not attacks or incidents.

### Activity Windows

Where enough comparable dated timestamps exist, LogLens can identify:

- Busiest minute
- Busiest hour
- Error / Critical-heavy minute

### Evidence

Pattern findings retain supporting evidence including:

- Original line number
- Severity
- Parsed timestamp
- Message preview
- Raw text
- Navigation back to the corresponding Entry Explorer entry

---

## No Fake AI or Threat Scoring

Pattern analysis is deliberately deterministic.

LogLens V1 does **not** use:

- AI inference
- Machine learning
- Probabilistic threat scoring
- Online reputation services
- Semantic similarity
- Remote APIs
- Malware verdicts

The same input and policy produce the same ordered result.

LogLens does **not** claim that a pattern represents:

- Malware
- An attack
- A breach
- Compromise
- Malicious intent

The goal is to identify and explain observable patterns while keeping supporting evidence available to the user.

---

## Security & Privacy

LogLens is intentionally local-first.

### Source Safety

The selected source file is opened using:

```text
FileMode.Open
FileAccess.Read
```

The parser also requires a readable, non-writable stream.

Original source files are never:

- Edited
- Appended to
- Overwritten
- Renamed
- Moved
- Deleted
- Replaced
- Quarantined
- Permission-modified

LogLens treats source content as **inert text**.

That includes text resembling:

- URLs
- PowerShell commands
- CMD commands
- Scripts
- Executable names
- Registry paths
- SQL
- JavaScript
- Filesystem paths

LogLens does not execute, browse, invoke, compile, or follow those strings.

---

## No Hidden Network Behaviour

LogLens V1 contains:

- No telemetry
- No analytics
- No advertising
- No account system
- No login
- No cloud processing
- No networking
- No remote API requests
- No crash-report uploads
- No process execution
- No shell execution
- No registry modification
- No Windows service control
- No privilege elevation

Processing happens locally on the user's Windows device.

---

## Architecture

LogLens uses a three-project architecture:

```text
LogLens.App
├── WPF user interface
├── Navigation
├── Windows dialogs
├── Accessibility
└── UI orchestration

LogLens.Core
├── Source validation
├── Strict read-only loading
├── Source integrity inspection
├── Encoding validation
├── Parsing
├── Severity detection
├── Timestamp detection
├── Search and filtering
├── Pattern analysis
├── Report generation
├── Export destination protection
├── Local session persistence
└── Scoped application-data erasure

LogLens.Core.Tests
├── Unit tests
├── Boundary tests
├── Regression tests
├── Integrity tests
├── Export tests
└── Persistence / erase tests
```

This separation keeps the main analysis logic independent from the WPF interface and makes safety-sensitive file operations easier to locate, test, and audit.

---

## Data Flow

```text
User selects a log
        ↓
Source validation
        ↓
Strict read-only loader
        ↓
Encoding + integrity inspection
        ↓
Plain-text parser
        ↓
Normalised log entries
        ↓
LogAnalysisResult
        ↓
 ┌───────────────┬────────────────┬─────────────────┐
 ↓               ↓                ↓
Dashboard      Entries          Patterns
                 ↓                ↓
          Search / Filters      Evidence
                 └────────┬───────┘
                          ↓
                   TXT / JSON Export
                          ↓
                 Local Session Storage
```

Downstream querying and pattern-analysis services receive already-parsed models rather than permission to reopen the source file.

---

## Parsing

LogLens conservatively extracts structured information while retaining the original evidence.

### Severity Recognition

Supported severity categories include:

- Trace
- Debug
- Info / Information
- Warn / Warning
- Error / Err
- Critical / Fatal
- Unknown

Unrecognised lines remain unclassified rather than being assigned invented information.

### Timestamp Recognition

V1 supports selected leading forms of:

- ISO 8601
- `yyyy-MM-dd HH:mm:ss`
- `dd/MM/yyyy HH:mm:ss`
- `HH:mm:ss`
- Supported bracketed equivalents
- Fractional seconds
- Explicit offsets where recognised

Missing timestamps are valid.

Malformed timestamp-like input is preserved and diagnosed rather than repaired.

Each parsed entry retains:

- Original line number
- Complete loaded raw line text
- Severity
- Parsed timestamp where available
- Extracted message information

---

## Export

LogLens supports separate local analysis exports as:

- `.txt`
- `.json`

### Exported Information

Reports can contain:

- Source filename
- Source size
- Detected encoding
- SHA-256
- Integrity information
- Parsing summary
- Severity counts
- Repeated-message findings
- Severity bursts
- Activity windows
- Bounded supporting evidence
- Diagnostics
- Relevant limitations

### Deliberately Excluded

Exports do not include:

- The complete original log
- The original source's full path
- Username
- Computer name
- Environment variables
- Browser data
- Credentials
- Authentication tokens
- Telemetry
- Invented threat scores

### Source-Overwrite Protection

The source path and selected report destination are canonicalised and compared before writing.

If the report destination resolves to the original source file, LogLens rejects the export.

Report generation and report writing are deliberately separated so the write boundary remains narrow and auditable.

---

## Local Session Persistence

LogLens can restore the latest eligible analysis session after restart.

Persistent application state is stored under:

```text
%LocalAppData%\LogLens\
```

LogLens does **not** deliberately use OneDrive, Documents, Desktop, Downloads, the source directory, or export directories for application session storage.

Persistence can restore:

- Dashboard results
- Parsed entries
- Diagnostics
- Search text
- Severity filters
- Timestamp filters
- Selected page
- Selected entry
- Pattern analysis through deterministic reconstruction
- Export readiness

### Why Raw Text Is Stored Locally

Raw parsed log text is required to restore:

- Entry Explorer
- Search
- Pattern evidence
- Pattern analysis
- Exports

Without the raw entries, LogLens could only restore summary counts.

The persisted session:

- Stays local
- Is bounded
- Is not transmitted
- Is not encrypted in V1
- Can be erased by the user

Only the latest eligible session is retained.

---

## Restored Sessions Are Snapshots

A restored session represents the state LogLens analysed previously.

LogLens does **not** silently reopen or revalidate the original source file when restoring a saved session.

Therefore:

- Restored SHA-256 information belongs to the original analysis
- Restored integrity information belongs to the original analysis
- The source may have moved after the previous session
- The source may have been deleted after the previous session
- The source may have changed after the previous session

To inspect the current state of a source file, the user should deliberately open it again.

---

## Erase All LogLens Data

LogLens includes a deliberate four-stage application-data erase flow.

The stages explain:

1. What LogLens will erase
2. What LogLens will leave untouched
3. The exact application-owned storage boundary
4. Final typed authorization

The final stage requires the exact phrase:

```text
ERASE LOGLENS DATA
```

The erase component is restricted to:

```text
%LocalAppData%\LogLens\
```

It cannot accept an arbitrary deletion path.

The erase operation does **not** delete:

- Original source logs
- Exported TXT reports
- Exported JSON reports
- Documents
- Desktop files
- OneDrive files
- Project files
- Browser data
- Files belonging to unrelated applications

This feature is intended for local privacy cleanup.

It is **not** a forensic secure-wipe implementation.

---

## Storage Bounds

V1 intentionally prevents local session data from growing indefinitely.

Current persistence behaviour includes:

- Latest session only
- Maximum persisted raw-text size
- Maximum serialised session size
- Bounded saved search state
- Existing parsing limits remain enforced
- Oversized sessions can continue working in memory without being persisted

This keeps the restore feature useful without creating uncontrolled local storage growth.

---

## Testing

### Final V1 Automated Verification

```text
223 tests passed
0 failed
0 skipped

Release build:
0 warnings
0 errors
```

Automated testing covers areas including:

- Source validation
- Strict read-only access
- Source-byte preservation
- Source metadata preservation
- SHA-256
- Unsupported paths
- Missing files
- Locked files
- Malformed input
- Invalid encoding
- Unicode
- Cancellation
- Long lines
- Severity recognition
- Timestamp recognition
- Raw-text preservation
- Search
- Severity filtering
- Timestamp filtering
- Combined filtering
- Pattern analysis
- Evidence integrity
- TXT export
- JSON export
- Source-overwrite rejection
- Local persistence
- Session restoration
- Corrupt persistence data
- Storage boundaries
- Atomic persistence behaviour
- Four-stage erase confirmation
- Source preservation during erase
- Export preservation during erase
- Regression behaviour across previous milestones

Existing tests were retained as later milestones were added so new functionality could be checked against previous behaviour.

---

## Manual Verification

Automated testing was supplemented with direct Windows UI testing.

Manual checks included:

- Application launch
- Navigation
- Native file picker
- Drag and drop
- Dashboard
- Empty logs
- Malformed timestamps
- Unicode
- Command-looking text
- URL-looking text
- Long lines
- Search
- Severity filters
- Timestamp filters
- Combined filtering
- No-match states
- Pattern evidence
- Repeated-message detection
- Error / Critical bursts
- Warning bursts
- Activity windows
- View in Entries
- TXT export
- JSON export
- Save-dialog cancellation
- Source-overwrite protection
- Sidebar contrast
- Session persistence
- Restart restoration
- Export from restored state
- Four-stage erase flow

Manual testing remains important because automated Core tests do not replace observing native WPF and Windows behaviour directly.

---

## Technology

- **C#**
- **.NET 10**
- **WPF**
- **XAML**
- **MSTest**
- **System.Text.Json**

Production functionality uses standard .NET / WPF libraries.

There are no third-party production package dependencies.

---

## V1 Limits

LogLens V1 intentionally focuses on a narrow, explainable scope.

### Input

- `.log` and `.txt` only
- Maximum source size: **25 MiB**
- Maximum parsed entries: **100,000**
- No dedicated JSON structural parser
- No dedicated CSV structural parser
- No legacy ANSI/code-page support
- UTF-32 unsupported
- Limited documented timestamp formats

### Search & UI

- Search uses deterministic case-insensitive substring matching
- No regex search
- No fuzzy search
- No semantic search
- Original source order is retained
- List previews are bounded
- Entry detail display is bounded while Core retains the complete loaded raw line within overall limits

### Pattern Analysis

- Repeated messages require exact case-sensitive raw-text matches
- Repeat threshold is fixed
- Error / Critical burst threshold is fixed
- Warning burst threshold is fixed
- Burst window is a fixed inclusive 60 seconds
- Activity windows use fixed calendar buckets
- Pattern output is bounded for presentation
- Patterns identify repetition and timing rather than malicious intent

### Persistence

- Latest session only
- Persisted local JSON is not encrypted
- Very large sessions may not be persisted
- Restored sessions are snapshots rather than live source validation

### Export

- TXT and JSON only
- Reports are bounded summaries rather than complete log copies
- Exports are not encrypted
- Exports are not digitally signed
- Exports are not automatically uploaded or opened

---

## What LogLens Is Not

LogLens is **not**:

- An antivirus product
- An EDR platform
- A malware scanner
- A SIEM replacement
- An incident-response platform
- A threat-intelligence service
- A security certification
- A guarantee of zero bugs

The project deliberately documents its limitations instead of hiding them.

---

## Development Approach

LogLens was developed through a structured **AI-assisted engineering workflow**.

### Project Ownership

**Harnoor Singh** was responsible for:

- Project ownership
- Product direction
- Requirements
- Safety boundaries
- Milestone scope
- Design decisions
- Manual verification
- Acceptance testing
- Final product decisions

### AI Assistance

ChatGPT assisted with:

- Architecture planning
- Milestone design
- Safety review
- Testing strategy
- Technical explanations
- Documentation planning

Codex assisted with:

- Implementation
- Compatibility checks
- Automated testing
- Regression verification
- Technical reporting

AI was used as an engineering multiplier rather than a substitute for ownership, verification, or decision-making.

Requirements were explicit, development was milestone-based, regression tests were retained, safety boundaries were repeatedly reviewed, and limitations remained documented throughout V1 development.

---

## Project Status

**LogLens V1 core development is complete.**

V1 focuses on:

- Safe local log inspection
- Explainable deterministic analysis
- Traceable evidence
- Strict source-file protection
- Local-first privacy
- Testable architecture
- Controlled persistence
- Explicit user-controlled exports

Future versions may explore carefully scoped improvements after V1 review.

---

## Download

Pre-built Windows releases of LogLens will be available through the repository's **Releases** section.

The repository and compiled release packages serve different purposes:

- **Repository:** source code, tests, architecture, and development history
- **Releases:** downloadable Windows builds for users who simply want to run LogLens

---

## Author

**Harnoor Singh**

Built as a practical software engineering, cybersecurity, and defensive-development portfolio project.

---

## Copyright

**Copyright © 2026 Harnoor Singh. All rights reserved.**

No open-source license is granted for this repository.

Unless explicit written permission is provided by the copyright holder, no permission is granted to reproduce, redistribute, modify, republish, sublicense, or commercially use the source code contained in this repository.

Public availability of this repository does not itself grant permission to reuse the source code beyond rights provided by applicable law and GitHub's platform terms.
