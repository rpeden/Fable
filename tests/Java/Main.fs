module Fable.Tests.Java.Main

open System
open Expecto

let private tryGetEnv name =
    let value = Environment.GetEnvironmentVariable(name)

    if String.IsNullOrWhiteSpace(value) then
        None
    else
        Some value

let private includeIntegrationTests =
    match tryGetEnv "FABLE_JAVA_SKIP_INTEGRATION" with
    | Some "1"
    | Some "true"
    | Some "TRUE" -> false
    | _ -> true

let private includeSlowSuites =
    match tryGetEnv "FABLE_JAVA_FAST" with
    | Some "1"
    | Some "true"
    | Some "TRUE" -> false
    | _ -> true

let allTests =
    [
        JavaFoundation.tests
        JavaPrinter.tests
        JavaReplacements.tests
        JavaRuntimeSurface.tests
        JavaBuildWiring.tests
        if includeSlowSuites then
            JavaCompilerSmoke.tests
        if includeIntegrationTests then
            JavaIntegration.tests
        JavaNaming.tests
        if includeSlowSuites then
            JavaRuntimeExecution.tests
    ]

let private withEnvCliArgs (args: string array) =
    if args.Length > 0 then
        args
    else
        match tryGetEnv "FABLE_JAVA_TEST_CASE", tryGetEnv "FABLE_JAVA_TEST_LIST", tryGetEnv "FABLE_JAVA_TEST_PATH" with
        | Some testCase, _, _ -> [| "--filter-test-case"; testCase |]
        | None, Some testList, _ -> [| "--filter-test-list"; testList |]
        | None, None, Some testPath -> [| "--filter"; testPath |]
        | None, None, None ->
            match tryGetEnv "FABLE_JAVA_TEST_FILTER" with
            | Some filter -> [| "--filter-test-case"; filter |]
            | None -> args

[<EntryPoint>]
let main args =
    let config = [ Sequenced ]
    let effectiveArgs = withEnvCliArgs args

    allTests
    |> testList "All"
    |> runTestsWithCLIArgs config effectiveArgs
