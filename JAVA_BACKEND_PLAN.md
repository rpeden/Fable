# Fable Java Backend — Definitive Implementation Plan

This plan combines the original architectural analysis (Codex), code review feedback, Java version strategy, and async design into a single authoritative document. It supersedes `JAVA_TRANSFORM_PLAN.md` and `JAVA_TRANSFORM_REVIEW.md`.

---

## Table of Contents

1. [Java Version Strategy](#1-java-version-strategy)
2. [Async Design — First-Class from Day One](#2-async-design--first-class-from-day-one)
3. [Architecture Baseline](#3-architecture-baseline)
4. [Key Design Decisions](#4-key-design-decisions)
5. [Phase-by-Phase Plan](#5-phase-by-phase-plan)
6. [MVP Feature Matrix](#6-mvp-feature-matrix)
7. [Risks and Mitigations](#7-risks-and-mitigations)
8. [Definition of Done](#8-definition-of-done)
9. [PR Sequence](#9-pr-sequence)

---

## 1. Java Version Strategy

### Target: Java 8 as baseline

The cost of supporting Java 8 is **generated code verbosity, not missing functionality**. Every feature we need has a Java 8-compatible fallback:

| Feature | Java 8 Fallback | Newer Java Equivalent |
|---------|----------------|----------------------|
| F# records | Class with fields + constructor + equals/hashCode/toString | `record` (Java 16+) |
| F# DUs | Abstract class + subclass per case | `sealed interface` + records (Java 17+) |
| Pattern matching | if/else chains, traditional switch | Pattern matching switch (Java 21+) |
| Async/Task | CompletableFuture (Java 8!) | Same; optionally virtual thread executors (Java 21+) |
| Closures | Custom `FuncN<>` interfaces | Same (java.util.function only goes to arity 2 regardless) |
| Type inference | Explicit types everywhere | `var` (Java 10+) |
| Text blocks | String concatenation | Text blocks (Java 13+) |

The transform layer and AST are Java-version-agnostic. Only the printer and runtime library care about the target version.

### Why Java 8 makes sense

1. **Amazon Corretto** offers fully supported LTS Java 8 through 2030+. Real users exist on this.
2. **CompletableFuture** — the critical async primitive — shipped in Java 8.
3. **Lambdas and functional interfaces** — essential for F# closure mapping — shipped in Java 8.
4. **Every other Fable backend targets the widest reasonable audience** of its platform. Python doesn't require 3.12+; Dart doesn't require 3.0+; Rust doesn't require nightly. Java shouldn't require 21+.
5. The Dart backend emits explicit classes for DUs without sealed types. The PHP backend does the same. This is a proven pattern in the Fable codebase already.

### Optional enhancements for newer JVMs

The printer could accept a `--java-target` flag (default: `8`) that enables cleaner output on newer JVMs:

- **Java 17+**: Emit `record` for F# records, `sealed interface` for DUs
- **Java 21+**: Use virtual thread executor in the async runtime library for better concurrency performance

These are quality-of-life improvements, not requirements. They can be added after MVP without changing the compiler architecture. The AST models high-level concepts ("record declaration", "union declaration"); the printer decides how to render them for the target version.

### What about Kotlin?

Targeting Kotlin instead of Java was considered. Kotlin coroutines offer excellent async semantics and the language has sealed classes, data classes, and null safety built in.

However:
- Kotlin is a **completely different backend** — it has its own syntax, type system, naming conventions, and AST shape. It's not a Java enhancement; it's a separate language.
- It adds the kotlin-stdlib dependency (~1.5MB) to all generated projects.
- It doesn't help Java backend implementation at all — the work doesn't overlap.
- Users who want Kotlin interop can call Fable-generated Java from Kotlin trivially (they're both JVM).

Verdict: **Target Java.** A Kotlin backend could be a separate future project, but it should not delay or replace the Java backend.

---

## 2. Async Design — First-Class from Day One

Async is not deferred. It is built into the architecture from Phase 1.

### How Fable async works (all backends)

Every Fable backend uses the same compilation model for `async { }` and `task { }`:

```
type Async<T> = (IAsyncContext<T>) -> void
```

`IAsyncContext<T>` carries:
- `onSuccess: (T) -> void` — called when the computation succeeds
- `onError: (Exception) -> void` — called when it throws
- `onCancel: (OperationCanceledException) -> void` — called when cancelled
- `cancelToken: CancellationToken` — checked before each continuation step
- `trampoline: Trampoline` — prevents stack overflow from deep continuation chains

The core operations:
- `protectedCont(f)` — wraps a continuation with cancellation checking and trampoline hijacking
- `protectedBind(computation, binder)` — chains two async operations (`let!`)
- `protectedReturn(value)` — lifts a value into async context
- `startAsCompletableFuture(computation)` — bridges CPS to Java's native future type

This is **proven architecture** — identical across JS (Promise bridge), Python (asyncio.Future bridge), and Rust (std::future::Future bridge). Java gets the same pattern with CompletableFuture.

### Java async runtime design

#### Core types (Java 8+)

```java
// The fundamental async type — a continuation-passing function
@FunctionalInterface
public interface Async<T> {
    void invoke(IAsyncContext<T> context);
}

// Async execution context carrying continuations + cancellation
public interface IAsyncContext<T> {
    void onSuccess(T value);
    void onError(Exception error);
    void onCancel(OperationCanceledException error);
    CancellationToken getCancelToken();
    Trampoline getTrampoline();
}

// Cancellation token with listener support
public class CancellationToken {
    private volatile boolean cancelled;
    private final Map<Integer, Runnable> listeners;
    // cancel(), addListener(), removeListener(), register()
}

// Trampoline to prevent stack overflow
public class Trampoline {
    private static final int MAX_CALL_COUNT = 2000;
    private int callCount;
    private final ScheduledExecutorService executor;

    public boolean incrementAndCheck() { /* ... */ }

    public void hijack(Runnable action) {
        this.callCount = 0;
        executor.submit(action);
    }
}
```

#### Core operations

```java
public final class AsyncModule {
    // protectedCont: wraps with cancellation check + trampoline
    public static <T> Async<T> protectedCont(Async<T> f) {
        return ctx -> {
            if (ctx.getCancelToken().isCancelled()) {
                ctx.onCancel(new OperationCanceledException());
            } else if (ctx.getTrampoline().incrementAndCheck()) {
                ctx.getTrampoline().hijack(() -> {
                    try { f.invoke(ctx); }
                    catch (Exception e) { ctx.onError(e); }
                });
            } else {
                try { f.invoke(ctx); }
                catch (Exception e) { ctx.onError(e); }
            }
        };
    }

    // protectedBind: the `let!` operation
    public static <T, U> Async<U> protectedBind(Async<T> computation, Function<T, Async<U>> binder) {
        return protectedCont(ctx ->
            computation.invoke(new AsyncContext<>(
                ctx.getTrampoline(),
                ctx.getCancelToken(),
                x -> { try { binder.apply(x).invoke(ctx); } catch (Exception e) { ctx.onError(e); } },
                ctx::onError,
                ctx::onCancel
            ))
        );
    }

    // protectedReturn: lift a value
    public static <T> Async<T> protectedReturn(T value) {
        return protectedCont(ctx -> ctx.onSuccess(value));
    }

    // Bridge to Java's native concurrency primitive
    public static <T> CompletableFuture<T> startAsCompletableFuture(
            Async<T> computation,
            CancellationToken cancelToken) {
        CompletableFuture<T> future = new CompletableFuture<>();
        startWithContinuations(computation,
            future::complete,
            future::completeExceptionally,
            ex -> future.cancel(true),
            cancelToken);
        return future;
    }
}
```

#### AsyncBuilder

```java
public class AsyncBuilder {
    public <T, U> Async<U> Bind(Async<T> computation, Function<T, Async<U>> binder) {
        return AsyncModule.protectedBind(computation, binder);
    }
    public <T> Async<T> Return(T value) {
        return AsyncModule.protectedReturn(value);
    }
    public <T> Async<T> ReturnFrom(Async<T> computation) {
        return computation;
    }
    public <T> Async<T> Delay(Supplier<Async<T>> generator) {
        return AsyncModule.protectedCont(ctx -> generator.get().invoke(ctx));
    }
    // Combine, While, For, TryWith, TryFinally, Using, Zero
}
```

#### Sleep, Parallel, Sequential

```java
public static Async<Void> sleep(int milliseconds) {
    return protectedCont(ctx -> {
        ScheduledFuture<?> timeout = scheduler.schedule(
            () -> { ctx.getCancelToken().removeListener(tokenId); ctx.onSuccess(null); },
            milliseconds, TimeUnit.MILLISECONDS);
        int tokenId = ctx.getCancelToken().addListener(() -> {
            timeout.cancel(false);
            ctx.onCancel(new OperationCanceledException());
        });
    });
}

public static <T> Async<List<T>> parallel(Iterable<Async<T>> computations) {
    // Start each as CompletableFuture, CompletableFuture.allOf(), collect results
}
```

### Java 21+ virtual thread optimization

The runtime library detects Java 21+ at runtime and swaps the executor:

```java
public class Trampoline {
    private static final ExecutorService executor = createExecutor();

    private static ExecutorService createExecutor() {
        try {
            // Java 21+: use virtual threads
            var method = Executors.class.getMethod("newVirtualThreadPerTaskExecutor");
            return (ExecutorService) method.invoke(null);
        } catch (NoSuchMethodException e) {
            // Java 8-20: use fork-join pool
            return ForkJoinPool.commonPool();
        }
    }

    public void hijack(Runnable action) {
        this.callCount = 0;
        executor.submit(action);
    }
}
```

This is a **one-spot runtime change**. The generated code is identical regardless of JVM version. The async architecture doesn't care whether the underlying threads are platform threads or virtual threads — it's the same CPS model either way.

### Why this is the right approach

| Alternative | Why not |
|------------|---------|
| **Vert.x** | Adds ~20MB dependencies. Couples output to a specific framework. Users who want Vert.x can use it via interop. |
| **Kotlin coroutines** | Requires targeting Kotlin, not Java. Different language, different backend entirely. |
| **Project Reactor / RxJava** | Heavy framework dependencies. Reactive is a programming model choice, not a compiler concern. |
| **Raw threads** | No composability. Can't express `let!` chaining without CPS or futures. |
| **Pure CompletableFuture chaining** (thenCompose) | Considered, but the existing CPS model is proven across 4 backends and handles cancellation/trampoline correctly. Rolling a different model for Java adds risk for no gain. |

The CPS + CompletableFuture bridge approach:
- Works on Java 8+
- Matches the proven pattern from JS/Python/Rust backends
- Gets virtual thread performance on Java 21+ for free
- Supports cancellation tokens correctly
- Handles deep async chains without stack overflow (trampoline)
- Doesn't add external dependencies

### Vert.x escape hatch for power users

Users who need Vert.x's event loop model can still use it via interop:

```fsharp
// F# user code using Emit/interop
[<Emit("io.vertx.core.Vertx.vertx()")>]
let createVertx(): obj = jsNative

// Or via fromContinuations bridging a Vert.x Future to Fable Async
let awaitVertxFuture (future: obj) : Async<'T> =
    Async.FromContinuations(fun (resolve, reject, _) ->
        // Bridge Vert.x Future to Fable async
        ())
```

The async infrastructure gives users `Async.FromContinuations` and `startAsCompletableFuture` — they can bridge to any JVM concurrency framework they want without us baking it in.

---

## 3. Architecture Baseline

The repository has per-language backend patterns that Java follows:

### Language enum and options
- `src/Fable.AST/Plugins.fs` — `Language` DU
- `src/Fable.Transforms/Global/Compiler.fs` — compiler options

### Backend dispatch
- CLI compile pipeline: `src/Fable.Cli/Pipeline.fs` — `compileFile` match
- CLI language parsing: `src/Fable.Cli/Entry.fs` — `--lang java` parsing
- Standalone target routing: `src/fable-standalone/src/Main.fs`
- JS host routing: `src/fable-compiler-js/src/app.fs`

### Transform backend structures (existing)
- Python: `src/Fable.Transforms/Python/*` (8 files: AST, Types, Util, Annotation, Bases, Reflection, Transforms, Printer, Compiler)
- Dart: `src/Fable.Transforms/Dart/*` (3 files: AST, Transform, Printer)
- Rust: `src/Fable.Transforms/Rust/*` (large AST module, Transform, Printer)
- PHP: `src/Fable.Transforms/Php/*` (3 files: AST, Transform, Printer)

### Shared language-sensitive transforms
- Naming/sanitization: `src/Fable.Transforms/Global/Naming.fs`, `src/Fable.Transforms/FSharp2Fable.Util.fs`
- Library import pathing: `src/Fable.Transforms/Transforms.Util.fs`
- Replacement dispatch: `src/Fable.Transforms/Replacements.Api.fs`

### Library provisioning
- Library selection/copy: `src/Fable.Compiler/ProjectCracker.fs`
- CLI assets: `src/Fable.Cli/Fable.Cli.fsproj`
- Library builders: `src/Fable.Build/FableLibrary/*.fs`

### Backend module layout (new)

```
src/Fable.Transforms/Java/
├── Java.fs          # Java AST definitions
├── Fable2Java.fs    # Fable AST → Java AST transform
├── JavaPrinter.fs   # Java AST → text output
└── Replacements.fs  # Java-specific runtime call replacements
```

This mirrors the Dart backend exactly.

---

## 4. Key Design Decisions

### 4.1 Closures and Lambdas

**Strategy: Custom `FuncN` functional interfaces in `fable-library-java`**

`java.util.function` only provides up to arity 2 (`Function<T,R>`, `BiFunction<T,U,R>`). F# regularly produces higher-arity lambdas and curried functions.

The runtime library provides:

```java
@FunctionalInterface public interface Func0<R> { R invoke(); }
@FunctionalInterface public interface Func1<T1, R> { R invoke(T1 arg1); }
@FunctionalInterface public interface Func2<T1, T2, R> { R invoke(T1 arg1, T2 arg2); }
// ... through Func8 or so (matching Scala's approach)

// Action equivalents for void-returning
@FunctionalInterface public interface Action0 { void invoke(); }
@FunctionalInterface public interface Action1<T1> { void invoke(T1 arg1); }
// etc.
```

Java 8 lambdas work with these:
```java
Func2<Integer, Integer, Integer> add = (a, b) -> a + b;
```

Curried F# functions (`'a -> 'b -> 'c`) compile to `Func1<A, Func1<B, C>>`.

Partial application compiles to explicit closure wrapping:
```java
// F#: let add3 = add 3
Func1<Integer, Integer> add3 = b -> add.invoke(3, b);
```

### 4.2 Discriminated Unions

**Strategy: Abstract class + subclass per case (Java 8 baseline)**

```java
// F#: type Shape = Circle of radius: float | Rectangle of w: float * h: float
public abstract class Shape {
    public abstract int get_Tag();

    // Case constructors
    public static Shape Circle(double radius) { return new Shape_Circle(radius); }
    public static Shape Rectangle(double w, double h) { return new Shape_Rectangle(w, h); }
}

public class Shape_Circle extends Shape {
    public final double radius;
    public Shape_Circle(double radius) { this.radius = radius; }
    public int get_Tag() { return 0; }

    @Override public boolean equals(Object o) { /* structural */ }
    @Override public int hashCode() { /* structural */ }
    @Override public String toString() { return "Circle(" + radius + ")"; }
}

public class Shape_Rectangle extends Shape {
    public final double w;
    public final double h;
    public Shape_Rectangle(double w, double h) { this.w = w; this.h = h; }
    public int get_Tag() { return 1; }

    @Override public boolean equals(Object o) { /* structural */ }
    @Override public int hashCode() { /* structural */ }
    @Override public String toString() { return "Rectangle(" + w + ", " + h + ")"; }
}
```

This is exactly the pattern used by the Dart and PHP backends, proven to work well.

**Java 17+ optional enhancement**: The printer could emit `sealed interface` + `record` instead:
```java
public sealed interface Shape permits Shape.Circle, Shape.Rectangle {
    int get_Tag();
    record Circle(double radius) implements Shape { public int get_Tag() { return 0; } }
    record Rectangle(double w, double h) implements Shape { public int get_Tag() { return 1; } }
}
```

This is a printer concern, not an architecture concern. Defer to post-MVP.

### 4.3 Records

**Strategy: Class with fields + constructor + equals/hashCode/toString (Java 8 baseline)**

```java
// F#: type Person = { Name: string; Age: int }
public class Person {
    public final String Name;
    public final int Age;

    public Person(String Name, int Age) {
        this.Name = Name;
        this.Age = Age;
    }

    // Copy method for `{ record with Field = value }` syntax
    public Person copy(String Name, int Age) {
        return new Person(Name, Age);
    }

    @Override public boolean equals(Object o) { /* structural */ }
    @Override public int hashCode() { /* structural */ }
    @Override public String toString() { return "Person(" + Name + ", " + Age + ")"; }
}
```

**Java 16+ optional enhancement**: Emit as `record Person(String Name, int Age) {}` and get equals/hashCode/toString for free. The `copy` method is still needed since Java records don't support `with`.

### 4.4 Option / Null Handling

**Strategy: `FSharpOption<T>` wrapper type**

Using `null` directly for `None` is tempting but breaks on `Some(null)` vs `None` — a distinction F# preserves. Using `java.util.Optional<T>` prohibits `null` values inside `Some`.

```java
public abstract class FSharpOption<T> {
    public abstract boolean isSome();
    public abstract T getValue();

    private static final FSharpOption<?> NONE = new None<>();

    @SuppressWarnings("unchecked")
    public static <T> FSharpOption<T> none() { return (FSharpOption<T>) NONE; }
    public static <T> FSharpOption<T> some(T value) { return new Some<>(value); }
}
```

For JVM interop, the library provides conversion methods:
```java
public static <T> Optional<T> toOptional(FSharpOption<T> opt) { /* ... */ }
public static <T> FSharpOption<T> fromOptional(Optional<T> opt) { /* ... */ }
public static <T> FSharpOption<T> fromNullable(T value) { /* ... */ }
```

### 4.5 Module → Class Mapping

Each F# **module** becomes a Java class. Each F# **file** may produce one or more classes.

- Top-level module → public class with static members
- Nested modules → nested static classes
- Module values/functions → `public static` methods/fields
- Module `let` bindings → initialized in `static { }` blocks (lazy if needed)

The Java class name matches the module name. The package is derived from the F# namespace (if present) or the relative file path, lowercased and sanitized.

### 4.6 Package Name Strategy

1. Use the F# namespace when available, lowercased: `MyApp.Domain.Models` → `package myapp.domain.models;`
2. Fall back to relative path under output root, lowercased and sanitized
3. Hyphens in directory names → underscores (illegal in Java packages)
4. Empty/default package avoided — always generate a package declaration
5. Optional `--java-package-prefix` CLI flag for users who want `com.company.app` prefixing (post-MVP)

### 4.7 Structural Equality and Comparison

The runtime library provides base helpers (mirroring Dart's `makeStructuralEquals` / `makeStructuralHashCode` / `makeStructuralCompareTo`):

```java
public class Util {
    public static boolean structuralEquals(Object a, Object b) { /* deep equality */ }
    public static int structuralHash(Object obj) { /* deep hash */ }
    public static int structuralCompareTo(Object a, Object b) { /* deep compare */ }
}
```

The printer generates `equals()`, `hashCode()`, `compareTo()` methods on records and DU cases that delegate to these helpers.

### 4.8 Generics and Type Erasure

Java's type erasure means generic type information is lost at runtime. Impact:

- **Reflection**: Severely limited. The plan explicitly defers full reflection parity. Basic `TypeInfo` works via class tokens where possible.
- **Generic array creation**: Uses `Object[]` internally, casts at access sites.
- **Collection creation**: No issue — `ArrayList<T>`, `HashMap<K,V>` etc. work fine with erasure.
- **`typeof<T>` in generics**: Cannot be supported in the general case. Document as limitation.

### 4.9 Exception Mapping

F# exceptions are DU cases under the hood. Java mapping:

- Custom F# exceptions → classes extending `RuntimeException`
- Base type: `FSharpException extends RuntimeException`
- Exception fields accessible as public final fields
- `try/with` patterns compile to try/catch chains with `instanceof` checks

### 4.10 String Interpolation

F# `$"Hello {name}"` compiles to string concatenation at the Fable AST level (already decomposed into `String.Concat` or similar calls). The backend emits `"Hello " + name` or `String.format(...)` in Java. No special handling needed.

### 4.11 Emit Strategy

Full emit support, matching the existing macro system from `BabelPrinter` / `DartPrinter` / `PythonPrinter`:

- `$0`, `$1`, ... — argument substitution
- `$0...` — spread argument
- `{{$0?then:else}}` — conditional emit

No restrictions. Users need escape hatches.

---

## 5. Phase-by-Phase Plan

### Phase 0: Foundation and Feature Flag

Goal: Create compile-time surface area without full codegen.

1. **Add `Java` case to the Language enum**
   - File: `src/Fable.AST/Plugins.fs`

2. **Add CLI language parsing**
   - Accept `java` (no `jvm` alias — the output is Java source, not bytecode)
   - File: `src/Fable.Cli/Entry.fs`

3. **Add default extension mapping**
   - `.java` in `defaultFileExt`
   - Files: `src/Fable.Compiler/Util.fs`, `src/Fable.Compiler/Util.fsi`

4. **Add compiler defines**
   - `FABLE_COMPILER_JAVA`
   - File: `src/Fable.Cli/Entry.fs`

5. **Add output/library path extensions for Java**
   - Library import path extension in `getLibPath`
   - Output import path rewrite support for `.java`
   - Files: `src/Fable.Transforms/Transforms.Util.fs`, `src/Fable.Compiler/Util.fs`

**Deliverable**: CLI accepts `--lang java`, pipeline compiles far enough to enter backend stub.

### Phase 1: Backend Skeleton + Closures + Async

Goal: Compile a minimal subset, emit valid Java, with closures and async working from day one.

This phase is larger than Codex's original Phase 1 because it front-loads closures and async — they're needed for any non-trivial F# program.

1. **Add Java backend files to project**
   - File: `src/Fable.Transforms/Fable.Transforms.fsproj`
   - Add: `Java/Java.fs`, `Java/Fable2Java.fs`, `Java/JavaPrinter.fs`, `Java/Replacements.fs`

2. **Implement Java AST**
   - Types: Package, Import, ClassDecl, MethodDecl, FieldDecl, Statement, Expression, Type nodes
   - Include: FuncN type references for closures, abstract class model for DUs
   - File: `src/Fable.Transforms/Java/Java.fs`

3. **Implement initial `Fable2Java` lowering**
   - Module-level functions and let bindings
   - Arithmetic, string ops, simple calls
   - Lambda → `FuncN` anonymous class / lambda expression
   - Delegate → `FuncN`
   - Partial application → closure wrapping
   - File: `src/Fable.Transforms/Java/Fable2Java.fs`

4. **Implement Java printer**
   - Package declarations, imports, class declarations, methods, statements, expressions
   - Emit macro support (`$0`, `$1`, spread, conditional)
   - File: `src/Fable.Transforms/Java/JavaPrinter.fs`

5. **Add Java replacement stubs**
   - Route async builder calls to `fable-library-java` AsyncBuilder/Async modules
   - Route basic type operations
   - File: `src/Fable.Transforms/Java/Replacements.fs`

6. **Extend shared replacement dispatch**
   - Add `| Java -> Java.Replacements.*` branches
   - File: `src/Fable.Transforms/Replacements.Api.fs`

7. **Wire backend into CLI pipeline**
   - Add `Java` compile module in `Pipeline.fs`
   - Add `match` case in `compileFile`
   - File: `src/Fable.Cli/Pipeline.fs`

8. **Bootstrap `fable-library-java` async core**
   - `Async.java`, `AsyncBuilder.java`, `CancellationToken.java`, `Trampoline.java`
   - `FuncN.java` (Func0 through Func8, Action0 through Action8)
   - `FSharpOption.java` skeleton
   - New folder: `src/fable-library-java/src/main/java/fable/library/`

**Deliverable**: Simple F# programs with functions, closures, and `async { }` blocks transpile to `.java` that compiles with `javac`.

### Phase 2: Naming, Packages, and Identifiers

Goal: Make generated Java identifiers and packages legal and deterministic.

1. **Add Java identifier sanitization**
   - Java keyword list (53 reserved words)
   - Identifier character rules: start with letter/$/_, continue with letter/digit/$/_
   - `sanitizeJavaIdent` parallel to existing Rust/Dart helpers
   - File: `src/Fable.Transforms/Global/Naming.fs`

2. **Integrate Java naming into symbol binding**
   - Entity declaration naming
   - Member declaration naming
   - Scoped identifier naming
   - File: `src/Fable.Transforms/FSharp2Fable.Util.fs`

3. **Implement package derivation**
   - F# namespace → Java package (lowercased, sanitized)
   - Path-based fallback
   - File: `src/Fable.Transforms/Java/Fable2Java.fs`

**Deliverable**: Multi-file projects compile without identifier collisions.

### Phase 3: Runtime Library and Replacements

Goal: Support core F# constructs via Java runtime helpers.

This phase can partially overlap with Phase 2 — basic runtime types don't need perfect naming to implement.

1. **Add library resolution logic**
   - `ProjectCracker.getFableLibraryPath` mapping for Java
   - Library dir name: `fable-library-java`
   - File: `src/Fable.Compiler/ProjectCracker.fs`

2. **Implement core runtime types in `fable-library-java`**
   MVP modules (in priority order):
   - `Util.java` (structural equality, hashing, comparison, common helpers)
   - `Types.java` (base types, FSharpException, Unit)
   - `FSharpOption.java` (Option with Some/None)
   - `FSharpList.java` (immutable linked list)
   - `Seq.java` (lazy sequence operations via Iterator/Iterable)
   - `Array.java` (array operations wrapper)
   - `Map.java` (immutable sorted map, wrapping TreeMap)
   - `Set.java` (immutable sorted set, wrapping TreeSet)
   - `FSharpResult.java` (Result<T,E>)
   - `String.java` (string helpers)
   - `Async.java` + `AsyncBuilder.java` (already scaffolded in Phase 1, expand here)
   - `Timer.java`, `TimeSpan.java` (basic time operations)
   - `Guid.java` (UUID wrapper)

3. **Implement Java-specific replacements**
   - Primitives and numeric conversions
   - String operations
   - Collection operations (List, Array, Seq, Map, Set)
   - Option/Result operations
   - Async operations (already routed in Phase 1, expand coverage here)
   - Ref cell / mutable model
   - File: `src/Fable.Transforms/Java/Replacements.fs`

4. **Extend shared replacement dispatch for all new operations**
   - File: `src/Fable.Transforms/Replacements.Api.fs`

**Deliverable**: Representative subset of existing tests run against generated Java + runtime library.

### Phase 4: Interop and Fable.Core Surface

Goal: Java-target interop comparable to other backends.

1. **Add `Fable.Core.Java.fs`**
   - Patterns analogous to `Fable.Core.Dart.fs`, `Fable.Core.Rust.fs`
   - Java-specific attributes for interop

2. **Add emit handling for Java**
   - Full emit macro support (already implemented in printer in Phase 1, wire attributes here)
   - File: `src/Fable.Transforms/Transforms.Util.fs`

3. **Include in `Fable.Core.fsproj`**

**Deliverable**: Usable interop API for calling Java/JDK APIs from F#.

### Phase 5: Build, Packaging, Distribution

Goal: Make the Java backend buildable and distributable.

1. **Build pipeline support**
   - Builder: `src/Fable.Build/FableLibrary/Java.fs`
   - Register in `Fable.Build.fsproj`, `Main.fs`, `Workspace.fs`

2. **CLI package content**
   - Include Java library assets in NuGet package
   - File: `src/Fable.Cli/Fable.Cli.fsproj`

3. **Optional standalone/compiler-js support**
   - `src/fable-standalone/src/Main.fs`: language routing
   - `src/fable-compiler-js/src/app.fs`: extension mapping

**Deliverable**: `./build.sh fable-library --java` works.

### Phase 6: Tests and CI

Goal: Make regressions visible, keep the backend shippable.

1. **Add backend test project**
   - `tests/Java/Fable.Tests.Java.fsproj`
   - Start with curated subset from existing test suites
   - Include async-specific tests from day one

2. **Add build/test runners**
   - `src/Fable.Build/Test/Java.fs`
   - `src/Fable.Build/Quicktest/Java.fs`
   - Register in `Main.fs`, `Fable.Build.fsproj`

3. **Add CI job**
   - `.github/workflows/build.yml` — new `build-java` job
   - Set up JDK 8 toolchain (baseline compatibility)
   - Run `./build.sh test java`

4. **Compiler smoke tests**
   - Integration tests for `--lang java`, `.java` extension, successful compile of fixtures

**Deliverable**: CI gate for Java backend correctness, including async tests.

---

## 6. MVP Feature Matrix

### Must have for first usable release

- Functions, let bindings, static module members
- Closures (lambdas, delegates, partial application) via FuncN
- Primitive types (int, float, bool, string, char)
- Tuples (as runtime helper type)
- Records (as classes with structural equality)
- Discriminated unions (abstract class + subclasses)
- `if/else`, `while`, `for` loops
- Pattern matching (compiled as if/else chains from decision trees)
- Arrays, lists, options, results — basic operations
- Method calls, object creation, static calls
- Exceptions (`try/catch/finally`)
- **Async** (`async { }`, `let!`, `do!`, `return`, `Async.Start`, `Async.StartAsTask` → `CompletableFuture`, `Async.Sleep`, `Async.Parallel`)
- String operations and interpolation
- Basic console I/O

### Explicitly deferred from MVP

- Full async/task model parity (edge cases, mailboxes — not the core async flow)
- Advanced reflection parity (type erasure limits this fundamentally)
- All .NET BCL edge cases
- Java 17+/21+ printer enhancements (sealed, records, virtual threads)
- `--java-target` flag for version-specific output
- `--java-package-prefix` configuration
- Full Event/Observable support

---

## 7. Risks and Mitigations

### 1. Semantic gap between F# and Java (closures, DUs, structural equality)
**Mitigation**: Custom runtime types (FuncN, FSharpOption, structural equality helpers). Route through runtime library — this is the proven pattern across all backends.

### 2. Name collisions and package resolution complexity
**Mitigation**: Implement Java-specific sanitizer early (Phase 2). Java has 53 reserved keywords; the sanitizer is straightforward.

### 3. Generic type erasure breaking reflection/TypeInfo
**Mitigation**: Document as known limitation. Defer advanced reflection. Basic use cases (collection operations, constructors) work fine with erasure.

### 4. Async correctness
**Mitigation**: The CPS model is proven across 4 backends (JS, Python, Rust, Dart). Port directly; don't reinvent. Test heavily from Phase 1.

### 5. Library scope explosion
**Mitigation**: Phase runtime modules by test-driven demand. Don't attempt complete BCL parity before MVP stabilizes.

### 6. CompletableFuture vs. async semantics mismatch
**Mitigation**: CompletableFuture is only the bridge point (`startAsCompletableFuture`). The internal async model uses CPS with explicit cancellation — it doesn't rely on CompletableFuture's limited cancellation semantics.

### 7. Java 8 verbosity making generated code hard to debug
**Mitigation**: Good `toString()` methods on all generated types. Source maps or comment annotations in generated code linking back to F# source lines. This is a post-MVP concern.

---

## 8. Definition of Done (Initial Java Backend)

- `fable --lang java` compiles multi-file F# projects to `.java`
- Generated code compiles with `javac` on JDK 8+
- Runtime library is resolved automatically through `fable_modules`
- **Async works**: `async { }` blocks compile and execute correctly, bridging to `CompletableFuture`
- Closures and partial application work via FuncN interfaces
- DUs and records have correct structural equality
- Java backend is covered by dedicated tests and CI
- Documentation exists for backend limitations and supported constructs

---

## 9. PR Sequence

### PR1: Foundation
- Language enum + CLI parse + extension + pipeline skeleton + empty Java backend files
- Tests: CLI accepts `--lang java`, produces correct file extension

### PR2: AST + Printer + Minimal Lowering
- Java AST definitions, printer, minimal Fable2Java for hello-world
- FuncN interfaces in fable-library-java
- Tests: simple F# file → compilable .java output

### PR3: Closures + Async
- Lambda/delegate → FuncN codegen
- Partial application codegen
- AsyncBuilder + Async + CancellationToken + Trampoline in fable-library-java
- Async replacements wiring
- Tests: async { } blocks compile and execute, closures work

### PR4: Naming + Packages
- Java identifier sanitization
- Package derivation from namespaces/paths
- Import generation
- Tests: multi-file projects compile without collisions

### PR5: Runtime Library Core
- FSharpOption, FSharpList, Seq, Array, Map, Set, Result, String, Util, Types
- Replacements for primitives and collections
- Tests: representative subset of existing test suite passes

### PR6: DUs + Records + Structural Equality
- Union codegen (abstract class + subclasses)
- Record codegen (class with fields + structural equality)
- Equality/comparison helpers in runtime library
- Tests: pattern matching, record updates, union construction/testing

### PR7: Interop + Build + CI
- Fable.Core.Java interop surface
- Build pipeline integration
- CI job with JDK 8
- Tests: integration tests, full test suite gating

### PR8+: Parity Expansion
- Expand collection operations
- Exception mapping
- Reflection subset
- Performance tuning
- Optional: Java 17+/21+ printer enhancements
