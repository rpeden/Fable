module Fable.Tests.Java.JavaReplacements

open Fable.Transforms.Java
open Util.Testing

let tests =
    testList "Java Replacements" [
        testCase "maps async builder singleton" <| fun _ ->
            Replacements.tryGetAsyncReplacement "Microsoft.FSharp.Control.FSharpAsyncBuilder" "Singleton"
            |> equal (Some("AsyncBuilder", "singleton"))

        testCase "maps async catch and cancellation token names" <| fun _ ->
            Replacements.tryGetAsyncReplacement "Microsoft.FSharp.Control.FSharpAsync" "Catch"
            |> equal (Some("Async", "catchAsync"))

            Replacements.tryGetAsyncReplacement "Microsoft.FSharp.Control.FSharpAsync" "get_CancellationToken"
            |> equal (Some("Async", "cancellationToken"))

        testCase "returns none for unrelated replacement" <| fun _ ->
            Replacements.tryGetAsyncReplacement "System.String" "Concat"
            |> equal None

        testCase "maps Option and Result constructors to Java runtime" <| fun _ ->
            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.FSharpOption`1" "Some"
            |> equal (Some("FSharpOption", "some"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.FSharpOption`1" "get_Value"
            |> equal (Some("FSharpOption", "getValue"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.FSharpOption`1" "get_IsSome"
            |> equal (Some("FSharpOption", "isSome"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.FSharpOption`1" "get_IsNone"
            |> equal (Some("FSharpOption", "isNone"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.FSharpResult`2" "NewOk"
            |> equal (Some("FSharpResult", "ok"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.ResultModule" "Map"
            |> equal (Some("FSharpResult", "map"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Core.ResultModule" "MapError"
            |> equal (Some("FSharpResult", "mapError"))

        testCase "maps collection modules to Java runtime modules" <| fun _ ->
            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Collections.SeqModule" "Map"
            |> equal (Some("Seq", "map"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Collections.ListModule" "Map"
            |> equal (Some("FSharpList", "map"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Collections.ArrayModule" "Map"
            |> equal (Some("Array", "map"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Collections.MapModule" "Add"
            |> equal (Some("Map", "add"))

            Replacements.tryGetCoreReplacement "Microsoft.FSharp.Collections.SetModule" "Add"
            |> equal (Some("Set", "add"))

        testCase "maps System.String calls to Java String runtime module" <| fun _ ->
            Replacements.tryGetCoreReplacement "System.String" "Concat"
            |> equal (Some("String", "concat"))

        testCase "classifies Fable.Core.Java emit helpers" <| fun _ ->
            Replacements.tryGetJavaInteropCall "emitExpr"
            |> equal Replacements.JavaInteropCall.EmitExpr

            Replacements.tryGetJavaInteropCall "emitStatement"
            |> equal Replacements.JavaInteropCall.EmitStatement

        testCase "classifies Fable.Core.Java import helpers" <| fun _ ->
            Replacements.tryGetJavaInteropCall "import"
            |> equal Replacements.JavaInteropCall.Import

            Replacements.tryGetJavaInteropCall "importMember"
            |> equal Replacements.JavaInteropCall.ImportMember

            Replacements.tryGetJavaInteropCall "importAll"
            |> equal Replacements.JavaInteropCall.ImportAll

        testCase "returns unsupported for unknown Java interop helpers" <| fun _ ->
            Replacements.tryGetJavaInteropCall "nope"
            |> equal Replacements.JavaInteropCall.Unsupported
    ]
