module Fable.Tests.Java.JavaNaming

open Fable
open Util.Testing

let tests =
    testList "Java Naming" [
        testCase "sanitizeJavaIdent escapes Java keywords" <| fun _ ->
            Naming.sanitizeJavaIdent (fun _ -> false) "class" Naming.NoMemberPart
            |> equal "class_"

        testCase "sanitizeJavaIdent replaces forbidden characters" <| fun _ ->
            Naming.sanitizeJavaIdent (fun _ -> false) "hello-world" Naming.NoMemberPart
            |> equal "hello$002Dworld"

        testCase "sanitizeJavaIdent avoids first-character digit" <| fun _ ->
            Naming.sanitizeJavaIdent (fun _ -> false) "1value" Naming.NoMemberPart
            |> equal "$0031value"

        testCase "sanitizeJavaIdent avoids conflicts" <| fun _ ->
            Naming.sanitizeJavaIdent (fun n -> n = "value") "value" Naming.NoMemberPart
            |> equal "value_1"
    ]
