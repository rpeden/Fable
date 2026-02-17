namespace Fable.Transforms.Java

module Replacements =

    open Fable
    open Fable.AST
    open Fable.AST.Fable
    open Fable.Transforms
    open Replacements.Util

    type Context = FSharp2Fable.Context
    type ICompiler = FSharp2Fable.IFableCompiler
    type CallInfo = ReplaceCallInfo

    let tryGetAsyncReplacement (declaringEntityFullName: string) (compiledName: string) : (string * string) option =
        match declaringEntityFullName, compiledName with
        | "Microsoft.FSharp.Control.FSharpAsyncBuilder", "Singleton"
        | "Microsoft.FSharp.Control.AsyncActivation`1", "Singleton" -> Some("AsyncBuilder", "singleton")

        | ("Microsoft.FSharp.Control.FSharpAsyncBuilder" | "Microsoft.FSharp.Control.AsyncActivation`1"), meth ->
            Some("AsyncBuilder", meth)

        | ("Microsoft.FSharp.Control.FSharpAsync" | "Microsoft.FSharp.Control.AsyncPrimitives"), "Catch" ->
            Some("Async", "catchAsync")

        | ("Microsoft.FSharp.Control.FSharpAsync" | "Microsoft.FSharp.Control.AsyncPrimitives"), "get_CancellationToken" ->
            Some("Async", "cancellationToken")

        | ("Microsoft.FSharp.Control.FSharpAsync" | "Microsoft.FSharp.Control.AsyncPrimitives"), "Start" ->
            Some("Async", "start")

        | ("Microsoft.FSharp.Control.FSharpAsync" | "Microsoft.FSharp.Control.AsyncPrimitives"), meth ->
            Some("Async", Naming.lowerFirst meth)

        | _ -> None

    let error (_com: FSharp2Fable.IFableCompiler) msg =
        FableError $"Java backend: {msg}" |> raise

    let tryCall (com: ICompiler) (_ctx: Context) r t (info: CallInfo) (thisArg: Expr option) (args: Expr list) =
        match tryGetAsyncReplacement info.DeclaringEntityFullName info.CompiledName with
        | Some(moduleName, memberName) ->
            Helper.LibCall(
                com,
                moduleName,
                memberName,
                t,
                args,
                info.SignatureArgTypes,
                info.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | None -> None
