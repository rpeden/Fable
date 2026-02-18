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

    type JavaInteropCall =
        | EmitExpr
        | EmitStatement
        | Import
        | ImportMember
        | ImportAll
        | Unsupported

    let tryGetJavaInteropCall (compiledName: string) : JavaInteropCall =
        match compiledName with
        | "emitExpr" -> EmitExpr
        | "emitStatement" -> EmitStatement
        | "import" -> Import
        | "importMember" -> ImportMember
        | "importAll" -> ImportAll
        | _ -> Unsupported

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

    let tryGetCoreReplacement (declaringEntityFullName: string) (compiledName: string) : (string * string) option =
        match declaringEntityFullName, compiledName with
        | "Microsoft.FSharp.Core.FSharpOption`1", "Some" -> Some("FSharpOption", "some")
        | "Microsoft.FSharp.Core.FSharpOption`1", "None" -> Some("FSharpOption", "none")
        | "Microsoft.FSharp.Core.FSharpOption`1", "get_Value" -> Some("FSharpOption", "getValue")
        | "Microsoft.FSharp.Core.FSharpOption`1", "get_IsSome" -> Some("FSharpOption", "isSome")
        | "Microsoft.FSharp.Core.FSharpOption`1", "get_IsNone" -> Some("FSharpOption", "isNone")
        | "Microsoft.FSharp.Core.OptionModule", meth -> Some("FSharpOption", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Core.FSharpResult`2", "NewOk" -> Some("FSharpResult", "ok")
        | "Microsoft.FSharp.Core.FSharpResult`2", "NewError" -> Some("FSharpResult", "error")
        | "Microsoft.FSharp.Core.ResultModule", meth -> Some("FSharpResult", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Collections.SeqModule", meth -> Some("Seq", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Collections.ListModule", meth -> Some("FSharpList", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Collections.ArrayModule", meth -> Some("Array", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Collections.MapModule", meth -> Some("Map", Naming.lowerFirst meth)
        | "Microsoft.FSharp.Collections.SetModule", meth -> Some("Set", Naming.lowerFirst meth)
        | "System.String", meth -> Some("String", Naming.lowerFirst meth)
        | _ -> None

    let error (_com: FSharp2Fable.IFableCompiler) msg =
        FableError $"Java backend: {msg}" |> raise

    let tryCall (com: ICompiler) (_ctx: Context) r t (info: CallInfo) (thisArg: Expr option) (args: Expr list) =
        match info.DeclaringEntityFullName with
        | Naming.StartsWith "Fable.Core.Java" _ ->
            match tryGetJavaInteropCall info.CompiledName, args with
            | EmitExpr, [ tupledArgs; macro ]
            | EmitStatement, [ tupledArgs; macro ] ->
                match macro with
                | RequireStringConstOrTemplate com _ctx r template ->
                    let args = destructureTupleArgs [ tupledArgs ]
                    let isStatement = tryGetJavaInteropCall info.CompiledName = EmitStatement
                    emitTemplate r t args isStatement template |> Some
            | ImportMember, [ RequireStringConst com _ctx r path ] ->
                makeImportUserGenerated r t Naming.placeholder path |> Some
            | ImportAll, [ RequireStringConst com _ctx r path ] -> makeImportUserGenerated r t "*" path |> Some
            | Import, [ RequireStringConst com _ctx r selector; RequireStringConst com _ctx r path ] ->
                makeImportUserGenerated r t selector path |> Some
            | _ -> None
        | _ ->
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
            | None ->
                match tryGetCoreReplacement info.DeclaringEntityFullName info.CompiledName with
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
