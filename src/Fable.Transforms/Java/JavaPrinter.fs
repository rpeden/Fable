module Fable.Transforms.JavaPrinter

open System.Text.RegularExpressions
open Fable.Transforms.Java
open Fable.Transforms.Printer

let isEmpty (file: File) : bool = List.isEmpty file.Declarations

let applyEmitMacro (value: string) (args: string list) =
    let inline replace pattern (f: Match -> string) input = Regex.Replace(input, pattern, f)

    value
    |> replace
        @"\$(\d+)\.\.\."
        (fun m ->
            let i = int m.Groups[1].Value

            args |> List.skip i |> String.concat ", "
        )
    |> replace
        @"\{\{\s*\$(\d+)\s*\?(.*?):(.*?)\}\}"
        (fun m ->
            let i = int m.Groups[1].Value

            match args |> List.tryItem i with
            | Some value when value = "true" -> m.Groups[2].Value
            | _ -> m.Groups[3].Value
        )
    |> replace
        @"\$(\d+)"
        (fun m ->
            let i = int m.Groups[1].Value
            args |> List.tryItem i |> Option.defaultValue ""
        )

let run (writer: Writer) (file: File) : Async<unit> =
    async {
        match file.Package with
        | Some packageName -> do! writer.Write($"package {packageName};\n\n")
        | None -> ()

        for importPath in file.Imports do
            do! writer.Write($"import {importPath};\n")

        if not (List.isEmpty file.Imports) then
            do! writer.Write("\n")

        for declaration in file.Declarations do
            do! writer.Write(declaration)
            do! writer.Write("\n")
    }
