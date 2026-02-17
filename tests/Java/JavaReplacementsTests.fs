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
    ]
