module Fable.Tests.Java.JavaFoundation

open Fable
open Fable.Compiler.Util
open Fable.Cli.Entry
open Util.Testing

let tests =
    testList "Java Foundation" [
        testCase "argLanguage parses java" <| fun _ ->
            let args = CliArgs([ "--lang"; "java" ])
            let actual = argLanguage args
            actual |> equal (Ok Language.Java)

        testCase "knownCliArgs includes java" <| fun _ ->
            let descriptions =
                knownCliArgs ()
                |> List.collect snd
                |> String.concat "\n"

            descriptions.Contains("java") |> equal true

        testCase "defaultFileExt for Java uses .java" <| fun _ ->
            File.defaultFileExt true Language.Java |> equal ".java"
            File.defaultFileExt false Language.Java |> equal ".fs.java"
    ]
