module Fable.Tests.Java.JavaPrinter

open System.Text
open Fable.AST
open Fable.Transforms
open Fable.Transforms.Printer
open Util.Testing

type private TestWriter() =
    let builder = StringBuilder()

    member _.Output = builder.ToString()

    interface Writer with
        member _.AddSourceMapping(_, _, _, _, _, _) = ()
        member _.MakeImportPath(path) = path
        member _.AddLog(_, _, ?range) = ()
        member _.Write(str) =
            async {
                builder.Append(str) |> ignore
            }

        member _.Dispose() = ()

let tests =
    testList "Java Printer" [
        testCase "emit macro replaces positional args" <| fun _ ->
            JavaPrinter.applyEmitMacro "$0 + $1" [ "left"; "right" ]
            |> equal "left + right"

        testCase "emit macro expands spread" <| fun _ ->
            JavaPrinter.applyEmitMacro "call($1...)" [ "ignored"; "a"; "b"; "c" ]
            |> equal "call(a, b, c)"

        testCase "emit macro evaluates conditional" <| fun _ ->
            JavaPrinter.applyEmitMacro "{{$0?YES:NO}}" [ "true" ]
            |> equal "YES"

            JavaPrinter.applyEmitMacro "{{$0?YES:NO}}" [ "false" ]
            |> equal "NO"

        testCase "prints package imports and declarations" <| fun _ ->
            let file: Java.File =
                {
                    Package = Some "my.app"
                    Imports = [ "java.util.Objects" ]
                    Declarations = [ "public final class Program {}" ]
                }

            let writer = new TestWriter()

            JavaPrinter.run (writer :> Writer) file
            |> Async.RunSynchronously

            let output = writer.Output
            output.Contains("package my.app;") |> equal true
            output.Contains("import java.util.Objects;") |> equal true
            output.Contains("public final class Program {}") |> equal true

        testCase "skips package and imports when absent" <| fun _ ->
            let file: Java.File =
                {
                    Package = None
                    Imports = []
                    Declarations = [ "public final class Main {}" ]
                }

            let writer = new TestWriter()

            JavaPrinter.run (writer :> Writer) file
            |> Async.RunSynchronously

            let output = writer.Output
            output.Contains("package ") |> equal false
            output.Contains("import ") |> equal false
            output.Contains("public final class Main {}") |> equal true
    ]
