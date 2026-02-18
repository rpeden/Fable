module Fable.Tests.Java.Main

open Expecto

let allTests =
    [
        JavaFoundation.tests
        JavaPrinter.tests
        JavaReplacements.tests
        JavaRuntimeSurface.tests
        JavaBuildWiring.tests
        JavaCompilerSmoke.tests
        JavaNaming.tests
        JavaRuntimeExecution.tests
    ]

[<EntryPoint>]
let main args =
    let config = [ Sequenced ]

    allTests
    |> testList "All"
    |> runTestsWithCLIArgs config args
