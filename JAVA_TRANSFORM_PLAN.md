# Fable Java Transform Implementation Plan

## 1. Objective and Scope
Implement a new Fable backend that transpiles F# (via Fable AST) to Java source code, with end-to-end support in:
- `Fable.Transforms` backend pipeline
- `Fable.Cli` (`--lang java`)
- library resolution and packaging (`fable-library-java`)
- build/test automation (`src/Fable.Build`, CI)
- optional standalone/compiler-js language routing

This plan is phased so we can ship an MVP quickly, then expand language coverage.

## 2. Current Architecture Baseline (What Exists)
The repository already has per-language backend patterns that Java should follow:

1. Language enum and options:
- `src/Fable.AST/Plugins.fs`
- `src/Fable.Transforms/Global/Compiler.fs`

2. Backend dispatch:
- CLI compile pipeline: `src/Fable.Cli/Pipeline.fs`
- CLI language parsing/status/macros: `src/Fable.Cli/Entry.fs`
- Standalone target transform+print routing: `src/fable-standalone/src/Main.fs`
- JS host routing: `src/fable-compiler-js/src/app.fs`

3. Transform backend structures:
- Python backend: `src/Fable.Transforms/Python/*`
- Dart backend: `src/Fable.Transforms/Dart/*`
- Rust backend: `src/Fable.Transforms/Rust/*`
- PHP backend: `src/Fable.Transforms/Php/*`

4. Shared language-sensitive transforms:
- naming, path, symbol sanitization: `src/Fable.Transforms/FSharp2Fable.Util.fs`, `src/Fable.Transforms/Global/Naming.fs`
- library import pathing: `src/Fable.Transforms/Transforms.Util.fs`
- call/method replacement dispatch: `src/Fable.Transforms/Replacements.Api.fs`

5. Library provisioning and copy/build:
- library selection and copy into `fable_modules`: `src/Fable.Compiler/ProjectCracker.fs`
- bundled CLI assets: `src/Fable.Cli/Fable.Cli.fsproj`
- library builders: `src/Fable.Build/FableLibrary/*.fs`

## 3. High-Level Design Decision
### Recommended backend shape
Use a **Java-specific AST + printer backend**, similar to Dart/Rust/Python, rather than emitting Java text directly from shared transforms.

Why:
- keeps transform logic testable and composable
- avoids coupling printer details into semantic lowering
- matches existing repository architecture for non-Babel targets

### Backend module layout (new)
Add `src/Fable.Transforms/Java/` with:
- `Java.fs` (AST definitions)
- `Fable2Java.fs` (Fable AST -> Java AST)
- `JavaPrinter.fs` (Java AST -> text)
- `Replacements.fs` (Java-specific runtime calls and special lowering)
- optional `README.md` documenting conventions and unsupported features

## 4. Phase-by-Phase Plan

## Phase 0: Foundation and Feature Flag
Goal: create compile-time surface area without full codegen.

1. Add `Java` language enum case and string rendering.
- File: `src/Fable.AST/Plugins.fs`

2. Add CLI language parsing alias support.
- Accept `java` and optional alias `jvm` (if desired).
- File: `src/Fable.Cli/Entry.fs`

3. Add default extension mapping.
- `.java` in `defaultFileExt`.
- Files: `src/Fable.Compiler/Util.fs`, `src/Fable.Compiler/Util.fsi`

4. Add compiler defines.
- New define: `FABLE_COMPILER_JAVA`
- File: `src/Fable.Cli/Entry.fs`

5. Add output/library path extensions for Java.
- library import path extension in `getLibPath`
- output import path rewrite support for `.java`
- Files: `src/Fable.Transforms/Transforms.Util.fs`, `src/Fable.Compiler/Util.fs`

Deliverable:
- CLI accepts `--lang java`
- pipeline compiles far enough to enter backend stub
- extension and defines behave correctly

## Phase 1: Backend Skeleton in `Fable.Transforms`
Goal: compile a minimal subset and emit valid Java for trivial programs.

1. Add Java backend files and include order in project.
- File: `src/Fable.Transforms/Fable.Transforms.fsproj`

2. Add Java replacement dispatch.
- Extend `Replacements.Api.fs` with `| Java -> Java.Replacements.*` branches.
- File: `src/Fable.Transforms/Replacements.Api.fs`

3. Implement minimal Java AST and printer.
- Support package decl, imports, class decl, static methods, literals, variable decl, return, simple calls.
- Files: `src/Fable.Transforms/Java/Java.fs`, `src/Fable.Transforms/Java/JavaPrinter.fs`

4. Implement initial `Fable2Java` lowering.
- Support: module-level functions, const/let, arithmetic, string ops, simple calls.
- File: `src/Fable.Transforms/Java/Fable2Java.fs`

5. Wire backend into CLI compiler pipeline.
- Add `Java` module similar to `Dart`/`Rust`/`Php` compile path.
- Add `match` case in `compileFile` dispatch.
- File: `src/Fable.Cli/Pipeline.fs`

Deliverable:
- simple F# file transpiles to `.java`
- no runtime/library dependency yet for tiny pure examples

## Phase 2: Language-Specific Name and Symbol Semantics
Goal: make generated Java identifiers/packages legal and deterministic.

1. Add Java identifier sanitation in naming layer.
- Java identifier-char rules and keyword list.
- `sanitizeJavaIdent` parallel to rust/dart/js helpers.
- File: `src/Fable.Transforms/Global/Naming.fs`

2. Integrate Java naming into F# -> Fable symbol binding points.
- Entity declaration naming
- Member declaration naming
- Identifier naming in scopes
- File: `src/Fable.Transforms/FSharp2Fable.Util.fs`

3. Decide module/entity mapping strategy.
Recommended initial strategy:
- each F# file compiles to one Java top-level class
- module values/functions become `static` members
- nested modules map to nested static classes or package-level class name prefixes

4. Decide package strategy.
Recommended MVP:
- derive package from project-relative path under output root
- sanitize path segments to Java package rules
- avoid default package

Deliverable:
- generated Java compiles for multi-file projects with no identifier collisions for common cases

## Phase 3: Runtime Library (`fable-library-java`) and Replacements
Goal: support core F# constructs by calling Java runtime helpers.

1. Add new library directory.
- New folder: `src/fable-library-java`
- Structure recommendation:
  - `src/fable-library-java/src/main/java/fable/library/*`
  - optional F# sources for parts compiled through Fable

2. Add library resolution logic.
- `ProjectCracker.getFableLibraryPath` mapping for Java
- default lib dir name e.g. `fable-library-java`
- File: `src/Fable.Compiler/ProjectCracker.fs`

3. Add language-specific replacements.
- New file: `src/Fable.Transforms/Java/Replacements.fs`
- Cover primitives and common runtime calls:
  - `Option`, `List`, `Seq`, `Map`, `Set`, `Result`
  - numeric conversions/parsing
  - equality/comparison semantics
  - ref/mutable cell model

4. Extend shared replacement dispatch.
- File: `src/Fable.Transforms/Replacements.Api.fs`

5. Add core Java runtime types.
MVP modules to implement first:
- `Util`, `Types`, `Option`, `List`, `Seq`, `Map`, `Set`, `String`, `Result`, `TimeSpan`, `Date`, `RegExp` (or Java regex wrapper)

Deliverable:
- representative subset of existing tests can execute using generated Java + runtime library

## Phase 4: Interop and Fable.Core Surface
Goal: provide Java-target interop ergonomics comparable to other backends.

1. Add `Fable.Core.Java.fs` and optional `Fable.Core.JavaInterop.fs`.
- patterns analogous to `Fable.Core.Dart.fs`, `Fable.Core.Rust.fs`, `Fable.Core.PyInterop.fs`

2. Decide `Emit` semantics for Java.
Options:
- conservative: limited macro emits allowed, with validation
- broader: direct Java snippet injection in expression/statement contexts

3. Add transform handling for Java-specific attributes.
- update attribute-name constants if needed in `src/Fable.Transforms/Transforms.Util.fs`

4. Include new files in `src/Fable.Core/Fable.Core.fsproj`.

Deliverable:
- usable interop API for calling Java/JDK/library APIs from F#

## Phase 5: Tooling, Build, Packaging, Distribution
Goal: make Java backend buildable and distributable like other languages.

1. Build pipeline support (`Fable.Build`).
- Add builder: `src/Fable.Build/FableLibrary/Java.fs`
- Register in:
  - `src/Fable.Build/Fable.Build.fsproj`
  - `src/Fable.Build/Main.fs` (help + command routing)
  - `src/Fable.Build/Workspace.fs`

2. CLI package content inclusion.
- include temp-built java library assets in NuGet tool package if distributing bundled library
- file: `src/Fable.Cli/Fable.Cli.fsproj`

3. Optional standalone/compiler-js support.
- `src/fable-standalone/src/Main.fs`: add result type + transform routing + print routing + `getLanguage`
- `src/fable-standalone/src/Fable.Standalone.fsproj`: include Java transform files
- `src/fable-compiler-js/src/app.fs`: language parser + extension mapping

Deliverable:
- `./build.sh fable-library --java` and `./build.sh test java` equivalents available

## Phase 6: Tests and CI
Goal: make regressions visible and keep backend shippable.

1. Add backend test project.
- new: `tests/Java/Fable.Tests.Java.fsproj`
- start with curated subset of tests from `tests/Python` or `tests/Rust`

2. Add `Fable.Build` test runner and quicktest wiring.
- `src/Fable.Build/Test/Java.fs`
- `src/Fable.Build/Quicktest/Java.fs`
- register in `src/Fable.Build/Main.fs`, `src/Fable.Build/Fable.Build.fsproj`

3. Add CI job.
- `.github/workflows/build.yml` new `build-java` job
- set up JDK toolchain and run `./build.sh test java`

4. Add compiler-level smoke tests.
- integration tests for CLI parse (`--lang java`), extension default `.java`, and successful compile of tiny fixture.

Deliverable:
- CI gate for Java backend basic correctness

## 5. MVP Feature Matrix
Target this for first usable release:
- Functions, let bindings, static module members
- Primitive types, tuples (as helper runtime type), records, simple unions
- `if/else`, loops, pattern match (limited)
- Arrays/lists/options/results basic operations
- Method calls, object creation, static calls
- Exceptions (`try/catch/finally`) basic mapping

Explicitly defer from MVP if needed:
- full async/task model parity
- advanced reflection parity
- all .NET BCL edge cases
- perfect parity for `Emit`/interop corner cases

## 6. Key Risks and Mitigations
1. Semantic gap between F# and Java (closures, DU, structural equality).
Mitigation: route through runtime helpers and keep transformations explicit in `Java/Replacements.fs`.

2. Name collision and package resolution complexity.
Mitigation: implement Java-specific sanitizer + deterministic package generation early (Phase 2 before broad test expansion).

3. Library scope explosion.
Mitigation: phase runtime modules by test-driven demand; do not attempt complete parity before MVP stabilizes.

4. Build/distribution complexity.
Mitigation: follow existing `Fable.Build/FableLibrary/*` pattern and land minimal path first.

## 7. Suggested Incremental Milestones
1. Milestone A: CLI accepts `--lang java`, backend stub emits one class with `main`-like entry for trivial program.
2. Milestone B: compile and run a quicktest project with arithmetic, functions, and collections.
3. Milestone C: stand up `fable-library-java` core modules and pass a subset test suite.
4. Milestone D: add CI job and stabilize generated code style/printer.
5. Milestone E: expand compatibility and interop API.

## 8. Definition of Done (Initial Java Backend)
- `fable --lang java` compiles multi-file projects to `.java`
- generated code compiles with configured JDK target
- runtime library is resolved automatically through `fable_modules`
- Java backend is covered by dedicated tests and CI
- documentation exists for backend limitations and supported constructs

## 9. First PR Sequence (Recommended)
1. PR1: language enum + CLI parse + extension + pipeline skeleton + empty Java backend files.
2. PR2: Java AST/printer + minimal lowering + hello-world quicktest.
3. PR3: naming sanitization + package/import correctness.
4. PR4: `fable-library-java` bootstrap + replacements for primitives/collections.
5. PR5: build/test/CI integration + standalone/compiler-js optional support.
6. PR6+: parity expansion and performance tuning.
