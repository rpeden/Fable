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

        testCase "Fable.Core project includes Java interop surface" <| fun _ ->
            let content = readFile "src/Fable.Core/Fable.Core.fsproj"
            content.Contains("Fable.Core.Java.fs") |> equal true

        testCase "Fable.Core.Java exposes emit/import helpers" <| fun _ ->
            let content = readFile "src/Fable.Core/Fable.Core.Java.fs"
            content.Contains("module Fable.Core.Java") |> equal true
            content.Contains("let emitExpr<'T>") |> equal true
            content.Contains("let emitStatement<'T>") |> equal true
            content.Contains("let import<'T>") |> equal true
            content.Contains("let importMember<'T>") |> equal true
            content.Contains("let importAll<'T>") |> equal true

        testCase "fable-standalone routes Java language through parser and printers" <| fun _ ->
            let content = readFile "src/fable-standalone/src/Main.fs"
            content.Contains("type JavaResult") |> equal true
            content.Contains("| Java ->") |> equal true
            content.Contains("let ast = Fable2Java.Compiler.transformFile com fableAst") |> equal true
            content.Contains("| :? JavaResult as java -> JavaPrinter.run writer java.Ast") |> equal true
            content.Contains("| \"java\" -> Java") |> equal true

        testCase "fable-compiler-js maps Java language to .java extension" <| fun _ ->
            let content = readFile "src/fable-compiler-js/src/app.fs"
            content.Contains("| \"java\" -> \".java\"") |> equal true
    ]
