# Java Backend Implementation Checklist

Tracking file for progress against `JAVA_BACKEND_PLAN.md`.
Last updated: 2026-02-17

## Overall Status
- [x] Phase 0: Foundation and Feature Flag
- [ ] Phase 1: Backend Skeleton + Closures + Async
- [ ] Phase 2: Naming, Packages, and Identifiers
- [ ] Phase 3: Runtime Library and Replacements
- [ ] Phase 4: Interop and Fable.Core Surface
- [ ] Phase 5: Build, Packaging, Distribution
- [x] Phase 6: Tests and CI

---

## Phase 0 — Foundation and Feature Flag

### Language/CLI surface
- [x] Add `Java` to `Language` DU (`src/Fable.AST/Plugins.fs`)
- [x] Add `Java` string rendering (`Language.ToString()`)
- [x] Accept `--lang java` in CLI parser (`src/Fable.Cli/Entry.fs`)
- [x] Mention `java` in CLI help text (`src/Fable.Cli/Entry.fs`)
- [x] Add `FABLE_COMPILER_JAVA` define (`src/Fable.Cli/Entry.fs`)

### Extensions/import pathing
- [x] Add `.java` to default extension mapping (`src/Fable.Compiler/Util.fs`)
- [x] Add Java library path extension in `getLibPath` (`src/Fable.Transforms/Transforms.Util.fs`)
- [x] Add `.java` support in output import path rewrite (`src/Fable.Compiler/Util.fs`)

### Pipeline/library wiring
- [x] Add Java pipeline branch (`src/Fable.Cli/Pipeline.fs`)
- [x] Add temporary Java backend stub with explicit error log (`src/Fable.Cli/Pipeline.fs`)
- [x] Add Java `fable-library` resolution mapping (`src/Fable.Compiler/ProjectCracker.fs`)
- [x] Resolve Java pattern matches in shared transforms (`src/Fable.Transforms/FSharp2Fable.Util.fs`, `src/Fable.Transforms/Replacements.Api.fs`)

### Tests (Phase 0)
- [x] Add compiler integration tests for Java language parsing/extension (`tests/Integration/Compiler/JavaFoundationTests.fs`)
- [x] Wire tests into integration test project (`tests/Integration/Compiler/Fable.Tests.Compiler.fsproj`, `tests/Integration/Compiler/Main.fs`)
- [x] Verify focused Java foundation tests pass (`All.Java Foundation`)

---

## Phase 1 — Backend Skeleton + Closures + Async

### 1) Backend file scaffolding and wiring
- [x] Add Java backend files to transforms project (`src/Fable.Transforms/Fable.Transforms.fsproj`)
  - [x] `Java/Java.fs`
  - [x] `Java/Fable2Java.fs`
  - [x] `Java/JavaPrinter.fs`
  - [x] `Java/Replacements.fs`

### 2) Java AST (minimal MVP for PR2)
- [x] Add minimal Java AST root type(s) for file/package/import/declarations
- [x] Keep AST narrow initially (only what printer/tests need)

### 3) Java printer (minimal MVP for PR2)
- [x] Implement `isEmpty`
- [x] Implement `run` with package/import/declaration output
- [x] Add emit macro plumbing placeholders (positional, spread, conditional)

### 4) Minimal Fable2Java lowering (PR2 scope)
- [x] Add `Compiler.transformFile` entrypoint in `Fable2Java`
- [x] Return minimal Java AST for a file
- [x] Map at least one declaration path for hello-world style output

### 5) Replacements scaffolding
- [x] Add Java replacements module scaffold
- [x] Route at least one replacement call path to Java module

### 6) Pipeline integration update
- [x] Replace stub-only Java pipeline path with transform+printer invocation

### 7) Tests (Phase 1 current)
- [x] Add initial Java printer tests (`tests/Integration/Compiler/JavaPrinterTests.fs`)
- [x] Wire `JavaPrinter` tests into test list (`tests/Integration/Compiler/Main.fs`)
- [x] Run focused Java printer tests and prove red
- [x] Implement minimum to make focused Java printer tests green
- [x] Add/verify a smoke path that reaches Java transform+printer from pipeline

### 8) Async + closures (Phase 1 full target, may span follow-up PRs)
- [x] Add `FuncN`/`ActionN` runtime interfaces in `fable-library-java`
- [x] Add async core scaffolding (`Async`, `AsyncBuilder`, `CancellationToken`, `Trampoline`)
- [x] Route async replacement entrypoints to Java runtime
- [x] Add focused async/closure tests for Java backend

---

## Phase 2 — Naming, Packages, and Identifiers
- [ ] Add Java keyword set and identifier sanitizer
- [ ] Integrate Java sanitizer in symbol naming/binding
- [ ] Add package derivation from namespace/path
- [ ] Add multi-file collision tests

## Phase 3 — Runtime Library and Replacements
- [ ] Bootstrap `src/fable-library-java/` structure
- [ ] Implement `Util`, `Types`, `FSharpOption`, `FSharpList`, `Seq`, `Array`, `Map`, `Set`, `FSharpResult`, `String`
- [ ] Expand Java replacements for primitives/collections/option/result/ref/async
- [ ] Add representative runtime-backed test subset

## Phase 4 — Interop and Fable.Core Surface
- [ ] Add `Fable.Core.Java.fs`
- [ ] Wire Java emit handling in transform utilities
- [ ] Include Java core surface in `Fable.Core.fsproj`

## Phase 5 — Build, Packaging, Distribution
- [x] Add `Fable.Build/FableLibrary/Java.fs`
- [x] Register Java library builder in build main/workspace/fsproj
- [x] Include Java library assets in `Fable.Cli.fsproj`
- [ ] Add standalone/compiler-js Java routing (optional in first pass)

## Phase 6 — Tests and CI
- [x] Add Java test project (`tests/Java/Fable.Tests.Java.fsproj`)
- [x] Add Java test/quicktest runners in `src/Fable.Build`
- [x] Add CI `build-java` job with JDK 8 baseline
- [x] Add compiler smoke tests for `--lang java` -> `.java`

---

## Notes
- Keep strict TDD sequence: red -> green -> refactor.
- Don’t weaken existing tests to hide regressions.
- Track deviations from plan here with date-stamped notes.

### 2026-02-17
- Java pipeline smoke compile verified end-to-end with `--lang java` when passing `--fableLib src/fable-library-ts` as a temporary workaround.
- Default library resolution for Java still expects built temp assets (`[temp/]fable-library-java`) and fails until Phase 5 build/library packaging wiring is implemented.
- Java-focused tests were migrated to `tests/Java` and removed from `tests/Integration/Compiler` to match backend test layout conventions.
