module Fable.Tests.Java.JavaRuntimeExecution

open System
open System.Diagnostics
open System.IO
open Util.Testing

let private repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let private runtimeDir = Path.Combine(repoRoot, "src", "fable-library-java", "src", "main", "java", "fable", "library")
let private smokeJava = Path.Combine(repoRoot, "tests", "Java", "runtime", "RuntimeSmoke.java")
let private outputDir = Path.Combine(repoRoot, "temp", "java-runtime-exec")

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

let tests =
    testList "Java Runtime Execution" [
        testCase "Java runtime compiles and Java smoke test executes" <| fun _ ->
            if Directory.Exists(outputDir) then
                Directory.Delete(outputDir, true)

            Directory.CreateDirectory(outputDir) |> ignore

            let runtimeSources = Directory.GetFiles(runtimeDir, "*.java") |> Array.toList
            let javacArgs = [ "--release"; "8"; "-d"; outputDir ] @ runtimeSources @ [ smokeJava ]
            let javacCode, _, javacErr = runProcess "javac" javacArgs repoRoot

            if javacCode <> 0 then
                failwithf "javac failed:\n%s" javacErr

            let javaArgs = [ "-cp"; outputDir; "RuntimeSmoke" ]
            let javaCode, _, javaErr = runProcess "java" javaArgs repoRoot

            if javaCode <> 0 then
                failwithf "java RuntimeSmoke failed:\n%s" javaErr

            true |> equal true
    ]
