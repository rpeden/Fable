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
        | Fable.Type.GenericParam(name, _, _) -> name
        | Fable.Type.LambdaType _ -> "Object" // functional interface – erased at this stage
        | Fable.Type.Tuple _ -> "Object" // tuple helper – deferred
        | _ -> "Object"

    // ---------------------------------------------------------------------------
    // Expression emitter
    // ---------------------------------------------------------------------------

    let private escapeJavaString (s: string) =
        s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")

    let rec private transformExpr (expr: Fable.Expr) : string =
        match expr with
        | Fable.Value(kind, _) ->
            match kind with
            | Fable.ValueKind.UnitConstant -> "null"
            | Fable.ValueKind.BoolConstant b -> if b then "true" else "false"
            | Fable.ValueKind.CharConstant c -> $"'{c}'"
            | Fable.ValueKind.StringConstant s -> $"\"{escapeJavaString s}\""
            | Fable.ValueKind.Null _ -> "null"
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
                    if Single.IsNaN(n) then "Float.NaN"
                    elif Single.IsPositiveInfinity(n) then "Float.POSITIVE_INFINITY"
                    elif Single.IsNegativeInfinity(n) then "Float.NEGATIVE_INFINITY"
                    else $"{n}f"
                | Fable.NumberValue.Float64 n ->
                    if Double.IsNaN(n) then "Double.NaN"
                    elif Double.IsPositiveInfinity(n) then "Double.POSITIVE_INFINITY"
                    elif Double.IsNegativeInfinity(n) then "Double.NEGATIVE_INFINITY"
                    else string n
                | Fable.NumberValue.Decimal n -> $"new java.math.BigDecimal(\"{n}\")"
                | _ -> "/* unsupported number */"
            | Fable.ValueKind.StringTemplate(None, parts, values) ->
                let chunks =
                    parts
                    |> List.mapi (fun i part ->
                        let p = $"\"{escapeJavaString part}\""

                        if i < List.length values then
                            $"{p} + {transformExpr values.[i]}"
                        else
                            p
                    )

                "(" + String.concat " + " chunks + ")"
            | Fable.ValueKind.NewOption(None, _, _) -> "null"
            | Fable.ValueKind.NewOption(Some v, _, _) -> transformExpr v
            | Fable.ValueKind.NewList(None, _) -> "null"
            | _ -> "/* TODO:value */"

        | Fable.IdentExpr id -> javaIdent id.Name

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

                $"({transformExpr left} {jOp} {transformExpr right})"

            | Fable.OperationKind.Unary(op, operand) ->
                let jOp =
                    match op with
                    | UnaryMinus -> "-"
                    | UnaryPlus -> "+"
                    | UnaryNot -> "!"
                    | UnaryNotBitwise -> "~"
                    | UnaryAddressOf -> ""

                $"({jOp}{transformExpr operand})"

            | Fable.OperationKind.Logical(op, left, right) ->
                let jOp = match op with LogicalAnd -> "&&" | LogicalOr -> "||"
                $"({transformExpr left} {jOp} {transformExpr right})"

        | Fable.IfThenElse(guard, thenExpr, elseExpr, _) ->
            $"({transformExpr guard} ? {transformExpr thenExpr} : {transformExpr elseExpr})"

        | Fable.Get(expr, Fable.GetKind.FieldGet field, _, _) ->
            $"{transformExpr expr}.{field.Name}"

        | Fable.Get(expr, Fable.GetKind.TupleIndex i, _, _) ->
            $"{transformExpr expr}.item{i + 1}"

        | Fable.Get(expr, Fable.GetKind.ListHead, _, _) ->
            $"{transformExpr expr}.head()"

        | Fable.Get(expr, Fable.GetKind.ListTail, _, _) ->
            $"{transformExpr expr}.tail()"

        | Fable.Get(expr, Fable.GetKind.OptionValue, _, _) ->
            $"{transformExpr expr}.getValue()"

        | Fable.TypeCast(expr, _) -> transformExpr expr

        | Fable.Sequential exprs ->
            match List.rev exprs with
            | [] -> "null"
            | last :: _ -> transformExpr last

        | Fable.Let(_, _, body) ->
            // In expression context, let-bind is a complex expression: fall back to the body
            transformExpr body

        | Fable.Call(callee, info, _, _) ->
            let calleeStr =
                match callee with
                | Fable.IdentExpr id -> javaIdent id.Name
                | Fable.Get(obj, Fable.GetKind.FieldGet field, _, _) ->
                    $"{transformExpr obj}.{field.Name}"
                | other -> transformExpr other

            let argsStr = info.Args |> List.map transformExpr |> String.concat ", "
            $"{calleeStr}({argsStr})"

        | Fable.CurriedApply(applied, args, _, _) ->
            let appStr = transformExpr applied
            let argsStr = args |> List.map transformExpr |> String.concat ", "
            $"{appStr}({argsStr})"

        | Fable.Lambda(arg, body, _) ->
            let argStr = javaIdent arg.Name
            $"({argStr}) -> {transformExpr body}"

        | Fable.Delegate(args, body, _, _) ->
            let argsStr =
                args |> List.map (fun a -> javaIdent a.Name) |> String.concat ", "

            $"({argsStr}) -> {transformExpr body}"

        | Fable.Import(info, _, _) ->
            // Resolve to the selector name; imports are tracked separately in a real impl
            info.Selector

        | Fable.Emit(info, _, _) ->
            applyEmitMacro info.Macro
                (info.CallInfo.Args |> List.map transformExpr)

        | Fable.Test(expr, Fable.TestKind.OptionTest isSome, _) ->
            let call = if isSome then "isSome" else "isNone"
            $"{transformExpr expr}.{call}()"

        | Fable.Test(expr, Fable.TestKind.ListTest isCons, _) ->
            if isCons then
                $"({transformExpr expr} != null)"
            else
                $"({transformExpr expr} == null)"

        | Fable.Test(expr, Fable.TestKind.UnionCaseTest tag, _) ->
            $"({transformExpr expr}.tag() == {tag})"

        | _ -> "/* TODO:expr */"

    // ---------------------------------------------------------------------------
    // Statement emitter
    // Returns (statement lines, captured expression value)
    // ---------------------------------------------------------------------------

    let rec private transformStmts (expr: Fable.Expr) (indent: string) : string list * string option =
        match expr with
        | Fable.Let(ident, value, body) ->
            let jType = transformType ident.Type
            let modifier = if ident.IsMutable then "" else "final "
            let idName = javaIdent ident.Name
            let decl = $"{indent}{modifier}{jType} {idName} = {transformExpr value};"
            let bodyStmts, bodyVal = transformStmts body indent
            decl :: bodyStmts, bodyVal

        | Fable.LetRec(bindings, body) ->
            let declStmts =
                bindings
                |> List.map (fun (ident, value) ->
                    let jType = transformType ident.Type
                    let modifier = if ident.IsMutable then "" else "final "
                    $"{indent}{modifier}{jType} {javaIdent ident.Name} = {transformExpr value};"
                )

            let bodyStmts, bodyVal = transformStmts body indent
            declStmts @ bodyStmts, bodyVal

        | Fable.Sequential exprs ->
            let rec go =
                function
                | [] -> [], None
                | [ last ] -> transformStmts last indent
                | x :: rest ->
                    let xStmts, xVal = transformStmts x indent
                    let discard = xVal |> Option.map (fun v -> $"{indent}{v};") |> Option.toList
                    let restStmts, restVal = go rest
                    xStmts @ discard @ restStmts, restVal

            go exprs

        | Fable.IfThenElse(guard, thenExpr, elseExpr, _) ->
            let guardStr = transformExpr guard
            let inner = indent + "    "
            let thenStmts, thenVal = transformStmts thenExpr inner
            let elseStmts, elseVal = transformStmts elseExpr inner

            match thenStmts, elseStmts, thenVal, elseVal with
            | [], [], Some tv, Some ev ->
                // Pure expression branches → ternary
                [], Some $"({guardStr} ? {tv} : {ev})"
            | _ ->
                let thenReturn = thenVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList
                let elseReturn = elseVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

                let block =
                    [ $"{indent}if ({guardStr}) {{" ]
                    @ thenStmts @ thenReturn
                    @ [ $"{indent}}} else {{" ]
                    @ elseStmts @ elseReturn
                    @ [ $"{indent}}}" ]

                block, None

        | Fable.WhileLoop(guard, body, _) ->
            let bodyStmts, _ = transformStmts body (indent + "    ")
            [ $"{indent}while ({transformExpr guard}) {{" ] @ bodyStmts @ [ $"{indent}}}" ], None

        | Fable.ForLoop(ident, start, limit, body, isUp, _) ->
            let id = javaIdent ident.Name
            let cmp = if isUp then "<=" else ">="
            let op = if isUp then "++" else "--"
            let bodyStmts, _ = transformStmts body (indent + "    ")

            [ $"{indent}for (int {id} = {transformExpr start}; {id} {cmp} {transformExpr limit}; {id}{op}) {{" ]
            @ bodyStmts
            @ [ $"{indent}}}" ], None

        | Fable.TryCatch(body, catch, finalizer, _) ->
            let inner = indent + "    "
            let bodyStmts, bodyVal = transformStmts body inner
            let bodyReturn = bodyVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList
            let tryBlock = [ $"{indent}try {{" ] @ bodyStmts @ bodyReturn @ [ $"{indent}}}" ]

            let catchBlock =
                match catch with
                | None -> []
                | Some(ident, handler) ->
                    let id = javaIdent ident.Name
                    let hStmts, hVal = transformStmts handler inner
                    let hReturn = hVal |> Option.map (fun v -> $"{inner}return {v};") |> Option.toList

                    [ $"{indent}catch (Exception {id}) {{" ] @ hStmts @ hReturn @ [ $"{indent}}}" ]

            let finallyBlock =
                match finalizer with
                | None -> []
                | Some fin ->
                    let fStmts, _ = transformStmts fin inner
                    [ $"{indent}finally {{" ] @ fStmts @ [ $"{indent}}}" ]

            tryBlock @ catchBlock @ finallyBlock, None

        | Fable.Extended(Fable.ExtendedSet.Throw(exprOpt, _), _) ->
            let inner =
                match exprOpt with
                | Some e -> $"new RuntimeException(String.valueOf({transformExpr e}))"
                | None -> "new RuntimeException()"

            [ $"{indent}throw {inner};" ], None

        | Fable.Set(target, kind, _, value, _) ->
            let targetStr =
                match kind with
                | Fable.SetKind.FieldSet name -> $"{transformExpr target}.{name}"
                | Fable.SetKind.ExprSet idx -> $"{transformExpr target}[{transformExpr idx}]"
                | Fable.SetKind.ValueSet -> transformExpr target

            [ $"{indent}{targetStr} = {transformExpr value};" ], None

        | Fable.DecisionTree(expr, targets) ->
            // Flatten the decision tree to a series of if/else via target bindings
            // Store targets for DecisionTreeSuccess lookups
            let stmts, v = transformStmts expr indent
            stmts, v

        | Fable.DecisionTreeSuccess(targetIndex, boundValues, _) ->
            // Emit as a comment for now – full decision tree requires target threading
            [ $"{indent}/* DecisionTreeSuccess target {targetIndex} */" ], None

        | _ ->
            // Leaf expression
            [], Some(transformExpr expr)

    // ---------------------------------------------------------------------------
    // Declaration emitter
    // ---------------------------------------------------------------------------

    /// Decide if a body expression can be represented as a Java initializer expression
    /// (i.e., contains no statements like let-bindings or control flow).
    let private isSimpleExpr (expr: Fable.Expr) =
        match expr with
        | Fable.Value _ -> true
        | Fable.IdentExpr _ -> true
        | Fable.Operation _ -> true
        | Fable.TypeCast(inner, _) ->
            match inner with
            | Fable.Value _ | Fable.IdentExpr _ | Fable.Operation _ -> true
            | _ -> false
        | _ -> false

    let private transformMemberDecl (decl: Fable.MemberDecl) : string list =
        let jName = javaIdent decl.Name

        if decl.Args.IsEmpty then
            // Module-level value
            if isSimpleExpr decl.Body then
                let jType = transformType decl.Body.Type
                [ $"    public static final {jType} {jName} = {transformExpr decl.Body};" ]
            else
                // Complex initializer → static method
                let jType = transformType decl.Body.Type
                let retType = if jType = "void" then "Object" else jType
                let stmts, retVal = transformStmts decl.Body "        "
                let retLine = retVal |> Option.map (fun v -> $"        return {v};") |> Option.toList

                [ $"    public static {retType} {jName}() {{" ]
                @ stmts @ retLine
                @ [ "    }" ]
        else
            // Function / method
            let retType = transformType decl.Body.Type
            let retTypeStr = if retType = "void" then "void" else retType

            let paramList =
                decl.Args
                |> List.map (fun a ->
                    let t = transformType a.Type
                    $"{t} {javaIdent a.Name}"
                )
                |> String.concat ", "

            let stmts, retVal = transformStmts decl.Body "        "

            let retLine =
                match retTypeStr, retVal with
                | "void", _ -> []
                | _, Some v -> [ $"        return {v};" ]
                | _, None -> []

            [ $"    public static {retTypeStr} {jName}({paramList}) {{" ]
            @ stmts @ retLine
            @ [ "    }" ]

    let rec private transformDeclaration (decl: Fable.Declaration) : string list =
        match decl with
        | Fable.Declaration.MemberDeclaration d -> transformMemberDecl d

        | Fable.Declaration.ActionDeclaration d ->
            let stmts, _ = transformStmts d.Body "        "

            if stmts.IsEmpty then
                []
            else
                [ "    static {" ] @ stmts @ [ "    }" ]

        | Fable.Declaration.ClassDeclaration d ->
            // Emit a stub inner class so the file at least compiles
            [ $"    // class {d.Name} (transformation not yet implemented)" ]

        | Fable.Declaration.ModuleDeclaration d ->
            // Flatten nested module members into the enclosing class
            d.Members |> List.collect transformDeclaration

    // ---------------------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------------------

    let transformFile (com: Compiler) (file: Fable.AST.Fable.File) : File =
        let packageName = getPackageName com
        let className = getClassName com
        let memberLines = file.Declarations |> List.collect transformDeclaration

        let classLines =
            if memberLines.IsEmpty then
                [ $"public final class {className} {{}}" ]
            else
                [ $"public final class {className} {{" ] @ memberLines @ [ "}" ]

        {
            Package = Some packageName
            Imports = []
            Declarations = classLines
        }
