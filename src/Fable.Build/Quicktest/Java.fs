module Build.Quicktest.Java

open Build.FableLibrary
open Build.Quicktest.Core

let handle (args: string list) =
    genericQuicktest
        {
            Language = "java"
            FableLibBuilder = BuildFableLibraryJava()
            ProjectDir = "src/quicktest"
            Extension = ".java"
            RunMode = RunScript
        }
        args
