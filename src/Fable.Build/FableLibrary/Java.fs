namespace Build.FableLibrary

open System.IO
open Fake.IO
open Build.Utils

type BuildFableLibraryJava() =
    member _.Run(?skipIfExist: bool) =
        let skipIfExist = defaultArg skipIfExist false

        let sourceDir = Path.Combine("src", "fable-library-java")
        let buildDir = Path.Combine("temp", "fable-library-java")

        if skipIfExist && Directory.Exists(buildDir) then
            printfn "Skipping Java library build stage"
        else
            Directory.clean buildDir
            Shell.copyDir buildDir sourceDir FileFilter.allFiles

    interface IFableLibraryBuilder with
        member this.Run(?skipIfExist: bool) = this.Run(?skipIfExist = skipIfExist)
