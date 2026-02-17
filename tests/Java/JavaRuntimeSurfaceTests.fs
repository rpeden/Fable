module Fable.Tests.Java.JavaRuntimeSurface

open System.IO
open Util.Testing

let private repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private readRuntimeFile relativePath =
    let fullPath = Path.Combine(repoRoot, relativePath)
    File.ReadAllText(fullPath)

let tests =
    testList "Java Runtime Surface" [
        testCase "Functional contains FuncN and ActionN interfaces" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Functional.java"

            content.Contains("interface Func8") |> equal true
            content.Contains("interface Action8") |> equal true

        testCase "Async module contains core cps operations" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/AsyncModule.java"

            content.Contains("protectedCont") |> equal true
            content.Contains("protectedBind") |> equal true
            content.Contains("startAsCompletableFuture") |> equal true

        testCase "Async builder exposes Bind Return and Delay" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/AsyncBuilder.java"

            content.Contains(" Bind(") |> equal true
            content.Contains(" Return(") |> equal true
            content.Contains(" Delay(") |> equal true
    ]
