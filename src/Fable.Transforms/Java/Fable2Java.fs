module Fable.Transforms.Fable2Java

open Fable
open Fable.AST
open Fable.Transforms.Java

module Compiler =
    let transformFile (_com: Compiler) (_file: Fable.AST.Fable.File) : File =
        {
            Package = None
            Imports = []
            Declarations = [ "public final class Program {}" ]
        }
