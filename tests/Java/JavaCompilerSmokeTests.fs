module Fable.Tests.Java.JavaCompilerSmoke

open System
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

let private compileJava (source: string) =
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
    ]
