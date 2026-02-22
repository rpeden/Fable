module Fable.Tests.Java.JavaIntegration

open System.IO
open Fable.Tests.Java.JavaTestHelpers
open Util.Testing

let tests =
    testList "Java Integration" [
        // -----------------------------------------------------------------------
        // Basic javac compilation
        // -----------------------------------------------------------------------

        testCase "generated Java output compiles with javac" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet value = 42" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = Path.Combine(outDir, "javac-out")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for generated Java:\n%s" javacErr

        testCase "generated Java and runtime execute together under Java 8" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet value = 42" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "Runner.java")

            File.WriteAllText(
                runnerJava,
                """package testproject;

import fable.library.FSharpOption;

public class Runner {
    public static void main(String[] args) {
        Program p = new Program();
        if (p == null) {
            throw new RuntimeException("Program instance creation failed");
        }

        FSharpOption<Integer> value = FSharpOption.some(42);
        if (!value.isSome() || value.getValue() != 42) {
            throw new RuntimeException("Runtime wiring failed");
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for runtime+generated execution smoke:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.Runner" ] repoRoot

            if javaCode <> 0 then
                failwithf "java execution failed for runtime+generated smoke:\n%s" javaErr

        testCase "transform output compiles with javac after AST is walked" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet value = 42\nlet double x = x + x" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = Path.Combine(outDir, "javac-transform-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected generated Java after transformer walk:\n%s" javacErr

        // -----------------------------------------------------------------------
        // Generics
        // -----------------------------------------------------------------------

        testCase "generic function compiles with javac" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet identity x = x" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-generics-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected generic Java output:\n%s" javacErr

        testCase "multi-argument generic function compiles with javac" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet first a b = a" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-generic-multi-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected multi-arg generic Java output:\n%s" javacErr

        // -----------------------------------------------------------------------
        // Records and DUs
        // -----------------------------------------------------------------------

        testCase "F# record type compiles with javac" <| fun _ ->
            let errors, _ = compileAndRead "module Program\ntype Point = { X: int; Y: int }" None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-record-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected record Java output:\n%s" javacErr

        testCase "F# DU type compiles with javac" <| fun _ ->
            let src = "module Program\ntype Shape = | Circle of Radius: double | Rectangle of Width: double * Height: double"
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-du-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected DU Java output:\n%s" javacErr

        // -----------------------------------------------------------------------
        // Classes with instance methods
        // -----------------------------------------------------------------------

        testCase "F# class with instance state compiles with javac" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-class-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected F# class Java output:\n%s" javacErr

        testCase "F# class can be instantiated and methods called at runtime" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "ClassRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class ClassRunner {
    public static void main(String[] args) {
        Program.Counter c = new Program.Counter(5);
        c.increment();
        if (c.count != 6) {
            throw new RuntimeException("Expected count=6 after increment, got " + c.count);
        }
        c.add(4);
        if (c.count != 10) {
            throw new RuntimeException("Expected count=10 after add(4), got " + c.count);
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-class-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for Counter class runtime test:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.ClassRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "Counter class runtime execution failed:\n%s" javaErr

        // -----------------------------------------------------------------------
        // Pattern matching — javac compilation
        // -----------------------------------------------------------------------

        testCase "simple DU match compiles with javac" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Square of float

let describe (s: Shape) =
    match s with
    | Circle r -> r
    | Square s -> s
"""
            let errors, _ = compileAndRead src (Some "javac-du-match")
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-match-du")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected DU match output:\n%s" javacErr

        testCase "DU match executes correctly at runtime" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Square of float

let area (s: Shape) =
    match s with
    | Circle r -> 3.14 * r * r
    | Square s -> s * s
"""
            let errors, _ = compileAndRead src (Some "javac-du-match-runtime")
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "MatchRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class MatchRunner {
    public static void main(String[] args) {
        double circleArea = Program.area(new Program.Shape.Circle(10.0));
        if (Math.abs(circleArea - 314.0) > 0.001) {
            throw new RuntimeException("Circle area: expected 314.0, got " + circleArea);
        }
        double squareArea = Program.area(new Program.Shape.Square(5.0));
        if (Math.abs(squareArea - 25.0) > 0.001) {
            throw new RuntimeException("Square area: expected 25.0, got " + squareArea);
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-match-du-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for DU match runtime test:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.MatchRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "DU match runtime execution failed:\n%s" javaErr

        testCase "integer literal match compiles with javac" <| fun _ ->
            let src = """module Program
let describe (n: int) =
    match n with
    | 1 -> "one"
    | 2 -> "two"
    | _ -> "other"
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-match-int")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected integer match output:\n%s" javacErr

        testCase "integer literal match executes correctly at runtime" <| fun _ ->
            let src = """module Program
let describe (n: int) =
    match n with
    | 1 -> "one"
    | 2 -> "two"
    | _ -> "other"
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "IntMatchRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class IntMatchRunner {
    public static void main(String[] args) {
        if (!Program.describe(1).equals("one")) {
            throw new RuntimeException("Expected 'one', got " + Program.describe(1));
        }
        if (!Program.describe(2).equals("two")) {
            throw new RuntimeException("Expected 'two', got " + Program.describe(2));
        }
        if (!Program.describe(99).equals("other")) {
            throw new RuntimeException("Expected 'other', got " + Program.describe(99));
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-match-int-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for int match runtime test:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.IntMatchRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "int match runtime execution failed:\n%s" javaErr

        testCase "match with multiple bound values compiles with javac" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Rectangle of float * float

let perimeter (s: Shape) =
    match s with
    | Circle r -> 2.0 * 3.14 * r
    | Rectangle(w, h) -> 2.0 * (w + h)
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-match-multi-bind")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected multi-bound match output:\n%s" javacErr

        testCase "match with multiple bound values executes correctly" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Rectangle of float * float

let perimeter (s: Shape) =
    match s with
    | Circle r -> 2.0 * 3.14 * r
    | Rectangle(w, h) -> 2.0 * (w + h)
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "MultiBoundRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class MultiBoundRunner {
    public static void main(String[] args) {
        double cp = Program.perimeter(new Program.Shape.Circle(10.0));
        if (Math.abs(cp - 62.8) > 0.001) {
            throw new RuntimeException("Circle perimeter: expected 62.8, got " + cp);
        }
        double rp = Program.perimeter(new Program.Shape.Rectangle(3.0, 4.0));
        if (Math.abs(rp - 14.0) > 0.001) {
            throw new RuntimeException("Rectangle perimeter: expected 14.0, got " + rp);
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-match-multi-bind-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for multi-bound match runtime test:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.MultiBoundRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "multi-bound match runtime execution failed:\n%s" javaErr

        testCase "wildcard match with no bindings compiles with javac" <| fun _ ->
            let src = """module Program
let classify (n: int) =
    match n with
    | 0 -> "zero"
    | _ -> "nonzero"
"""
            let errors, _ = compileAndRead src None
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let compileOutput = Path.Combine(outDir, "javac-match-wildcard")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected wildcard match output:\n%s" javacErr

        testCase "three-plus case DU match executes correctly" <| fun _ ->
            let src = """module Program
type Color = Red | Green | Blue

let name (c: Color) =
    match c with
    | Red -> "red"
    | Green -> "green"
    | Blue -> "blue"
"""
            let errors, _ = compileAndRead src (Some "javac-3case-match")
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "ColorRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class ColorRunner {
    public static void main(String[] args) {
        if (!Program.name(new Program.Color.Red()).equals("red")) {
            throw new RuntimeException("Expected 'red', got " + Program.name(new Program.Color.Red()));
        }
        if (!Program.name(new Program.Color.Green()).equals("green")) {
            throw new RuntimeException("Expected 'green', got " + Program.name(new Program.Color.Green()));
        }
        if (!Program.name(new Program.Color.Blue()).equals("blue")) {
            throw new RuntimeException("Expected 'blue', got " + Program.name(new Program.Color.Blue()));
        }
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-match-3case-run")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for 3-case DU match test:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.ColorRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "3-case DU match runtime execution failed:\n%s" javaErr

        // -----------------------------------------------------------------------
        // printf / printfn
        // -----------------------------------------------------------------------

        testCase "printfn simple string compiles with javac" <| fun _ ->
            let src = """module Program
let run () = printfn "Hello World"
"""
            let errors, _ = compileAndRead src (Some "javac-printfn-simple")
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = Path.Combine(outDir, "javac-printfn-simple-out")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected printfn output:\n%s" javacErr

        testCase "printfn simple string executes correctly" <| fun _ ->
            let src = """module Program
let run () = printfn "Hello World"
"""
            let errors, _ = compileAndRead src (Some "javac-printfn-exec")
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = Path.Combine(outDir, "PrintfnRunner.java")
            File.WriteAllText(
                runnerJava,
                """package testproject;

public class PrintfnRunner {
    public static void main(String[] args) {
        Program p = new Program();
        p.run();
    }
}
"""
            )

            let compileOutput = Path.Combine(outDir, "javac-printfn-exec-out")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for printfn execution test:\n%s" javacErr

            let javaCode, javaOut, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.PrintfnRunner" ] repoRoot

            if javaCode <> 0 then
                failwithf "printfn runtime execution failed:\n%s" javaErr

            javaOut.Trim() |> equal "Hello World"
    ]
