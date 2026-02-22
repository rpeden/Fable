module Fable.Tests.Java.JavaCompilerSmoke

open System.IO
open Fable.Tests.Java.JavaTestHelpers
open Util.Testing

let tests =
    testList "Java Compiler Smoke" [
        // ---- Basic compilation ----
        testCase "compiles with --lang java without compiler errors" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet value = 42" None
            errors.Length |> equal 0

        testCase "writes .java output files" <| fun _ ->
            let errors, _ = compileAndRead "module Program\nlet value = 42" None
            errors.Length |> equal 0
            let javaFiles = Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
            javaFiles.Length > 0 |> equal true

        testCase "writes package declaration derived from project path" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet value = 42" None
            errors.Length |> equal 0
            content.Contains("package testproject;") |> equal true

        // ---- Value and function emission ----
        testCase "emits field for module-level let value binding" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet value = 42" (Some "field-binding")
            errors.Length |> equal 0
            (content.Contains("value") && content.Contains("42")) |> equal true

        testCase "emits static method for module-level function" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet add a b = a + b" (Some "static-method")
            errors.Length |> equal 0
            content.Contains("add") |> equal true

        testCase "lambda value emits typed Functional.Func1 signature" <| fun _ ->
            let src = "module Program\nlet apply (f: int -> int) x = f x"
            let errors, content = compileAndRead src (Some "lambda-func1-type")
            errors.Length |> equal 0
            content.Contains("fable.library.Functional.Func1<Integer, Integer>") |> equal true

        testCase "emits boolean constant correctly" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet flag = true" None
            errors.Length |> equal 0
            content.Contains("true") |> equal true

        testCase "emits string constant correctly" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet greeting = \"hello\"" None
            errors.Length |> equal 0
            content.Contains("hello") |> equal true

        // ---- Generics ----
        testCase "generic function emits type parameter declaration" <| fun _ ->
            let errors, content = compileAndRead "module Program\nlet identity x = x" (Some "generic-function")
            errors.Length |> equal 0
            content.Contains("<") |> equal true

        // ---- Records and DUs ----
        testCase "F# record emits Java class with fields" <| fun _ ->
            let errors, content = compileAndRead "module Program\ntype Point = { X: int; Y: int }" (Some "record")
            errors.Length |> equal 0
            (content.Contains("Point") && content.Contains("X") && content.Contains("Y")) |> equal true

        testCase "F# record emits structural equals hashCode and toString" <| fun _ ->
            let errors, content = compileAndRead "module Program\ntype Point = { X: int; Y: int }" (Some "record-structural")
            errors.Length |> equal 0
            content.Contains("@Override public boolean equals(Object obj)") |> equal true
            content.Contains("@Override public int hashCode()") |> equal true
            content.Contains("@Override public String toString()") |> equal true

        testCase "F# DU emits abstract base class with inner subclasses" <| fun _ ->
            let src = "module Program\ntype Shape = | Circle of Radius: double | Rectangle of Width: double * Height: double"
            let errors, content = compileAndRead src (Some "du")
            errors.Length |> equal 0
            (content.Contains("Shape") && content.Contains("Circle") && content.Contains("Rectangle")) |> equal true
            content.Contains("enum Tag { Circle, Rectangle }") |> equal true
            content.Contains("abstract Tag tag()") |> equal true
            content.Contains("return Tag.Circle") |> equal true

        testCase "F# DU cases emit structural equals hashCode and toString" <| fun _ ->
            let src = "module Program\ntype Shape = | Circle of Radius: double | Rectangle of Width: double * Height: double"
            let errors, content = compileAndRead src (Some "du-structural")
            errors.Length |> equal 0
            content.Contains("if (!(obj instanceof Circle)) return false;") |> equal true
            content.Contains("if (!(obj instanceof Rectangle)) return false;") |> equal true
            content.Contains("java.util.Objects.hash(Shape.Tag.Circle") |> equal true
            content.Contains("java.util.Objects.hash(Shape.Tag.Rectangle") |> equal true

        testCase "explicit downcast currently emits helper call" <| fun _ ->
            let src = """module Program
let asString (x: obj) = x :?> string
"""
            let errors, content = compileAndRead src (Some "typecast-downcast")
            errors.Length |> equal 0
            content.Contains("downcast(x)") |> equal true

        // ---- Classes with instance methods ----
        testCase "F# class constructor initializes instance fields" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let errors, content = compileAndRead src (Some "class-constructor")
            errors.Length |> equal 0
            (content.Contains("Counter") && content.Contains("count") && content.Contains("initial")) |> equal true

        testCase "F# class instance methods are inside the class body" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
    member this.Add n = count <- count + n
"""
            let errors, content = compileAndRead src None
            errors.Length |> equal 0
            content.Contains("Counter__Increment") |> equal false
            content.Contains("Counter__get_Value") |> equal false
            content.Contains("increment") |> equal true
            content.Contains("getValue") |> equal true

        testCase "F# class instance methods have no unit parameter" <| fun _ ->
            let src = """module Program
type Counter(initial: int) =
    let mutable count = initial
    member this.Value = count
    member this.Increment() = count <- count + 1
"""
            let errors, content = compileAndRead src None
            errors.Length |> equal 0
            content.Contains("unitVar") |> equal false
            content.Contains("void unitVar") |> equal false

        // ---- Multi-file ----
        testCase "multi-file compilation uses distinct package paths" <| fun _ ->
            let originalProject = File.ReadAllText(projectFile)
            let originalProgram = File.ReadAllText(sourceFile)
            let subDir = Path.Combine(projectDir, "Sub")
            let subFile = Path.Combine(subDir, "Program.fs")

            try
                Directory.CreateDirectory(subDir) |> ignore
                File.WriteAllText(sourceFile, "module RootProgram\nlet value = 1")
                File.WriteAllText(subFile, "module SubProgram\nlet value = 2")

                File.WriteAllText(
                    projectFile,
                    """<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RollForward>Major</RollForward>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="Program.fs" />
        <Compile Include="Sub/Program.fs" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../../../src/Fable.Core/Fable.Core.fsproj" />
    </ItemGroup>

</Project>"""
                )

                let _, errors = compileJava "module RootProgram\nlet value = 1"
                errors.Length |> equal 0

                let javaFiles = Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories)
                javaFiles.Length >= 2 |> equal true

                let generated = javaFiles |> Array.map File.ReadAllText |> String.concat "\n\n"
                generated.Contains("package testproject;") |> equal true
                generated.Contains("package testproject.sub;") |> equal true
            finally
                File.WriteAllText(projectFile, originalProject)
                File.WriteAllText(sourceFile, originalProgram)

                if File.Exists(subFile) then
                    File.Delete(subFile)

                if Directory.Exists(subDir) && Directory.GetFileSystemEntries(subDir).Length = 0 then
                    Directory.Delete(subDir)

        // -----------------------------------------------------------------------
        // Pattern matching (DecisionTree) — string content checks
        // -----------------------------------------------------------------------

        testCase "DU match emits target body expressions, not placeholders" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Square of float

let area (s: Shape) =
    match s with
    | Circle r -> 3.14 * r * r
    | Square side -> side * side
"""
            let errors, content = compileAndRead src (Some "du-match")
            errors.Length |> equal 0
            // 3.14 only appears in target[0] body — proves targets are resolved
            content.Contains("3.14") |> equal true
            // Placeholder comments must not survive
            content.Contains("DecisionTreeSuccess") |> equal false

        testCase "integer literal match emits target body strings" <| fun _ ->
            let src = """module Program
let describe (n: int) =
    match n with
    | 1 -> "one"
    | 2 -> "two"
    | _ -> "other"
"""
            let errors, content = compileAndRead src (Some "int-match")
            errors.Length |> equal 0
            // String constants live in targets — only appear if targets are resolved
            content.Contains("\"one\"") |> equal true
            content.Contains("\"two\"") |> equal true
            content.Contains("\"other\"") |> equal true

        testCase "multi-bound DU match emits bound value access" <| fun _ ->
            let src = """module Program
type Shape = Circle of float | Rectangle of float * float

let perimeter (s: Shape) =
    match s with
    | Circle r -> 2.0 * 3.14 * r
    | Rectangle(w, h) -> 2.0 * (w + h)
"""
            let errors, content = compileAndRead src (Some "multi-bound-match")
            errors.Length |> equal 0
            // 3.14 is in target[0] body only
            content.Contains("3.14") |> equal true
            content.Contains("DecisionTreeSuccess") |> equal false

        testCase "three-case DU match emits all target bodies" <| fun _ ->
            let src = """module Program
type Color = Red | Green | Blue

let name (c: Color) =
    match c with
    | Red -> "red"
    | Green -> "green"
    | Blue -> "blue"
"""
            let errors, content = compileAndRead src (Some "three-case-match")
            errors.Length |> equal 0
            content.Contains("\"red\"") |> equal true
            content.Contains("\"green\"") |> equal true
            content.Contains("\"blue\"") |> equal true

        // -----------------------------------------------------------------------
        // Tuple emission
        // -----------------------------------------------------------------------

        testCase "tuple creation emits Tuple2 constructor" <| fun _ ->
            let src = "module Program\nlet pair = (1, 2)"
            let errors, content = compileAndRead src (Some "tuple-create")
            errors.Length |> equal 0
            content.Contains("fable.library.Tuple.Tuple2") |> equal true

        testCase "tuple field access emits item1 item2" <| fun _ ->
            let src = """module Program
let pair = (1, "hello")
let a = fst pair
let b = snd pair
"""
            let errors, content = compileAndRead src (Some "tuple-access")
            errors.Length |> equal 0
            content.Contains(".item1") |> equal true
            content.Contains(".item2") |> equal true

        testCase "3-tuple emits Tuple3 constructor" <| fun _ ->
            let src = "module Program\nlet triple = (1, 2, 3)"
            let errors, content = compileAndRead src (Some "tuple3-create")
            errors.Length |> equal 0
            content.Contains("fable.library.Tuple.Tuple3") |> equal true

        // -----------------------------------------------------------------------
        // List cons emission
        // -----------------------------------------------------------------------

        testCase "list cons emits FSharpList.cons call" <| fun _ ->
            let src = "module Program\nlet xs = [1; 2; 3]"
            let errors, content = compileAndRead src (Some "list-cons")
            errors.Length |> equal 0
            content.Contains("FSharpList.cons") |> equal true

        // -----------------------------------------------------------------------
        // BinaryExponent → Math.pow
        // -----------------------------------------------------------------------

        testCase "exponent operator emits Math.pow" <| fun _ ->
            let src = "module Program\nlet result = 2.0 ** 3.0"
            let errors, content = compileAndRead src (Some "exponent")
            errors.Length |> equal 0
            content.Contains("Math.pow") |> equal true

        // -----------------------------------------------------------------------
        // Operators replacements emit correct Java
        // -----------------------------------------------------------------------

        testCase "failwith emits RuntimeException throw" <| fun _ ->
            let src = """module Program
let boom () = failwith "oops"
"""
            let errors, content = compileAndRead src (Some "failwith")
            errors.Length |> equal 0
            content.Contains("RuntimeException") |> equal true

        testCase "int conversion emits Java cast" <| fun _ ->
            let src = "module Program\nlet n = int 3.14"
            let errors, content = compileAndRead src (Some "int-conversion")
            errors.Length |> equal 0
            content.Contains("(int)") |> equal true

        testCase "pipe operators compile without leakage" <| fun _ ->
            let src = "module Program\nlet result = 3 |> ((+) 2)"
            let errors, _ = compileAndRead src (Some "pipe-operators")
            errors.Length |> equal 0

        testCase "composition operator compiles without leakage" <| fun _ ->
            let src = "module Program\nlet f = ((+) 1) >> ((*) 2)\nlet result = f 5"
            let errors, _ = compileAndRead src (Some "composition-operator")
            errors.Length |> equal 0

        testCase "operator call fallback emits Java operator symbols" <| fun _ ->
            let src = "module Program\nlet add a b = Microsoft.FSharp.Core.Operators.op_Addition a b\nlet value = add 3 4"
            let errors, content = compileAndRead src (Some "op-addition-fallback")
            errors.Length |> equal 0
            content.Contains("+") |> equal true

        testCase "pown emits Math.pow with int exponent cast" <| fun _ ->
            let src = "module Program\nlet value = pown 2.0 3"
            let errors, content = compileAndRead src (Some "pown-operator")
            errors.Length |> equal 0
            content.Contains("Math.pow") |> equal true

        testCase "lock emits Util.lock helper call" <| fun _ ->
            let src = "module Program\nlet gate = obj()\nlet value = lock gate (fun () -> 42)"
            let errors, content = compileAndRead src (Some "lock-operator")
            errors.Length |> equal 0
            content.Contains("Util.lock") |> equal true

        testCase "string module emits correct Java" <| fun _ ->
            let src = """module Program
let x = "hello"
let n = x.Length
let upper = x.ToUpper()
"""
            let errors, content = compileAndRead src (Some "string-methods")
            errors.Length |> equal 0
            (content.Contains("length()") || content.Contains(".length")) |> equal true

        testCase "TrimStart and TrimEnd emit Java 8-compatible replacements" <| fun _ ->
            let src = """module Program
let x = "  hello  "
let a = x.TrimStart()
let b = x.TrimEnd()
"""
            let errors, content = compileAndRead src (Some "string-trim-java8")
            errors.Length |> equal 0
            content.Contains("replaceFirst(\"^\\\\s+\", \"\")") |> equal true
            content.Contains("replaceFirst(\"\\\\s+$\", \"\")") |> equal true
            content.Contains("stripLeading") |> equal false
            content.Contains("stripTrailing") |> equal false

        testCase "String.Split emits literal separator quoting" <| fun _ ->
            let src = """module Program
let parts = "a.b.c".Split(".")
"""
            let errors, content = compileAndRead src (Some "string-split-literal")
            errors.Length |> equal 0
            content.Contains("String.split") |> equal true

        testCase "String.replicate routes to runtime String.replicate" <| fun _ ->
            let src = """module Program
let x = String.replicate 3 "ab"
"""
            let errors, content = compileAndRead src (Some "string-replicate")
            errors.Length |> equal 0
            content.Contains("String.replicate") |> equal true

        // -----------------------------------------------------------------------
        // Import tracking (D1-D5)
        // -----------------------------------------------------------------------

        testCase "Option.map emits import and qualified call" <| fun _ ->
            let src = """module Program
let result = Option.map (fun x -> x + 1) (Some 42)
"""
            let errors, content = compileAndRead src (Some "option-map-import")
            errors.Length |> equal 0
            content.Contains("import fable.library.FSharpOption;") |> equal true
            content.Contains("FSharpOption.map") |> equal true

        testCase "List.map emits import and qualified call" <| fun _ ->
            let src = """module Program
let result = List.map (fun x -> x + 1) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-map-import")
            errors.Length |> equal 0
            content.Contains("import fable.library.FSharpList;") |> equal true
            content.Contains("FSharpList.map") |> equal true

        testCase "Array.length emits import and qualified call" <| fun _ ->
            let src = """module Program
let n = Array.length [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-length-import")
            errors.Length |> equal 0
            content.Contains("import fable.library.Array;") |> equal true
            content.Contains("Array.length") |> equal true

        // -----------------------------------------------------------------------
        // C6 — Collection module expansion coverage
        // -----------------------------------------------------------------------

        testCase "List.filter emits FSharpList.filter call" <| fun _ ->
            let src = """module Program
let result = List.filter (fun x -> x > 1) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-filter")
            errors.Length |> equal 0
            content.Contains("FSharpList.filter") |> equal true

        testCase "List.fold emits FSharpList.fold call" <| fun _ ->
            let src = """module Program
let result = List.fold (fun acc x -> acc + x) 0 [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-fold")
            errors.Length |> equal 0
            content.Contains("FSharpList.fold") |> equal true

        testCase "List.length emits FSharpList.length call" <| fun _ ->
            let src = """module Program
let n = List.length [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-length")
            errors.Length |> equal 0
            content.Contains("FSharpList.length") |> equal true

        testCase "List.rev emits FSharpList.reverse call" <| fun _ ->
            let src = """module Program
let result = List.rev [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-rev")
            errors.Length |> equal 0
            // List.rev compiles as ListModule.Reverse → lowerFirst → "reverse"
            (content.Contains("FSharpList.reverse") || content.Contains("FSharpList.rev")) |> equal true

        testCase "List.exists emits FSharpList.exists call" <| fun _ ->
            let src = """module Program
let result = List.exists (fun x -> x > 2) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-exists")
            errors.Length |> equal 0
            content.Contains("FSharpList.exists") |> equal true

        testCase "List.forall emits FSharpList.forAll call" <| fun _ ->
            let src = """module Program
let result = List.forall (fun x -> x > 0) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-forall")
            errors.Length |> equal 0
            content.Contains("FSharpList.forAll") |> equal true

        testCase "List.append emits FSharpList.append call" <| fun _ ->
            let src = """module Program
let result = List.append [1; 2] [3; 4]
"""
            let errors, content = compileAndRead src (Some "list-append")
            errors.Length |> equal 0
            content.Contains("FSharpList.append") |> equal true

        testCase "List.find emits FSharpList.find call" <| fun _ ->
            let src = """module Program
let result = List.find (fun x -> x > 1) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-find")
            errors.Length |> equal 0
            content.Contains("FSharpList.find") |> equal true

        testCase "List.tryFind emits FSharpList.tryFind call" <| fun _ ->
            let src = """module Program
let result = List.tryFind (fun x -> x > 5) [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-tryfind")
            errors.Length |> equal 0
            content.Contains("FSharpList.tryFind") |> equal true

        testCase "List.head emits FSharpList.head call" <| fun _ ->
            let src = """module Program
let result = List.head [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-head")
            errors.Length |> equal 0
            content.Contains("FSharpList.head") |> equal true

        testCase "List.tail emits FSharpList.tail call" <| fun _ ->
            let src = """module Program
let result = List.tail [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-tail")
            errors.Length |> equal 0
            content.Contains("FSharpList.tail") |> equal true

        testCase "List.isEmpty emits FSharpList.isEmpty call" <| fun _ ->
            let src = """module Program
let result = List.isEmpty [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-isempty")
            errors.Length |> equal 0
            content.Contains("FSharpList.isEmpty") |> equal true

        testCase "List.item emits FSharpList.item call" <| fun _ ->
            let src = """module Program
let result = List.item 1 [1; 2; 3]
"""
            let errors, content = compileAndRead src (Some "list-item")
            errors.Length |> equal 0
            content.Contains("FSharpList.item") |> equal true

        testCase "Array.map emits Array.map call" <| fun _ ->
            let src = """module Program
let result = Array.map (fun x -> x * 2) [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-map")
            errors.Length |> equal 0
            content.Contains("Array.map") |> equal true

        testCase "Array.filter emits Array.filter call" <| fun _ ->
            let src = """module Program
let result = Array.filter (fun x -> x > 1) [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-filter")
            errors.Length |> equal 0
            content.Contains("Array.filter") |> equal true

        testCase "Array.fold emits Array.fold call" <| fun _ ->
            let src = """module Program
let result = Array.fold (fun acc x -> acc + x) 0 [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-fold")
            errors.Length |> equal 0
            content.Contains("Array.fold") |> equal true

        testCase "Array.exists emits Array.exists call" <| fun _ ->
            let src = """module Program
let result = Array.exists (fun x -> x > 2) [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-exists")
            errors.Length |> equal 0
            content.Contains("Array.exists") |> equal true

        testCase "Array.forall emits Array.forAll call" <| fun _ ->
            let src = """module Program
let result = Array.forall (fun x -> x > 0) [|1; 2; 3|]
"""
            let errors, content = compileAndRead src (Some "array-forall")
            errors.Length |> equal 0
            content.Contains("Array.forAll") |> equal true

        testCase "Array.sortBy emits Array.sortBy call" <| fun _ ->
            let src = """module Program
let result = Array.sortBy (fun x -> x) [|3; 1; 2|]
"""
            let errors, content = compileAndRead src (Some "array-sortby")
            errors.Length |> equal 0
            content.Contains("Array.sortBy") |> equal true

        testCase "Option.map emits FSharpOption.map call" <| fun _ ->
            let src = """module Program
let result = Option.map (fun x -> x + 1) (Some 42)
"""
            let errors, content = compileAndRead src (Some "option-map")
            errors.Length |> equal 0
            content.Contains("FSharpOption.map") |> equal true

        testCase "Option.bind emits FSharpOption.bind call" <| fun _ ->
            let src = """module Program
let result = Option.bind (fun x -> if x > 0 then Some x else None) (Some 42)
"""
            let errors, content = compileAndRead src (Some "option-bind")
            errors.Length |> equal 0
            content.Contains("FSharpOption.bind") |> equal true

        testCase "Option.defaultValue emits FSharpOption.defaultValue call" <| fun _ ->
            let src = """module Program
let result = Option.defaultValue 0 (Some 42)
"""
            let errors, content = compileAndRead src (Some "option-defaultvalue")
            errors.Length |> equal 0
            content.Contains("FSharpOption.defaultValue") |> equal true

        testCase "Option.isSome emits FSharpOption.isSome call" <| fun _ ->
            let src = """module Program
let result = Option.isSome (Some 42)
"""
            let errors, content = compileAndRead src (Some "option-issome")
            errors.Length |> equal 0
            content.Contains("FSharpOption.isSome") |> equal true

        testCase "Option.isNone emits FSharpOption.isNone call" <| fun _ ->
            let src = """module Program
let result = Option.isNone None
"""
            let errors, content = compileAndRead src (Some "option-isnone")
            errors.Length |> equal 0
            content.Contains("FSharpOption.isNone") |> equal true

        testCase "Map.add emits Map.add call" <| fun _ ->
            let src = """module Program
let m = Map.ofList [(1, "one"); (2, "two")]
let m2 = Map.add 3 "three" m
"""
            let errors, content = compileAndRead src (Some "map-add")
            errors.Length |> equal 0
            content.Contains("Map.add") |> equal true

        testCase "Map.tryFind emits Map.tryFind call" <| fun _ ->
            let src = """module Program
let m = Map.ofList [(1, "one"); (2, "two")]
let result = Map.tryFind 1 m
"""
            let errors, content = compileAndRead src (Some "map-tryfind")
            errors.Length |> equal 0
            content.Contains("Map.tryFind") |> equal true

        testCase "Set.contains emits Set.contains call" <| fun _ ->
            let src = """module Program
let s = Set.ofList [1; 2; 3]
let result = Set.contains 2 s
"""
            let errors, content = compileAndRead src (Some "set-contains")
            errors.Length |> equal 0
            content.Contains("Set.contains") |> equal true

        testCase "Set.add emits Set.add call" <| fun _ ->
            let src = """module Program
let s = Set.ofList [1; 2; 3]
let s2 = Set.add 4 s
"""
            let errors, content = compileAndRead src (Some "set-add")
            errors.Length |> equal 0
            content.Contains("Set.add") |> equal true
    ]
