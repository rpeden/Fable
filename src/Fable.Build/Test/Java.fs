module Build.Test.Java

open Build.Utils
open SimpleExec

let private projectDir = Path.Resolve("tests", "Java")

let handle (_args: string list) =
    Command.Run("dotnet", "test -c Release", workingDirectory = projectDir)
