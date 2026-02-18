module Fable.Tests.Java.JavaCompilerSmoke

open System
open System.Diagnostics
open System.IO
open Fable
open Fable.Cli.Main
open Fable.Transforms.State
open Fable.Compiler.Util
open Util.Testing

let private repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let private projectDir = System.IO.Path.Combine(repoRoot, "tests", "Integration", "Compiler", "TestProject")
let private projectFile = System.IO.Path.Combine(projectDir, "TestProject.fsproj")
let private sourceFile = System.IO.Path.Combine(projectDir, "Program.fs")
let private outDir = System.IO.Path.Combine(repoRoot, "temp", "java-compiler-smoke")
let private runtimeDir = System.IO.Path.Combine(repoRoot, "src", "fable-library-java", "src", "main", "java", "fable", "library")

let private runProcess (exe: string) (args: string list) (workingDir: string) =
    let psi = ProcessStartInfo()
    psi.FileName <- exe
    psi.WorkingDirectory <- workingDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true

    for arg in args do
        psi.ArgumentList.Add(arg)

    use proc = new Process()
    proc.StartInfo <- psi
    proc.Start() |> ignore
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    proc.ExitCode, stdout, stderr

let private compileJava (source: string) =
    if Directory.Exists(outDir) then
        Directory.Delete(outDir, true)

    Directory.CreateDirectory(outDir) |> ignore

    let compilerOptions = CompilerOptionsHelper.Make(language = Language.Java, fileExtension = ".java")

    let cliArgs =
        { CliArgs.ProjectFile = projectFile
          FableLibraryPath = None
          RootDir = projectDir
          Configuration = "Debug"
          OutDir = Some outDir
          IsWatch = false
          Precompile = false
          PrecompiledLib = None
          PrintAst = false
          SourceMaps = false
          SourceMapsRoot = None
          NoRestore = false
          NoCache = false
          NoParallelTypeCheck = false
          Exclude = [ "Fable.Core" ]
          Replace = Map.empty
          RunProcess = None
          CompilerOptions = compilerOptions
          Verbosity = Verbosity.Normal }

    File.WriteAllText(sourceFile, source)

    let state = State.Create(cliArgs, recompileAllFiles = true)

    let logs =
        match state |> startCompilationAsync |> Async.RunSynchronously with
        | Error(_, logs) -> Array.toList logs
        | Ok(_, logs) -> Array.toList logs

    let errors = logs |> List.filter (fun m -> m.Severity = Severity.Error)
    logs, errors

let private getGeneratedJavaFiles () =
    Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
    |> Array.filter (fun file -> not (file.Contains("fable_modules")))
    |> Array.toList

let tests =
    testList "Java Compiler Smoke" [
        testCase "compiles with --lang java without compiler errors" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

        testCase "writes .java output files for java compilation" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

            let javaFiles = Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
            javaFiles.Length > 0 |> equal true

        testCase "writes package declaration derived from project path" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

            let javaFile = Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories) |> Array.head
            let content = File.ReadAllText(javaFile)
            content.Contains("package testproject;") |> equal true

        testCase "generated Java output compiles with javac" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-out")

            if Directory.Exists(compileOutput) then
                Directory.Delete(compileOutput, true)

            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for generated Java:\n%s" javacErr

            true |> equal true

        testCase "generated Java and runtime execute together under Java 8" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = System.IO.Path.Combine(outDir, "Runner.java")

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

            let compileOutput = System.IO.Path.Combine(outDir, "javac-run")

            if Directory.Exists(compileOutput) then
                Directory.Delete(compileOutput, true)

            Directory.CreateDirectory(compileOutput) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ runtimeSources @ generatedJava @ [ runnerJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed for runtime+generated execution smoke:\n%s" javacErr

            let javaCode, _, javaErr = runProcess "java" [ "-cp"; compileOutput; "testproject.Runner" ] repoRoot

            if javaCode <> 0 then
                failwithf "java execution failed for runtime+generated smoke:\n%s" javaErr

            true |> equal true

        testCase "emits field for module-level let value binding" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.head |> File.ReadAllText
            // The stub just emits an empty class - this verifies the transformer actually walks the AST
            (content.Contains("value") && content.Contains("42")) |> equal true

        testCase "emits static method for module-level function" <| fun _ ->
            let _, errors = compileJava "module Program\nlet add a b = a + b"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.head |> File.ReadAllText
            content.Contains("add") |> equal true

        testCase "emits boolean constant correctly" <| fun _ ->
            let _, errors = compileJava "module Program\nlet flag = true"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let content = generatedJava |> List.head |> File.ReadAllText
            content.Contains("true") |> equal true

        testCase "emits string constant correctly" <| fun _ ->
            let _, errors = compileJava "module Program\nlet greeting = \"hello\""
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let content = generatedJava |> List.head |> File.ReadAllText
            content.Contains("hello") |> equal true

        testCase "transform output compiles with javac after AST is walked" <| fun _ ->
            let _, errors = compileJava "module Program\nlet value = 42\nlet double x = x + x"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-transform-check")

            if Directory.Exists(compileOutput) then
                Directory.Delete(compileOutput, true)

            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac rejected generated Java after transformer walk:\n%s" javacErr

            true |> equal true

        testCase "generic function emits type parameter declaration" <| fun _ ->
            let _, errors = compileJava "module Program\nlet identity x = x"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.head |> File.ReadAllText
            // Should emit something like `public static <A> A identity(A x)` — the `<` is the key indicator
            content.Contains("<") |> equal true

        testCase "generic function compiles with javac" <| fun _ ->
            let _, errors = compileJava "module Program\nlet identity x = x"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-generics-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot
            if javacCode <> 0 then
                failwithf "javac rejected generic Java output:\n%s" javacErr
            true |> equal true

        testCase "multi-argument generic function compiles with javac" <| fun _ ->
            let _, errors = compileJava "module Program\nlet first a b = a"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-generic-multi-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot
            if javacCode <> 0 then
                failwithf "javac rejected multi-arg generic Java output:\n%s" javacErr
            true |> equal true

        testCase "F# record emits Java class with fields" <| fun _ ->
            let _, errors = compileJava "module Program\ntype Point = { X: int; Y: int }"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.map File.ReadAllText |> String.concat "\n"
            // The record class should contain the field names
            (content.Contains("Point") && content.Contains("X") && content.Contains("Y")) |> equal true

        testCase "F# record type compiles with javac" <| fun _ ->
            let _, errors = compileJava "module Program\ntype Point = { X: int; Y: int }"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-record-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot
            if javacCode <> 0 then
                failwithf "javac rejected record Java output:\n%s" javacErr
            true |> equal true

        testCase "F# DU emits abstract base class with inner subclasses" <| fun _ ->
            let _, errors = compileJava "module Program\ntype Shape = | Circle of Radius: double | Rectangle of Width: double * Height: double"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.map File.ReadAllText |> String.concat "\n"
            // Should contain the DU abstract base and the case names as inner classes
            (content.Contains("Shape") && content.Contains("Circle") && content.Contains("Rectangle")) |> equal true

        testCase "F# DU type compiles with javac" <| fun _ ->
            let _, errors = compileJava "module Program\ntype Shape = | Circle of Radius: double | Rectangle of Width: double * Height: double"
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-du-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot
            if javacCode <> 0 then
                failwithf "javac rejected DU Java output:\n%s" javacErr
            true |> equal true

        testCase "F# class constructor initializes instance fields" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let _, errors = compileJava src
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.map File.ReadAllText |> String.concat "\n"
            // Class should exist with the field and constructor
            (content.Contains("Counter") && content.Contains("count") && content.Contains("initial")) |> equal true

        testCase "F# class instance methods are inside the class body" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let _, errors = compileJava src
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let content = generatedJava |> List.map File.ReadAllText |> String.concat "\n"
            // Instance methods must NOT be static module-level functions with mangled names
            content.Contains("Counter__Increment") |> equal false
            content.Contains("Counter__get_Value") |> equal false
            // And should have the properly named methods present
            content.Contains("increment") |> equal true
            content.Contains("getValue") |> equal true

        testCase "F# class instance methods have no unit parameter" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
"""
            let _, errors = compileJava src
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            let content = generatedJava |> List.map File.ReadAllText |> String.concat "\n"
            // F# unit argument must not appear as a Java parameter
            content.Contains("unitVar") |> equal false
            content.Contains("void unitVar") |> equal false

        testCase "F# class with instance state compiles with javac" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let _, errors = compileJava src
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let compileOutput = System.IO.Path.Combine(outDir, "javac-class-check")
            if Directory.Exists(compileOutput) then Directory.Delete(compileOutput, true)
            Directory.CreateDirectory(compileOutput) |> ignore

            let javacArgs = [ "--release"; "8"; "-d"; compileOutput ] @ generatedJava
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot
            if javacCode <> 0 then
                failwithf "javac rejected F# class Java output:\n%s" javacErr
            true |> equal true

        testCase "F# class can be instantiated and methods called at runtime" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let _, errors = compileJava src
            errors.Length |> equal 0

            let generatedJava = getGeneratedJavaFiles ()
            generatedJava.IsEmpty |> equal false

            let runnerJava = System.IO.Path.Combine(outDir, "ClassRunner.java")
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

            let compileOutput = System.IO.Path.Combine(outDir, "javac-class-run")
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
            true |> equal true

        testCase "multi-file compilation uses distinct package paths" <| fun _ ->
            let originalProject = File.ReadAllText(projectFile)
            let originalProgram = File.ReadAllText(sourceFile)
            let subDir = System.IO.Path.Combine(projectDir, "Sub")
            let subFile = System.IO.Path.Combine(subDir, "Program.fs")

            try
                Directory.CreateDirectory(subDir) |> ignore
                File.WriteAllText(sourceFile, "module RootProgram\nlet value = 1")
                File.WriteAllText(subFile, "module SubProgram\nlet value = 2")

                File.WriteAllText(
                    projectFile,
                                        """<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RollForward>Major</RollForward>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="Program.fs" />
        <Compile Include="Sub/Program.fs" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../../../src/Fable.Core/Fable.Core.fsproj" />
    </ItemGroup>

</Project>"""
                )

                let _, errors = compileJava "module RootProgram\nlet value = 1"
                errors.Length |> equal 0

                let javaFiles = Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
                javaFiles.Length >= 2 |> equal true

                let generated = javaFiles |> Array.map File.ReadAllText |> String.concat "\n\n"
                generated.Contains("package testproject;") |> equal true
                generated.Contains("package testproject.sub;") |> equal true
            finally
                File.WriteAllText(projectFile, originalProject)
                File.WriteAllText(sourceFile, originalProgram)

                if File.Exists(subFile) then
                    File.Delete(subFile)

                if Directory.Exists(subDir) && Directory.GetFileSystemEntries(subDir).Length = 0 then
                    Directory.Delete(subDir)
    ]
