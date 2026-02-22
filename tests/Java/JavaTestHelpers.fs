module Fable.Tests.Java.JavaTestHelpers

open System
open System.Diagnostics
open System.IO
open Fable
open Fable.Cli.Main
open Fable.Transforms.State
open Fable.Compiler.Util

let repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let projectDir = System.IO.Path.Combine(repoRoot, "tests", "Integration", "Compiler", "TestProject")
let projectFile = System.IO.Path.Combine(projectDir, "TestProject.fsproj")
let sourceFile = System.IO.Path.Combine(projectDir, "Program.fs")
let outDir = System.IO.Path.Combine(repoRoot, "temp", "java-compiler-smoke")
let runtimeDir = System.IO.Path.Combine(repoRoot, "src", "fable-library-java", "src", "main", "java", "fable", "library")

/// Set FABLE_JAVA_SNAPSHOTS=1 to save generated Java output to temp/java-snapshots/{name}/
let shouldSaveSnapshots =
    not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FABLE_JAVA_SNAPSHOTS")))

let runProcess (exe: string) (args: string list) (workingDir: string) =
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

let compileJava (source: string) =
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

let getGeneratedJavaFiles () =
    Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
    |> Array.filter (fun file -> not (file.Contains("fable_modules")))
    |> Array.toList

let getGeneratedJavaContent () =
    getGeneratedJavaFiles () |> List.map File.ReadAllText |> String.concat "\n"

let saveSnapshot (name: string) =
    let generatedFiles = getGeneratedJavaFiles ()

    if not generatedFiles.IsEmpty then
        let dest = System.IO.Path.Combine(repoRoot, "temp", "java-snapshots", name)

        if Directory.Exists(dest) then
            Directory.Delete(dest, true)

        Directory.CreateDirectory(dest) |> ignore

        for file in generatedFiles do
            let relPath = Path.GetRelativePath(outDir, file)
            let destFile = Path.Combine(dest, relPath)
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)) |> ignore
            File.Copy(file, destFile)

/// Compile F# source, return (errors, generated Java content string).
/// When snapshotName is Some and FABLE_JAVA_SNAPSHOTS env var is set,
/// saves the generated files to temp/java-snapshots/{name}/.
let compileAndRead (source: string) (snapshotName: string option) =
    let _, errors = compileJava source
    let content = getGeneratedJavaContent ()

    match snapshotName with
    | Some name when shouldSaveSnapshots -> saveSnapshot name
    | _ -> ()

    errors, content
