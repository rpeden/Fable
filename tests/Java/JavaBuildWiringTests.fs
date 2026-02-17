module Fable.Tests.Java.JavaBuildWiring

open System.IO
open Util.Testing

let private repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private readFile relativePath =
    let fullPath = Path.Combine(repoRoot, relativePath)
    File.ReadAllText(fullPath)

let tests =
    testList "Java Build Wiring" [
        testCase "Fable.Build project includes Java quicktest module" <| fun _ ->
            let content = readFile "src/Fable.Build/Fable.Build.fsproj"
            content.Contains("Quicktest/Java.fs") |> equal true

        testCase "Build main exposes Java quicktest command" <| fun _ ->
            let content = readFile "src/Fable.Build/Main.fs"
            content.Contains("java                    Run for Java") |> equal true
            content.Contains("| \"java\" :: _ -> Quicktest.Java.handle args") |> equal true

        testCase "CI workflow has build-java job with JDK 8 and Java tests" <| fun _ ->
            let content = readFile ".github/workflows/build.yml"
            content.Contains("build-java:") |> equal true
            content.Contains("uses: actions/setup-java") |> equal true
            content.Contains("java-version: '8'") |> equal true
            content.Contains("run: ./build.sh test java") |> equal true

        testCase "Fable.Cli packages Java runtime library assets" <| fun _ ->
            let content = readFile "src/Fable.Cli/Fable.Cli.fsproj"
            content.Contains("temp\\fable-library-java\\**\\*.*") |> equal true
            content.Contains("PackagePath=\"fable-library-java\\\"") |> equal true
    ]
