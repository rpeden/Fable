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

        testCase "Runtime includes Util helpers for structural semantics" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Util.java"

            content.Contains("class Util") |> equal true
            content.Contains("structuralEquals") |> equal true
            content.Contains("structuralHash") |> equal true

        testCase "Runtime includes Types module with Unit value" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Types.java"

            content.Contains("class Types") |> equal true
            content.Contains("class Unit") |> equal true

        testCase "Runtime includes FSharpOption some/none representation" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/FSharpOption.java"

            content.Contains("class FSharpOption") |> equal true
            content.Contains("some(") |> equal true
            content.Contains("none(") |> equal true
            content.Contains("isNone(") |> equal true

        testCase "Runtime includes FSharpList immutable list shape" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/FSharpList.java"

            content.Contains("class FSharpList") |> equal true
            content.Contains("empty()") |> equal true
            content.Contains("cons(") |> equal true

        testCase "Runtime includes Seq helpers" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Seq.java"

            content.Contains("class Seq") |> equal true
            content.Contains("map(") |> equal true
            content.Contains("toList(") |> equal true

        testCase "Runtime includes Array helpers" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Array.java"

            content.Contains("class Array") |> equal true
            content.Contains("map(") |> equal true
            content.Contains("append(") |> equal true

        testCase "Runtime includes FSharpResult ok/error representation" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/FSharpResult.java"

            content.Contains("class FSharpResult") |> equal true
            content.Contains("ok(") |> equal true
            content.Contains("error(") |> equal true
            content.Contains("map(") |> equal true
            content.Contains("mapError(") |> equal true

        testCase "Runtime includes String helper operations" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/String.java"

            content.Contains("class String") |> equal true
            content.Contains("concat(") |> equal true
            content.Contains("join(") |> equal true

        testCase "Runtime includes Map helper operations" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Map.java"

            content.Contains("class Map") |> equal true
            content.Contains("empty(") |> equal true
            content.Contains("add(") |> equal true

        testCase "Runtime includes Set helper operations" <| fun _ ->
            let content = readRuntimeFile "src/fable-library-java/src/main/java/fable/library/Set.java"

            content.Contains("class Set") |> equal true
            content.Contains("empty(") |> equal true
            content.Contains("add(") |> equal true
    ]
