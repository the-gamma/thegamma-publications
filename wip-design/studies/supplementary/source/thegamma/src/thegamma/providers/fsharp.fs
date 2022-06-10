﻿// ------------------------------------------------------------------------------------------------
// F# provider makes it possible to use Fable-compiled F# types (even with generics!)
// ------------------------------------------------------------------------------------------------
module TheGamma.TypeProviders.FSharpProvider

open TheGamma
open TheGamma.Babel
open TheGamma.Babel.BabelOperators
open TheGamma.Common
open Fable.Import
open ProviderHelpers

// ------------------------------------------------------------------------------------------------
// Records that represent the JSON with metadata about the F# types
// ------------------------------------------------------------------------------------------------

/// AnyType has `kind` property accessible via `getKind`
type AnyType = obj

type GenericParameterType = 
  { kind : string 
    name : string }

type ArrayType = 
  { kind : string 
    element : AnyType }

type PrimitiveType = 
  { kind : string 
    name : string }

type FunctionType = 
  { kind : string 
    arguments : AnyType[]
    returns : AnyType }

type NamedType = 
  { kind : string 
    name : string
    typargs : AnyType[] }

/// Member has `kind` property accessible via `getKind`
type Member = obj

type Argument = 
  { name : string
    optional : bool
    ``type`` : AnyType }

type MethodMember = 
  { kind : string
    name : string 
    typepars : AnyType[]
    arguments:Argument[]
    returns : AnyType }

type PropertyMember = 
  { kind : string
    name : string 
    returns : AnyType }

type ExportedType = 
  { name : string
    typepars : AnyType[]
    ``static`` : bool 
    instance : string[]
    members : Member[] }

// ------------------------------------------------------------------------------------------------
// Special-case `ObjectTypes` for handling of generics 
// ------------------------------------------------------------------------------------------------

/// Represents a fully applied generic type such as `Series<string, int>`
type GenericType =
  inherit ObjectType
  abstract TypeArguments : Type list
  abstract TypeDefinition : GenericTypeDefinition

/// Represents an applied generic type that may contain type parameters e.g. `Series<string, 'V>`
and GenericTypeSchema = 
  inherit ObjectType
  abstract TypeDefinition : GenericTypeDefinition
  abstract TypeArguments : TypeSchema list
  abstract Substitute : (string -> Type option) -> GenericType

/// Represents a generic type definition such as `Series'2`
and GenericTypeDefinition = 
  inherit ObjectType
  abstract FullName : string
  abstract TypeParameterCount : int
  abstract Apply : TypeSchema list -> GenericTypeSchema

/// Representation of types that may contain type parameters e.g. `Series<string, 'v>[]`
and TypeSchema = 
  | Primitive of Type
  | GenericType of GenericTypeSchema
  | Parameter of string
  | Function of TypeSchema list * TypeSchema
  | List of TypeSchema

// ------------------------------------------------------------------------------------------------
// Operations on types and type schemas
// ------------------------------------------------------------------------------------------------

let rec mapGenericType typ g =
  match typ with 
  | Type.Delayed(f) -> 
      Type.Delayed(Async.CreateNamedFuture "applyTypes" <| async {
        let! res = Async.AwaitFuture f
        return mapGenericType res g })
  | Type.Object(:? GenericTypeDefinition as gtd) -> 
      Type.Object(gtd.Apply([ for t in g gtd -> TypeSchema.Primitive t]).Substitute(fun _ -> None))
  | t -> t

let rec applyTypes typ tyargs =
  match typ with 
  | Type.Delayed(f) -> 
      Type.Delayed(Async.CreateNamedFuture "applyTypes" <| async {
        let! res = Async.AwaitFuture f
        return applyTypes res tyargs })
  | Type.Object(:? GenericTypeDefinition as gtd) -> 
      Type.Object(gtd.Apply([ for t in tyargs -> TypeSchema.Primitive t]).Substitute(fun _ -> None))
  | _ -> failwith "applyTypes: Expected generic type definition"
      
let rec unifyTypes ctx schemas tys = 
  match schemas, tys with
  | [], [] -> Some ctx
  | TypeSchema.GenericType(gs)::ss, Type.Object(:? GenericType as gt)::ts 
      when gt.TypeDefinition.FullName = gs.TypeDefinition.FullName && 
        List.length gs.TypeArguments = List.length gt.TypeArguments ->
      unifyTypes ctx (gs.TypeArguments @ ss) (gt.TypeArguments @ ts)
  | TypeSchema.Primitive(t1)::ss, t2::ts when Types.typesEqual t1 t2 -> unifyTypes ctx ss ts
  | TypeSchema.Parameter(n)::ss, t::ts -> unifyTypes ((n,t)::ctx) ss ts
  | TypeSchema.List(s)::ss, Type.List(t)::ts -> unifyTypes ctx (s::ss) (t::ts)
  | TypeSchema.Function(sa, sr)::ss, Type.Method(ta, tr)::ts when List.length sa = List.length ta -> 
      let ta = [ for ma in ta -> ma.Type, None ]
      let tr = defaultArg (tr ta) Type.Any // TODO: This should probably never be None
      unifyTypes ctx (sr::(sa @ ss)) (tr::((List.map fst ta) @ ts)) 
  | TypeSchema.GenericType(_)::_, _ 
  | TypeSchema.Primitive(_)::_, _ 
  | TypeSchema.List(_)::_, _ 
  | TypeSchema.Function(_)::_, _ 
  | [], _
  | _, [] -> 
    match schemas, tys with
    | s::_, t::_ -> Log.trace("providers", "Failed to unify types %O and %O", s, t)
    | _ -> Log.trace("providers", "Failed to unify types %O and %O", schemas, tys)
    None

let rec substituteTypeParams assigns schema = 
  match schema with
  | TypeSchema.GenericType ts -> Type.Object(ts.Substitute assigns)
  | TypeSchema.Primitive t -> t
  | TypeSchema.List s -> Type.List (substituteTypeParams assigns s)
  | TypeSchema.Parameter n -> match assigns n with Some t -> t | _ -> failwith "substituteTypeParams: unresolved type parameter"
  | TypeSchema.Function(is, rs) -> 
      let args = is |> List.map (fun it -> { MethodArgument.Name = ""; Optional = false; Static = false; Type = substituteTypeParams assigns it })
      Type.Method(args, fun _ -> Some(substituteTypeParams assigns rs)) // TODO: This should check input arguments

let rec partiallySubstituteTypeParams (assigns:string -> Type option) schema = 
  match schema with
  | TypeSchema.Primitive t -> TypeSchema.Primitive t
  | TypeSchema.List s -> TypeSchema.List (partiallySubstituteTypeParams assigns s)
  | TypeSchema.Parameter n when (assigns n).IsSome -> TypeSchema.Primitive(assigns(n).Value) 
  | TypeSchema.Parameter n -> TypeSchema.Parameter n
  | TypeSchema.Function(is, rs) -> 
      TypeSchema.Function
        ( List.map (partiallySubstituteTypeParams assigns) is, 
          partiallySubstituteTypeParams assigns rs )
  | TypeSchema.GenericType ts ->
      { new GenericTypeSchema with
          member x.Members = failwith "Uninstantiated generic type schema"
          member x.TypeEquals _ = failwith "Uninstantiated generic type schema"
          member x.TypeArguments = List.map (partiallySubstituteTypeParams assigns) ts.TypeArguments
          member x.TypeDefinition = ts.TypeDefinition
          member x.Substitute assigns2 =
            ts.Substitute (fun n ->
              match assigns2 n, assigns n with
              | Some t, _ 
              | _, Some t -> Some t
              | _ -> None) } |> TypeSchema.GenericType    
   
/// This way of accessing `kind` of `AnyType` or `Member` works both in .NET and in JS
[<Emit("$0.kind")>]
let getKind (o:obj) : string = 
  o.GetType().GetProperty("kind").GetValue(o) :?> string


// Needs to be delayed to avoid calling lookupNamed too early
let importProvidedType url lookupNamed exp = 
  let rec mapType (t:AnyType) = 
    match getKind t with
    | "primitive" -> 
        ( match (unbox<PrimitiveType> t).name with
          | "object" -> Type.Any
          | "int" | "float" -> Type.Primitive PrimitiveType.Number
          | "string" -> Type.Primitive PrimitiveType.String
          | "bool" -> Type.Primitive PrimitiveType.Bool
          | "unit" -> Type.Primitive PrimitiveType.Unit
          | "date" -> Type.Primitive PrimitiveType.Date
          | t -> failwith ("provideFSharpType: Unsupported type: " + t) )
        |> TypeSchema.Primitive
    | "function"->
        let t = unbox<FunctionType> t
        TypeSchema.Function(List.ofSeq (Array.map mapType t.arguments),mapType t.returns)
    | "named" -> 
        let t = (unbox<NamedType> t)
        let tyargs = List.ofArray (Array.map mapType t.typargs)
        match lookupNamed t.name with
        | Type.Object (:? GenericTypeDefinition as gtd) -> 
            if gtd.TypeParameterCount <> List.length tyargs then 
              failwith "provideFSharpType: Named type has mismatching nuumber of arguments"
            gtd.Apply tyargs |> TypeSchema.GenericType 
        | t -> TypeSchema.Primitive t
    | "parameter" -> TypeSchema.Parameter (unbox<GenericParameterType> t).name
    | "array" -> TypeSchema.List(mapType (unbox<ArrayType> t).element)
    | _ -> failwith "provideFSharpType: Unexpected type"

  let getTypeParameters typars = 
    typars |> Array.map (fun t -> 
      match mapType t with
      | TypeSchema.Parameter(n) -> n
      | _ -> failwith "importProvidedType: expected type parameter") |> List.ofArray

  let generateMembers assigns = 
    exp.members |> Array.choose (fun m ->
      if getKind m = "property" then
        let m = unbox<PropertyMember> m
        let retTyp = substituteTypeParams assigns (mapType m.returns)
        let emitter = { Emit = fun inst -> MemberExpression(inst, IdentifierExpression(m.name, None), false, None) }
        Some { Member.Name = m.name; Type = retTyp; Metadata = []; Emitter = emitter }

      elif getKind m = "method" then
        let m = unbox<MethodMember> m
        let typars = getTypeParameters m.typepars 
        // Do not substitute bound variables
        let assigns n = if List.exists ((=) n) typars then None else assigns n

        let args = [ for a in m.arguments -> a.name, a.optional, partiallySubstituteTypeParams assigns (mapType a.``type``) ]
        let emitter = { Emit = fun inst -> MemberExpression(inst, IdentifierExpression(m.name, None), false, None) }
            
        let retTyp = partiallySubstituteTypeParams assigns (mapType m.returns)
        let retFunc tys =
          let tys = List.map fst tys
          Log.trace("providers", "F# provider unifying: %O, %O", [| for _, _, t in args -> t |], Array.ofList tys)
          match unifyTypes [] [ for _, _, t in args -> t ] tys with 
          | None -> None
          | Some assigns ->
              let assigns =
                assigns 
                |> Seq.groupBy fst
                |> Seq.map (fun (p, tys) ->
                    p, tys |> Seq.fold (fun st (_, ty2) ->
                      match st with
                      | Some ty1 -> if Types.typesEqual ty1 ty2 then Some ty2 else None
                      | None -> None) (Some Type.Any) )
                |> Seq.fold (fun assigns assign ->
                  match assigns, assign with
                  | Some assigns, (p, Some assign) -> Some ((p,assign)::assigns)
                  | _ -> None) (Some [])
              match assigns with
              | Some assigns when List.length assigns = List.length typars ->
                  let assigns = dict assigns
                  let subst n = if assigns.ContainsKey n then Some assigns.[n] else None
                  Some (substituteTypeParams subst retTyp)
              | _ -> None

        // How to show type parameters before they are eliminated?
        let args = [ for n, o, t in args -> { MethodArgument.Name = n; Optional = o; Static = false; Type = substituteTypeParams (fun _ -> Some Type.Any) t } ] 
        Some { Member.Name = m.name; Type = Type.Method(args, retFunc); Metadata = []; Emitter = emitter }
      else None)

  let objectType = 
    match getTypeParameters exp.typepars with
    | [] -> 
        { new ObjectType with
            member x.Members = generateMembers (fun _ -> None) 
            member x.TypeEquals _ = false }
    | typars ->
        { new GenericTypeDefinition with
            member td.TypeParameterCount = List.length typars
            member td.FullName = TypeProvidersRuntime.concatUrl url exp.name
            member td.Members = failwithf "Uninstantiated generic type definition (%s)" td.FullName
            member td.TypeEquals _ = failwithf "Uninstantiated generic type definition (%s)" td.FullName
            member td.Apply tyargs = 
              { new GenericTypeSchema with
                  member x.Members = failwith "Uninstantiated generic type schema"
                  member x.TypeDefinition = td
                  member x.TypeEquals _ = failwith "Uninstantiated generic type schema"
                  member x.Substitute assigns = 
                    // Lazy so that lookupNamed does not get called too early
                    let tyArgLookup = dict (List.zip typars tyargs)
                    let members = lazy generateMembers (fun n ->
                      match tyArgLookup.TryGetValue n with
                      | true, tysch -> Some(substituteTypeParams assigns tysch)
                      | _ -> None)

                    { new GenericType with
                        member x.TypeArguments = List.map (substituteTypeParams assigns ) tyargs
                        member x.TypeDefinition = td
                        member x.Members = members.Value
                        member x.TypeEquals t2 = 
                          match t2 with
                          | :? GenericType as gt ->
                              gt.TypeDefinition.FullName = x.TypeDefinition.FullName &&
                                Types.listsEqual x.TypeArguments gt.TypeArguments Types.typesEqual
                          | _ -> false }
                  member x.TypeArguments = tyargs } } :> _
    
  objectType |> Type.Object

let provideFSharpTypes lookupNamed url =   
  async {
    let! json = Http.Request("GET", url)
    let expTys = jsonParse<ExportedType[]> json
    return
      [ for exp in expTys ->
          let ty = importProvidedType url lookupNamed exp
          if exp.``static`` then           
            let e = exp.instance |> Seq.fold (fun chain s -> 
              match chain with
              | None -> Some(IdentifierExpression(s, None))
              | Some e -> Some(MemberExpression(e, IdentifierExpression(s, None), false, None)) ) None |> Option.get
            let ty = mapGenericType ty (fun gtd -> [ for i in 1 .. gtd.TypeParameterCount -> Type.Any ])
            ProvidedType.GlobalValue(exp.name, [], e, ty)
          else
            ProvidedType.NamedType(exp.name, ty) ] }
