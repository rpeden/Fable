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

    /// Sanitize a simple identifier with no conflict checking.
    let private javaIdent (name: string) : string =
        Naming.sanitizeJavaIdent (fun _ -> false) name Naming.NoMemberPart

    let private getClassName (com: Compiler) =
        let raw = IO.Path.GetFileNameWithoutExtension(com.CurrentFile)
        let sanitized: string = javaIdent raw

        if sanitized.Length = 0 then
            "Module"
        elif Char.IsLower(sanitized.[0]) then
            string (Char.ToUpper(sanitized.[0])) + sanitized.[1..]
        else
            sanitized

    // ---------------------------------------------------------------------------
    // Emit macro helper (inlined from JavaPrinter to avoid forward dependency)
    // ---------------------------------------------------------------------------

    let private applyEmitMacro (value: string) (args: string list) =
        let inline replace pattern (f: System.Text.RegularExpressions.Match -> string) input =
            System.Text.RegularExpressions.Regex.Replace(input, pattern, f)

        value
        |> replace
            @"\$(\d+)\.\.\."
            (fun m ->
                let i = int m.Groups.[1].Value
                args |> List.skip i |> String.concat ", "
            )
        |> replace
            @"\{\{\s*\$(\d+)\s*\?(.*?):(.*?)\}\}"
            (fun m ->
                let i = int m.Groups.[1].Value

                match args |> List.tryItem i with
                | Some value when value = "true" -> m.Groups.[2].Value
                | _ -> m.Groups.[3].Value
            )
        |> replace
            @"\$(\d+)"
            (fun m ->
                let i = int m.Groups.[1].Value
                args |> List.tryItem i |> Option.defaultValue ""
            )

    // ---------------------------------------------------------------------------
    // Generic type parameter helpers
    // ---------------------------------------------------------------------------

    /// Strip leading `$` and uppercase the first letter so `$a` -> `A`, `$T` -> `T`.
    let private sanitizeGenericParamName (name: string) : string =
        let stripped =
            if name.StartsWith("$", StringComparison.Ordinal) then
                name.[1..]
            else
                name

        let clean =
            if stripped.Length = 0 then
                "T"
            else
                stripped

        let safe = Naming.checkJavaKeywords clean

        string (Char.ToUpper(safe.[0]))
        + (if safe.Length > 1 then
               safe.[1..]
           else
               "")

    /// Collect distinct sanitized GenericParam names appearing in a type.
    let rec private collectGenericParams (typ: Fable.Type) : string list =
        match typ with
        | Fable.Type.GenericParam(name, _, _) -> [ sanitizeGenericParamName name ]
        | Fable.Type.Array(inner, _) -> collectGenericParams inner
        | Fable.Type.Option(inner, _) -> collectGenericParams inner
        | Fable.Type.LambdaType(arg, ret) -> collectGenericParams arg @ collectGenericParams ret
        | Fable.Type.DeclaredType(_, genArgs) -> genArgs |> List.collect collectGenericParams
        | Fable.Type.Tuple(types, _) -> types |> List.collect collectGenericParams
        | _ -> []

    // ---------------------------------------------------------------------------
    // Type mapping
    // ---------------------------------------------------------------------------

    let rec private transformType (typ: Fable.Type) : string =
        match typ with
        | Fable.Type.Unit -> "void"
        | Fable.Type.Boolean -> "boolean"
        | Fable.Type.Char -> "char"
        | Fable.Type.String -> "String"
        | Fable.Type.Number(Int32, _) -> "int"
        | Fable.Type.Number(Int64, _) -> "long"
        | Fable.Type.Number(UInt64, _) -> "long"
        | Fable.Type.Number(Float64, _) -> "double"
        | Fable.Type.Number(Float32, _) -> "float"
        | Fable.Type.Number(UInt8, _) -> "byte"
        | Fable.Type.Number(Int16, _) -> "short"
        | Fable.Type.Number(_, _) -> "Object"
        | Fable.Type.Option(inner, _) -> transformType inner
        | Fable.Type.Array(inner, _) -> transformType inner + "[]"
        | Fable.Type.Any -> "Object"
        | Fable.Type.DeclaredType(ref, _) -> ref.DisplayName
        | Fable.Type.GenericParam(name, _, _) -> sanitizeGenericParamName name
        | Fable.Type.LambdaType _ -> "Object" // functional interface - erased at this stage
        | Fable.Type.Tuple _ -> "Object" // tuple helper - deferred
        | _ -> "Object"

    // ---------------------------------------------------------------------------
    // Expression emitter
    // ---------------------------------------------------------------------------

    let private escapeJavaString (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")

    let rec private transformExpr (com: Compiler) (expr: Fable.Expr) : string =
        match expr with
        | Fable.Value(kind, _) ->
            match kind with
            | Fable.ValueKind.UnitConstant -> "null"
            | Fable.ValueKind.BoolConstant b ->
                if b then
                    "true"
                else
                    "false"
            | Fable.ValueKind.CharConstant c -> $"'{c}'"
            | Fable.ValueKind.StringConstant s -> $"\"{escapeJavaString s}\""
            | Fable.ValueKind.Null _ -> "null"
            | Fable.ValueKind.ThisValue _ -> "this"
            | Fable.ValueKind.BaseValue _ -> "super"
            | Fable.ValueKind.NumberConstant(v, _) ->
                match v with
                | Fable.NumberValue.Int8 n -> string (int n)
                | Fable.NumberValue.UInt8 n -> string (int n)
                | Fable.NumberValue.Int16 n -> string (int n)
                | Fable.NumberValue.UInt16 n -> string (int n)
                | Fable.NumberValue.Int32 n -> string n
                | Fable.NumberValue.UInt32 n -> string n
                | Fable.NumberValue.Int64 n -> $"{n}L"
                | Fable.NumberValue.UInt64 n -> $"{n}L"
                | Fable.NumberValue.Float32 n ->
                    if Single.IsNaN(n) then
                        "Float.NaN"
                    elif Single.IsPositiveInfinity(n) then
                        "Float.POSITIVE_INFINITY"
                    elif Single.IsNegativeInfinity(n) then
                        "Float.NEGATIVE_INFINITY"
                    else
                        $"{n}f"
                | Fable.NumberValue.Float64 n ->
                    if Double.IsNaN(n) then
                        "Double.NaN"
                    elif Double.IsPositiveInfinity(n) then
                        "Double.POSITIVE_INFINITY"
                    elif Double.IsNegativeInfinity(n) then
                        "Double.NEGATIVE_INFINITY"
                    else
                        string n
                | Fable.NumberValue.Decimal n -> $"new java.math.BigDecimal(\"{n}\")"
                | _ -> "/* unsupported number */"

            | Fable.ValueKind.StringTemplate(None, parts, values) ->
                let chunks =
                    parts
                    |> List.mapi (fun i part ->
                        let p = $"\"{escapeJavaString part}\""

                        if i < List.length values then
                            $"{p} + {transformExpr com values.[i]}"
                        else
                            p
                    )

                "(" + String.concat " + " chunks + ")"

            | Fable.ValueKind.NewOption(None, _, _) -> "null"
            | Fable.ValueKind.NewOption(Some v, _, _) -> transformExpr com v
            | Fable.ValueKind.NewList(None, _) -> "null"
            | Fable.ValueKind.NewList(Some(head, tail), _) ->
                $"/* cons({transformExpr com head}, {transformExpr com tail}) */"

            | Fable.ValueKind.NewRecord(values, entityRef, _) ->
                let className = javaIdent entityRef.DisplayName
                let argsStr = values |> List.map (transformExpr com) |> String.concat ", "
                $"new {className}({argsStr})"

            | Fable.ValueKind.NewUnion(values, tag, entityRef, _) ->
                let entity = com.GetEntity(entityRef)
                let cases = entity.UnionCases

                if tag < List.length cases then
                    let caseName = javaIdent cases.[tag].Name
                    let parentName = javaIdent entityRef.DisplayName
                    let argsStr = values |> List.map (transformExpr com) |> String.concat ", "
                    $"new {parentName}.{caseName}({argsStr})"
                else
                    $"/* NewUnion tag {tag} out of range */"

            | Fable.ValueKind.NewArray(Fable.ArrayValues values, typ, _) ->
                let jType = transformType typ
                let elems = values |> List.map (transformExpr com) |> String.concat ", "
                $"new {jType}[]{{{elems}}}"

            | Fable.ValueKind.NewArray(Fable.ArrayAlloc size, typ, _) ->
                let jType = transformType typ
                $"new {jType}[{transformExpr com size}]"

            | _ -> "/* TODO:value */"

        | Fable.IdentExpr id ->
            if id.IsThisArgument then
                "this"
            else
                javaIdent id.Name

        | Fable.Operation(kind, _, _, _) ->
            match kind with
            | Fable.OperationKind.Binary(op, left, right) ->
                let jOp =
                    match op with
                    | BinaryPlus -> "+"
                    | BinaryMinus -> "-"
                    | BinaryMultiply -> "*"
                    | BinaryDivide -> "/"
                    | BinaryModulus -> "%"
                    | BinaryExponent -> "/* ** */"
                    | BinaryEqual -> "=="
                    | BinaryUnequal -> "!="
                    | BinaryLess -> "<"
                    | BinaryLessOrEqual -> "<="
                    | BinaryGreater -> ">"
                    | BinaryGreaterOrEqual -> ">="
                    | BinaryAndBitwise -> "&"
                    | BinaryOrBitwise -> "|"
                    | BinaryXorBitwise -> "^"
                    | BinaryShiftLeft -> "<<"
                    | BinaryShiftRightSignPropagating -> ">>"
                    | BinaryShiftRightZeroFill -> ">>>"

                $"({transformExpr com left} {jOp} {transformExpr com right})"

            | Fable.OperationKind.Unary(op, operand) ->
                let jOp =
                    match op with
                    | UnaryMinus -> "-"
                    | UnaryPlus -> "+"
                    | UnaryNot -> "!"
                    | UnaryNotBitwise -> "~"
                    | UnaryAddressOf -> ""

                $"({jOp}{transformExpr com operand})"

            | Fable.OperationKind.Logical(op, left, right) ->
                let jOp =
                    match op with
                    | LogicalAnd -> "&&"
                    | LogicalOr -> "||"

                $"({transformExpr com left} {jOp} {transformExpr com right})"

        | Fable.IfThenElse(guard, thenExpr, elseExpr, _) ->
            $"({transformExpr com guard} ? {transformExpr com thenExpr} : {transformExpr com elseExpr})"

        | Fable.Get(expr, Fable.GetKind.FieldGet field, _, _) -> $"{transformExpr com expr}.{field.Name}"

        | Fable.Get(expr, Fable.GetKind.TupleIndex i, _, _) -> $"{transformExpr com expr}.item{i + 1}"

        | Fable.Get(expr, Fable.GetKind.ListHead, _, _) -> $"{transformExpr com expr}.head()"

        | Fable.Get(expr, Fable.GetKind.ListTail, _, _) -> $"{transformExpr com expr}.tail()"

        | Fable.Get(expr, Fable.GetKind.OptionValue, _, _) -> $"{transformExpr com expr}.getValue()"

        | Fable.Get(expr, Fable.GetKind.ExprGet idx, _, _) -> $"{transformExpr com expr}[{transformExpr com idx}]"

        | Fable.Get(expr, Fable.GetKind.UnionTag, _, _) -> $"{transformExpr com expr}.tag()"

        | Fable.Get(expr, Fable.GetKind.UnionField info, _, _) -> $"{transformExpr com expr}.field{info.FieldIndex}"

        | Fable.TypeCast(expr, _) -> transformExpr com expr

        | Fable.Sequential exprs ->
            match List.rev exprs with
            | [] -> "null"
            | last :: _ -> transformExpr com last

        | Fable.Let(_, _, body) -> transformExpr com body

        | Fable.Call(callee, info, _, _) ->
            let calleeStr =
                match callee with
                | Fable.IdentExpr id -> javaIdent id.Name
                | Fable.Get(obj, Fable.GetKind.FieldGet field, _, _) -> $"{transformExpr com obj}.{field.Name}"
                | other -> transformExpr com other

            let argsStr = info.Args |> List.map (transformExpr com) |> String.concat ", "
            $"{calleeStr}({argsStr})"

        | Fable.CurriedApply(applied, args, _, _) ->
            let appStr = transformExpr com applied
            let argsStr = args |> List.map (transformExpr com) |> String.concat ", "
            $"{appStr}({argsStr})"

        | Fable.Lambda(arg, body, _) ->
            let argStr = javaIdent arg.Name
            $"({argStr}) -> {transformExpr com body}"

        | Fable.Delegate(args, body, _, _) ->
            let argsStr = args |> List.map (fun a -> javaIdent a.Name) |> String.concat ", "

            $"({argsStr}) -> {transformExpr com body}"

        | Fable.Import(info, _, _) -> info.Selector

        | Fable.Emit(info, _, _) -> applyEmitMacro info.Macro (info.CallInfo.Args |> List.map (transformExpr com))

        | Fable.Test(expr, Fable.TestKind.OptionTest isSome, _) ->
            let call =
                if isSome then
                    "isSome"
                else
                    "isNone"

            $"{transformExpr com expr}.{call}()"

        | Fable.Test(expr, Fable.TestKind.ListTest isCons, _) ->
            if isCons then
                $"({transformExpr com expr} != null)"
            else
                $"({transformExpr com expr} == null)"

        | Fable.Test(expr, Fable.TestKind.UnionCaseTest tag, _) -> $"({transformExpr com expr}.tag() == {tag})"

        | Fable.Test(expr, Fable.TestKind.TypeTest typ, _) ->
            $"({transformExpr com expr} instanceof {transformType typ})"

        | _ -> "/* TODO:expr */"

    // ---------------------------------------------------------------------------
    // Statement emitter
    // Returns (statement lines, captured expression value)
    // ---------------------------------------------------------------------------

    let rec private transformStmts (com: Compiler) (expr: Fable.Expr) (indent: string) : string list * string option =
        match expr with
        | Fable.Let(ident, value, body) ->
            let jType = transformType ident.Type

            let modifier =
                if ident.IsMutable then
                    ""
                else
                    "final "

            let idName = javaIdent ident.Name
            let decl = $"{indent}{modifier}{jType} {idName} = {transformExpr com value};"
            let bodyStmts, bodyVal = transformStmts com body indent
            decl :: bodyStmts, bodyVal

        | Fable.LetRec(bindings, body) ->
            let declStmts =
                bindings
                |> List.map (fun (ident, value) ->
                    let jType = transformType ident.Type

                    let modifier =
                        if ident.IsMutable then
                            ""
                        else
                            "final "

                    $"{indent}{modifier}{jType} {javaIdent ident.Name} = {transformExpr com value};"
                )

            let bodyStmts, bodyVal = transformStmts com body indent
            declStmts @ bodyStmts, bodyVal

        | Fable.Sequential exprs ->
            let rec go =
                function
                | [] -> [], None
                | [ last ] -> transformStmts com last indent
                | x :: rest ->
                    let xStmts, xVal = transformStmts com x indent
                    let discard = xVal |> Option.map (fun v -> $"{indent}{v};") |> Option.toList
                    let restStmts, restVal = go rest
                    xStmts @ discard @ restStmts, restVal

            go exprs

        | Fable.IfThenElse(guard, thenExpr, elseExpr, _) ->
            let guardStr = transformExpr com guard
            let inner = indent + "    "
            let thenStmts, thenVal = transformStmts com thenExpr inner
            let elseStmts, elseVal = transformStmts com elseExpr inner

            match thenStmts, elseStmts, thenVal, elseVal with
            | [], [], Some tv, Some ev -> [], Some $"({guardStr} ? {tv} : {ev})"
            | _ ->
                let thenReturn =
                    thenVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

                let elseReturn =
                    elseVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

                let block =
                    [ $"{indent}if ({guardStr}) {{" ]
                    @ thenStmts
                    @ thenReturn
                    @ [ $"{indent}}} else {{" ]
                    @ elseStmts
                    @ elseReturn
                    @ [ $"{indent}}}" ]

                block, None

        | Fable.WhileLoop(guard, body, _) ->
            let bodyStmts, _ = transformStmts com body (indent + "    ")

            [ $"{indent}while ({transformExpr com guard}) {{" ]
            @ bodyStmts
            @ [ $"{indent}}}" ],
            None

        | Fable.ForLoop(ident, start, limit, body, isUp, _) ->
            let id = javaIdent ident.Name

            let cmp =
                if isUp then
                    "<="
                else
                    ">="

            let op =
                if isUp then
                    "++"
                else
                    "--"

            let bodyStmts, _ = transformStmts com body (indent + "    ")

            [
                $"{indent}for (int {id} = {transformExpr com start}; {id} {cmp} {transformExpr com limit}; {id}{op}) {{"
            ]
            @ bodyStmts
            @ [ $"{indent}}}" ],
            None

        | Fable.TryCatch(body, catch, finalizer, _) ->
            let inner = indent + "    "
            let bodyStmts, bodyVal = transformStmts com body inner

            let bodyReturn =
                bodyVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

            let tryBlock = [ $"{indent}try {{" ] @ bodyStmts @ bodyReturn @ [ $"{indent}}}" ]

            let catchBlock =
                match catch with
                | None -> []
                | Some(ident, handler) ->
                    let id = javaIdent ident.Name
                    let hStmts, hVal = transformStmts com handler inner
                    let hReturn = hVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

                    [ $"{indent}catch (Exception {id}) {{" ] @ hStmts @ hReturn @ [ $"{indent}}}" ]

            let finallyBlock =
                match finalizer with
                | None -> []
                | Some fin ->
                    let fStmts, _ = transformStmts com fin inner
                    [ $"{indent}finally {{" ] @ fStmts @ [ $"{indent}}}" ]

            tryBlock @ catchBlock @ finallyBlock, None

        | Fable.Extended(Fable.ExtendedSet.Throw(exprOpt, _), _) ->
            let inner =
                match exprOpt with
                | Some e -> $"new RuntimeException(String.valueOf({transformExpr com e}))"
                | None -> "new RuntimeException()"

            [ $"{indent}throw {inner};" ], None

        | Fable.Set(target, kind, _, value, _) ->
            let targetStr =
                match kind with
                | Fable.SetKind.FieldSet name -> $"{transformExpr com target}.{name}"
                | Fable.SetKind.ExprSet idx -> $"{transformExpr com target}[{transformExpr com idx}]"
                | Fable.SetKind.ValueSet -> transformExpr com target

            [ $"{indent}{targetStr} = {transformExpr com value};" ], None

        | Fable.DecisionTree(expr, _targets) -> transformStmts com expr indent

        | Fable.DecisionTreeSuccess(targetIndex, boundValues, _) ->
            let binds =
                boundValues
                |> List.mapi (fun i v -> $"{indent}/* bound[{i}] = {transformExpr com v} */")

            binds @ [ $"{indent}/* DecisionTreeSuccess target {targetIndex} */" ], None

        | _ -> [], Some(transformExpr com expr)

    // ---------------------------------------------------------------------------
    // Declaration emitter
    // ---------------------------------------------------------------------------

    /// Decide if a body expression can be represented as a Java field initializer.
    let private isSimpleExpr (expr: Fable.Expr) =
        match expr with
        | Fable.Value _ -> true
        | Fable.IdentExpr _ -> true
        | Fable.Operation _ -> true
        | Fable.TypeCast(inner, _) ->
            match inner with
            | Fable.Value _
            | Fable.IdentExpr _
            | Fable.Operation _ -> true
            | _ -> false
        | _ -> false

    /// Emit a module-level or standalone member (static methods / values).
    let private transformMemberDecl (com: Compiler) (decl: Fable.MemberDecl) : string list =
        let jName = javaIdent decl.Name

        if decl.Args.IsEmpty then
            if isSimpleExpr decl.Body then
                let jType = transformType decl.Body.Type
                [ $"    public static final {jType} {jName} = {transformExpr com decl.Body};" ]
            else
                let jType = transformType decl.Body.Type

                let retType =
                    if jType = "void" then
                        "Object"
                    else
                        jType

                let stmts, retVal = transformStmts com decl.Body "        "

                let retLine =
                    retVal |> Option.map (fun v -> $"        return {v};") |> Option.toList

                [ $"    public static {retType} {jName}() {{" ] @ stmts @ retLine @ [ "    }" ]
        else
            let genericParamNames =
                (decl.Args |> List.collect (fun a -> collectGenericParams a.Type))
                @ collectGenericParams decl.Body.Type
                |> List.distinct

            let genericDecl =
                if genericParamNames.IsEmpty then
                    ""
                else
                    let joined = String.concat ", " genericParamNames
                    $"<{joined}> "

            let retType = transformType decl.Body.Type

            let retTypeStr =
                if retType = "void" then
                    "void"
                else
                    retType

            let paramList =
                decl.Args
                |> List.map (fun a ->
                    let t = transformType a.Type
                    $"{t} {javaIdent a.Name}"
                )
                |> String.concat ", "

            let stmts, retVal = transformStmts com decl.Body "        "

            let retLine =
                match retTypeStr, retVal with
                | "void", _ -> []
                | _, Some v -> [ $"        return {v};" ]
                | _, None -> []

            [ $"    public static {genericDecl}{retTypeStr} {jName}({paramList}) {{" ]
            @ stmts
            @ retLine
            @ [ "    }" ]

    /// Emit an instance method (member of a class/record/DU).
    /// The first arg with IsThisArgument=true becomes `this` inside the body (via IdentExpr handling)
    /// and is omitted from the parameter list.
    let private transformAttachedMember (com: Compiler) (decl: Fable.MemberDecl) : string list =
        let jName = javaIdent decl.Name
        let hasThis = decl.Args |> List.exists (fun a -> a.IsThisArgument)
        let methodArgs = decl.Args |> List.filter (fun a -> not a.IsThisArgument)

        let staticMod =
            if hasThis then
                ""
            else
                "static "

        let genericParamNames =
            (methodArgs |> List.collect (fun a -> collectGenericParams a.Type))
            @ collectGenericParams decl.Body.Type
            |> List.distinct

        let genericDecl =
            if genericParamNames.IsEmpty then
                ""
            else
                let joined = String.concat ", " genericParamNames
                $"<{joined}> "

        let retType = transformType decl.Body.Type

        let retTypeStr =
            if retType = "void" then
                "void"
            else
                retType

        let paramList =
            methodArgs
            |> List.map (fun a ->
                let t = transformType a.Type
                $"{t} {javaIdent a.Name}"
            )
            |> String.concat ", "

        let indent = "        "
        let stmts, retVal = transformStmts com decl.Body indent

        let retLine =
            match retTypeStr, retVal with
            | "void", _ -> []
            | _, Some v -> [ $"{indent}return {v};" ]
            | _, None -> []

        [ $"    public {staticMod}{genericDecl}{retTypeStr} {jName}({paramList}) {{" ]
        @ stmts
        @ retLine
        @ [ "    }" ]

    /// Emit a Java class for an F# record.
    let private transformRecordDecl (com: Compiler) (entity: Fable.Entity) (decl: Fable.ClassDecl) : string list =
        let className = javaIdent decl.Name
        let fields = entity.FSharpFields

        let fieldDecls =
            fields
            |> List.map (fun f ->
                let jType = transformType f.FieldType
                let jName = javaIdent f.Name

                let modifier =
                    if f.IsMutable then
                        ""
                    else
                        "final "

                $"    public {modifier}{jType} {jName};"
            )

        let ctorParams =
            fields
            |> List.map (fun f ->
                let jType = transformType f.FieldType
                let jName = javaIdent f.Name
                $"{jType} {jName}"
            )
            |> String.concat ", "

        let ctorBody =
            fields
            |> List.map (fun f ->
                let jName = javaIdent f.Name
                $"        this.{jName} = {jName};"
            )

        let ctorLines =
            [ $"    public {className}({ctorParams}) {{" ] @ ctorBody @ [ "    }" ]

        let memberLines = decl.AttachedMembers |> List.collect (transformAttachedMember com)

        [ $"public static final class {className} {{" ]
        @ fieldDecls
        @ [ "" ]
        @ ctorLines
        @ memberLines
        @ [ "    }" ]

    /// Emit an abstract Java base class + static inner subclasses for an F# DU.
    let private transformDUDecl (com: Compiler) (entity: Fable.Entity) (decl: Fable.ClassDecl) : string list =
        let className = javaIdent decl.Name
        let cases = entity.UnionCases

        let innerCaseLines =
            cases
            |> List.mapi (fun tag case ->
                let caseName = javaIdent case.Name
                let caseFields = case.UnionCaseFields

                let fieldDecls =
                    caseFields
                    |> List.map (fun f ->
                        let jType = transformType f.FieldType
                        let jName = javaIdent f.Name
                        $"        public final {jType} {jName};"
                    )

                let ctorParams =
                    caseFields
                    |> List.map (fun f ->
                        let jType = transformType f.FieldType
                        let jName = javaIdent f.Name
                        $"{jType} {jName}"
                    )
                    |> String.concat ", "

                let ctorBody =
                    caseFields
                    |> List.map (fun f ->
                        let jName = javaIdent f.Name
                        $"            this.{jName} = {jName};"
                    )

                let ctorLines =
                    [ $"        public {caseName}({ctorParams}) {{" ] @ ctorBody @ [ "        }" ]

                [ $"    public static final class {caseName} extends {className} {{" ]
                @ fieldDecls
                @ ctorLines
                @ [ $"        @Override public int tag() {{ return {tag}; }}" ]
                @ [ "    }" ]
            )
            |> List.concat

        let memberLines = decl.AttachedMembers |> List.collect (transformAttachedMember com)

        [ $"public static abstract class {className} {{" ]
        @ [ "    public abstract int tag();" ]
        @ innerCaseLines
        @ memberLines
        @ [ "    }" ]

    /// Emit a general F# class (object with constructor and instance members).
    let private transformClassDecl (com: Compiler) (entity: Fable.Entity) (decl: Fable.ClassDecl) : string list =
        let className = javaIdent decl.Name

        // Extract fields from entity (covers FSharp-defined fields/properties)
        let entityFields = entity.FSharpFields

        let fieldDecls =
            entityFields
            |> List.map (fun f ->
                let jType = transformType f.FieldType
                let jName = javaIdent f.Name
                $"    public {jType} {jName};"
            )

        let ctorLines =
            match decl.Constructor with
            | None -> []
            | Some ctor ->
                let ctorArgs =
                    ctor.Args
                    |> List.filter (fun a -> not a.IsThisArgument)
                    |> List.map (fun a ->
                        let jType = transformType a.Type
                        $"{jType} {javaIdent a.Name}"
                    )
                    |> String.concat ", "

                let stmts, _ = transformStmts com ctor.Body "        "

                [ $"    public {className}({ctorArgs}) {{" ] @ stmts @ [ "    }" ]

        let memberLines = decl.AttachedMembers |> List.collect (transformAttachedMember com)

        [ $"public static class {className} {{" ]
        @ fieldDecls
        @ ctorLines
        @ memberLines
        @ [ "    }" ]

    let rec private transformDeclaration (com: Compiler) (decl: Fable.Declaration) : string list =
        match decl with
        | Fable.Declaration.MemberDeclaration d -> transformMemberDecl com d

        | Fable.Declaration.ActionDeclaration d ->
            let stmts, _ = transformStmts com d.Body "        "

            if stmts.IsEmpty then
                []
            else
                [ "    static {" ] @ stmts @ [ "    }" ]

        | Fable.Declaration.ClassDeclaration d ->
            let entity = com.GetEntity(d.Entity)

            if entity.IsFSharpRecord || entity.IsFSharpExceptionDeclaration then
                transformRecordDecl com entity d
            elif entity.IsFSharpUnion then
                transformDUDecl com entity d
            else
                transformClassDecl com entity d

        | Fable.Declaration.ModuleDeclaration d -> d.Members |> List.collect (transformDeclaration com)

    // ---------------------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------------------

    let transformFile (com: Compiler) (file: Fable.AST.Fable.File) : File =
        let packageName = getPackageName com
        let className = getClassName com

        // All declarations go inside a single `public final class ClassName {}` wrapper.
        // Records, DUs, and classes are emitted as `public static` nested classes.
        // This ensures each .java file has exactly one public top-level class.
        let memberLines = file.Declarations |> List.collect (transformDeclaration com)

        let outputLines =
            if memberLines.IsEmpty then
                [ $"public final class {className} {{}}" ]
            else
                [ $"public final class {className} {{" ] @ memberLines @ [ "}" ]

        {
            Package = Some packageName
            Imports = []
            Declarations = outputLines
        }
