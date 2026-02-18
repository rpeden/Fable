module Fable.Core.Java

open System
open Fable.Core

/// Destructure a tuple of arguments and apply them to literal code as with EmitAttribute.
/// E.g. `emitExpr (arg1, arg2) "$0 + $1"` becomes `arg1 + arg2`
let emitExpr<'T> (args: obj) (code: string) : 'T = nativeOnly

/// Same as emitExpr but intended for code that must appear in statement position
/// (so it can contain `return`, `break`, loops, etc)
/// E.g. `emitStatement aValue "while($0 < 5) { doSomething(); }"`
let emitStatement<'T> (args: obj) (code: string) : 'T = nativeOnly

/// Works like `ImportAttribute` for Java backend imports.
let import<'T> (selector: string) (path: string) : 'T = nativeOnly

/// Must be immediately assigned to a value in a let binding.
/// Imports a member from the external module with same name as value in binding.
let importMember<'T> (path: string) : 'T = nativeOnly

/// Imports a whole external module.
let importAll<'T> (path: string) : 'T = nativeOnly
