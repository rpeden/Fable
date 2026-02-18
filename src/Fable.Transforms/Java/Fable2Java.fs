module Fable.Transforms.Fable2Java

open System
open System.IO
open Fable
open Fable.AST
open Fable.Transforms.Java

module Compiler =
    let private sanitizePackageSegment (segment: string) =
        segment.ToLowerInvariant()
        |> Naming.sanitizeJavaIdentForbiddenChars
        |> Naming.checkJavaKeywords

    let private getPackageName (com: Compiler) =
        let projectDir = System.IO.Path.GetDirectoryName(com.ProjectFile)
        let currentDir = System.IO.Path.GetDirectoryName(com.CurrentFile)
        let projectName = System.IO.Path.GetFileName(projectDir)
        let relDir = Path.getRelativeFileOrDirPath true projectDir true currentDir

        let segments =
            if String.IsNullOrWhiteSpace(relDir) || relDir = "." || relDir = "./" then
                [ projectName ]
            else
                let relDir =
                    if relDir.StartsWith("./", StringComparison.Ordinal) then
                        relDir[2..]
                    else
                        relDir

                let relSegments =
                    relDir.Split(
                        [|
                            System.IO.Path.DirectorySeparatorChar
                            System.IO.Path.AltDirectorySeparatorChar
                        |],
                        StringSplitOptions.RemoveEmptyEntries
                    )

                projectName :: Array.toList relSegments

        segments
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> List.map sanitizePackageSegment
        |> String.concat "."

    let transformFile (com: Compiler) (_file: Fable.AST.Fable.File) : File =
        {
            Package = Some(getPackageName com)
            Imports = []
            Declarations = [ "public final class Program {}" ]
        }
