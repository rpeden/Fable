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
