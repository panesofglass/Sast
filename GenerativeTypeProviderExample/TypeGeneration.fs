﻿module GenerativeTypeProviderExample.TypeGeneration

// Outside namespaces and modules
open Microsoft.FSharp.Quotations
open ProviderImplementation.ProvidedTypes // open the providedtypes.fs file
open System.Reflection // necessary if we want to use the f# assembly
open System.Threading.Tasks

// ScribbleProvider specific namespaces and modules
open GenerativeTypeProviderExample.DomainModel
open GenerativeTypeProviderExample.CommunicationAgents
open GenerativeTypeProviderExample.IO

(******************* TYPE PROVIDER'S HELPERS *******************)

// CREATING TYPES, NESTED TYPES, METHODS, PROPERTIES, CONSTRUCTORS
let internal createProvidedType assembly name = 
    ProvidedTypeDefinition(assembly, ns, name, Some baseType, IsErased=false)

let internal createProvidedIncludedType name = 
    ProvidedTypeDefinition(name,Some baseType, IsErased=false)

let internal createProvidedIncludedTypeChoice typing name =
    ProvidedTypeDefinition(name, Some typing , IsErased=false)

let internal createMethodType name param typing expression =
    ProvidedMethod( name, param, typing, InvokeCode = (fun args -> expression ))

let internal createPropertyType name typing expression =
    ProvidedProperty( name , typing , IsStatic = true, GetterCode = (fun args -> expression ))

let internal createCstor param expression = 
    ProvidedConstructor( parameters = param, InvokeCode = (fun args -> expression ))


// ADDING TYPES, NESTED TYPES, METHODS, PROPERTIES, CONSTRUCTORS TO THE ASSEMBLY AND AS MEMBERS OF THE TYPE PROVIDER
let internal addProvidedTypeToAssembly (providedType:ProvidedTypeDefinition)=
    asm.AddTypes([providedType])
    providedType

let internal addIncludedTypeToProvidedType nestedTypeToAdd (providedType:ProvidedTypeDefinition) =
    providedType.AddMembers(nestedTypeToAdd)
    providedType

let internal addMethod methodType (providedType:ProvidedTypeDefinition) = 
    providedType.AddMember methodType
    providedType    

let internal addProperty propertyToAdd (providedType:ProvidedTypeDefinition) =
    providedType.AddMember(propertyToAdd)
    providedType

let internal addCstor cstorToAdd (providedType:ProvidedTypeDefinition) =
    providedType.AddMember(cstorToAdd)
    providedType

let internal addMember (memberInfo:#MemberInfo) (providedType:ProvidedTypeDefinition) = 
    providedType.AddMember(memberInfo)
    providedType

let internal addMembers (membersInfo:#MemberInfo list) (providedType:ProvidedTypeDefinition) = 
    providedType.AddMembers(membersInfo)
    providedType


(******************* TYPE PROVIDER'S FUNCTIONS *******************)

let internal findCurrentIndex current (fsmInstance:ScribbleProtocole.Root []) = // gerer les cas
    let mutable inc = 0
    let mutable index = -1 
    for event in fsmInstance do
        match event.CurrentState with
            |n when n=current -> index <- inc
            | _ -> inc <- inc + 1
    index

let internal findNext index (fsmInstance:ScribbleProtocole.Root []) =
    (fsmInstance.[index].NextState)

let internal findNextIndex currentState (fsmInstance:ScribbleProtocole.Root []) =
    let index = findCurrentIndex currentState fsmInstance in
    let next = findNext index fsmInstance in
    findCurrentIndex next fsmInstance

let internal findSameNext nextState  (fsmInstance:ScribbleProtocole.Root [])  =
    let mutable list = []
    let mutable inc = 0
    for event in fsmInstance do
        if event.NextState = nextState then
            list <- inc::list
        inc <- inc+1
    list

let rec alreadySeen (liste:string list) (s:string) =
    match liste with
        | [] -> false
        | hd::tl -> if hd.Equals(s) then
                        true
                    else
                        alreadySeen tl s

let internal findSameCurrent currentState  (fsmInstance:ScribbleProtocole.Root [])  =
    let mutable list = []
    let mutable inc = 0
    for event in fsmInstance do
        if event.CurrentState = currentState then
            list <- inc::list
        inc <- inc+1
    list


// Test this function by changing t with t+1 and see the mistakes happen  -> generate the useless ProvidedTypeDefinition and throw exception cause it
// is not added to the assembly.
let rec findProvidedType (providedList:ProvidedTypeDefinition list) stateValue =
    match providedList with
        |[] -> // Useless case, t is useless but we need this case due to pattern matching exhaustiveness.
                "CodingMistake" |> createProvidedIncludedType 
        |[a] -> let t = ref 0
                if System.Int32.TryParse(a.Name.Replace("State",""),t) && (!t)=stateValue then
                    a
                else 
                    findProvidedType [] stateValue    
        |hd::tl -> let t = ref 0
                   if System.Int32.TryParse(hd.Name.Replace("State",""),t) && (!t)=stateValue then
                       hd
                   else
                       findProvidedType tl stateValue      

                       
let internal makeRoleTypes (fsmInstance:ScribbleProtocole.Root []) = 
    let mutable liste = [fsmInstance.[0].LocalRole]
    let mutable listeType = []
    let ctor = <@@ () @@> |> createCstor []
    let t = fsmInstance.[0].LocalRole 
                                        |> createProvidedIncludedType 
                                        |> addCstor ctor
    let t = t |> addProperty (Expr.NewObject(ctor,[]) |> createPropertyType "instance" t)
    listeType <- t::listeType
    let mutable mapping = Map.empty<_,ProvidedTypeDefinition>.Add(fsmInstance.[0].LocalRole,t)
    for event in fsmInstance do
        if not(alreadySeen liste event.Partner) then
            let ctor = ( <@@ () @@> |> createCstor [])
            let t = event.Partner 
                                    |> createProvidedIncludedType
                                    |> addCstor ctor    
            let t = t |> addProperty (Expr.NewObject(ctor, []) |> createPropertyType "instance" t)                                                                     
            mapping <- mapping.Add(event.Partner,t)
            liste <- event.Partner::liste
            listeType <- t::listeType
    (mapping,listeType)




let internal makeLabelTypes (fsmInstance:ScribbleProtocole.Root []) (providedList: ProvidedTypeDefinition list) = 
    let mutable listeLabelSeen = []
    let mutable listeType = []
    let mutable choiceIter = 1
    let mutable mapping = Map.empty<_,System.Type>
    for event in fsmInstance do
        if (event.Type.Contains("choice") && not(alreadySeen listeLabelSeen event.Label)) then
            match choiceIter with
                |i when i <= TypeChoices.NUMBER_OF_CHOICES ->   let assem = typeof<TypeChoices.Choice1>.Assembly
                                                                let typeCtor = assem.GetType("GenerativeTypeProviderExample.TypeChoices+Choice" + i.ToString())
                                                                mapping <- mapping.Add("Choice"+ string event.CurrentState,typeCtor)
                                                                listeType <- typeCtor::listeType 
                                                                choiceIter <- choiceIter + 1
                                                                let listIndexChoice = findSameCurrent event.CurrentState fsmInstance
                                                                let nextType = findProvidedType providedList (event.NextState)
                                                                let rec aux (liste:int list) =
                                                                    match liste with
                                                                        |[] -> ()
                                                                        |[aChoice] -> if not(alreadySeen listeLabelSeen fsmInstance.[aChoice].Label) then
                                                                                        let currEvent = fsmInstance.[aChoice] 
                                                                                        let name = currEvent.Label.Replace("(","").Replace(")","") 
                                                                                        
                                                                                        let t = name |> createProvidedIncludedTypeChoice typeCtor
                                                                                                     |> addCstor ([] |> createCstor <|  <@@ () @@>)
                                                                                                     |> addMethod (nextType |> createMethodType "next" [] <| <@@ () @@>)
                                                                                                     
                                                                                        t.SetAttributes(TypeAttributes.Public ||| TypeAttributes.Class)
                                                                                        
                                                                                        mapping <- mapping.Add(fsmInstance.[aChoice].Label,t)
                                                                                        listeLabelSeen <- fsmInstance.[aChoice].Label::listeLabelSeen
                                                                                        listeType <- (t :> System.Type )::listeType  
                                                                                        
                                                                        |hd::tl -> if not(alreadySeen listeLabelSeen fsmInstance.[hd].Label) then
                                                                                        let currEvent = fsmInstance.[hd] 
                                                                                        let name = currEvent.Label.Replace("(","").Replace(")","") 
                                                                                        
                                                                                        let t= name |> createProvidedIncludedTypeChoice typeCtor
                                                                                                    |> addCstor ([] |> createCstor <|  <@@ () @@>)
                                                                                                    |> addMethod (nextType |> createMethodType "next" [] <| <@@ () @@>)
                                                                                        t.SetAttributes(TypeAttributes.Public ||| TypeAttributes.Class)
                                                                                       
                                                                                        mapping <- mapping.Add(fsmInstance.[hd].Label,t)
                                                                                        listeLabelSeen <- fsmInstance.[hd].Label::listeLabelSeen
                                                                                        listeType <- (t :> System.Type )::listeType
                                                                                        
                                                                                        aux tl 
                                                                in aux listIndexChoice 
                | _ -> failwith ("number of choice > " + TypeChoices.NUMBER_OF_CHOICES.ToString() + " : CHAHHH va chez ta mere ") 

        else if not(alreadySeen listeLabelSeen event.Label) then
            let name = event.Label.Replace("(","").Replace(")","") 
            let t = name |> createProvidedIncludedType
                         |> addCstor (<@@ name :> obj @@> |> createCstor [])

            mapping <- mapping.Add(event.Label,t)
            listeLabelSeen <- event.Label::listeLabelSeen
            listeType <- ( t :> System.Type )::listeType
    (mapping,listeType)


let internal makeStateTypeBase (n:int) (s:string) = 
    (s + string n) |> createProvidedIncludedType
                   |> addCstor (<@@ s+ string n @@> |> createCstor [])

let internal makeStateType (n:int) = makeStateTypeBase n "State"


let internal createProvidedParameters (event : ScribbleProtocole.Root) =
    let generic = typeof<Buf<_>>.GetGenericTypeDefinition() 
    let payload = event.Payload
    let mutable n = 0

    [for param in payload do
        n <- n+1
        let genType = generic.MakeGenericType(System.Type.GetType(param))
        yield ProvidedParameter(("Payload_" + string n),genType)] // returns all the buffer


let internal toProvidedList (array:_ []) =
    [for i in 0..(array.Length-1) do
        yield ProvidedParameter(("Payload_" + string i),System.Type.GetType(array.[i]))]

let internal toList (array:_ []) =
    [for elem in array do
        yield elem ]


let rec goingThrough (methodName:string) (providedList:ProvidedTypeDefinition list) (aType:ProvidedTypeDefinition) (indexList:int list) 
                     (mLabel:Map<string,System.Type>) (mRole:Map<string,ProvidedTypeDefinition>) (fsmInstance:ScribbleProtocole.Root []) =
        match indexList with
        |[] -> // Last state: no next state possible
                aType |> addMethod (<@@ printfn "finish" @@> |> createMethodType methodName [] typeof<unit> ) |> ignore
        |[b] -> let nextType = findProvidedType providedList fsmInstance.[b].NextState
                let c = nextType.GetConstructors().[0]
                let exprState = Expr.NewObject(c, [])
                let event = fsmInstance.[b]
                let fullName = event.Label
                let message = serialize fullName
                let role = event.Partner
                let listTypes = 
                    match methodName with
                        |"send" -> toProvidedList event.Payload
                        |"receive" -> createProvidedParameters event
                        | _ -> []
                let listParam = 
                    match methodName with
                        |"send" | "receive" -> List.append [ProvidedParameter("Label",mLabel.[fullName]);ProvidedParameter("Role",mRole.[role])] listTypes
                        | _  -> []
                let listPayload = (toList event.Payload)   
                match methodName with
                    |"send" -> let myMethod = ProvidedMethod(methodName,listParam,nextType,
                                                                IsStaticMethod = false,
                                                                InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                        let buf = serializeMessage fullName (toList event.Payload) buffers
                                                                                        //let buf = serialize fullName
                                                                                        let exprAction = <@@ Regarder.sendMessage "agent" (%%buf:byte[]) role @@>
                                                                                        //let exprAction = <@@ Regarder.sendMessage "agent" buf role @@> 
                                                                                        Expr.Sequential(exprAction,exprState) )
                               aType 
                                    |> addMethod myMethod
                                    |> ignore
                    |"receive" ->  let myMethod = ProvidedMethod(methodName,listParam,nextType,
                                                                    IsStaticMethod = false,
                                                                    InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                            let listPayload = (toList event.Payload)
                                                                                                  (*let mutable exp = []
                                                                                                  for elem in buffers do
                                                                                                    exp <- (Expr.Coerce(elem,typeof<obj>))::exp
                                                                                                  let exprTest = Expr.NewArray(typeof<obj>,exp)*)
                                                                                                  (*let exprAction = <@@ //Regarder.receiveMessage "agent" [message] role  listPayload
                                                                                                                       printf "HEY MATTHIEU"
                                                                                                                       Async.StartAsTask(Regarder.receiveMessage "agent" [message] role  listPayload)
                                                                                                                       //printf "NO WAYYYYYYYYY"
                                                                                                                       //deserializeTest listPayload// received //buffers //received listPayload |> ignore
                                                                                                                       (*received.ContinueWith(fun (listTask:Task<byte[] list>) -> 
                                                                                                                                                    deserializeTest buffers (listTask.Result) listPayload) *)
                                                                                                                                                    (*if listTask.IsCompleted then
                                                                                                                                                        listTask.Result |> List.iteri(fun i buf ->
                                                                                                                                                                                    deserializeTest (%%buffers.[i]) buf listPayload.[i]
                                                                                                                                                                               )
                                                                                                                                                    else
                                                                                                                                                        failwith "Heyyyyyyyyy"                
                                                                                                                                                  )*)
                                                                                                                                                  (*      deserializeTest      )
                                                                                                                       let truc = Async.AwaitIAsyncResult(received) |> ignore
                                                                                                                       
                                                                                                                       async{
                                                                                                                        let! result = Async.AwaitTask(received)*)
                                                                                                                       
                                                                                                                       //deserializeTest (%%exprTest:obj[]) received listPayload
                                                                                                                       
                                                                                                                       //let result = received //Async.RunSynchronously(received) 
                                                                                                                       //result |> List.iter (printfn "ALLEZ PUTINNNNNNNNN : %A")  
                                                                                                                       //received
                                                                                                                       @@> // We can a TimeOut if we wait to long*)
                                                                                                                       
                                                                                                  //let exprDeserialize = deserializeMessage buffers exprAction listPayload
                                                                                                  //let expr = Expr.Sequential(exprTest,exprAction)
                                                                                            let exprDes = deserialize buffers listPayload [message] role
                                                                                            Expr.Sequential(exprDes,exprState) )
                                   let myMethodAsync =  ProvidedMethod((methodName+"Async"),listParam,nextType,
                                                                        IsStaticMethod = false,
                                                                        InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                                let listPayload = (toList event.Payload)

                                                                                                let exprDes = deserializeAsync buffers listPayload [message] role
                                                                                                Expr.Sequential(exprDes,exprState) )
                                   aType 
                                    |> addMethod myMethod
                                    |> addMethod myMethodAsync
                                    |> ignore                 
                
                    | _ -> failwith " Mistake !!!!!!" 

        |hd::tl -> let nextType = findProvidedType providedList fsmInstance.[hd].NextState
                   let c = nextType.GetConstructors().[0]
                   let exprState = Expr.NewObject(c, [])
                   let event = fsmInstance.[hd]
                   let fullName = event.Label
                   let message = serialize(fullName)
                   let role = event.Partner
                   let listTypes = 
                    match methodName with
                        |"send" -> toProvidedList event.Payload
                        |"receive" -> createProvidedParameters event
                        | _ -> []
                   let listParam = 
                    match methodName with
                        |"send" | "receive" -> List.append [ProvidedParameter("Label",mLabel.[fullName]);ProvidedParameter("Role",mRole.[role])] listTypes
                        | _  -> []
                   
                   match methodName with
                    |"send" -> let myMethod = ProvidedMethod(methodName,listParam,nextType,
                                                                IsStaticMethod = false,
                                                                InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                        let buf = serializeMessage fullName (toList event.Payload) buffers
                                                                                        //let buf = serialize fullName
                                                                                        let exprAction = <@@ Regarder.sendMessage "agent" (%%buf:byte[]) role @@>
                                                                                        //let exprAction = <@@ Regarder.sendMessage "agent" buf role @@> 
                                                                                        Expr.Sequential(exprAction,exprState) )
                               aType 
                                    |> addMethod myMethod
                                    |> ignore
                    |"receive" ->  let myMethod = ProvidedMethod(methodName,listParam,nextType,
                                                                    IsStaticMethod = false,
                                                                    InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                            let listPayload = (toList event.Payload)
                                                                                                  (*let mutable exp = []
                                                                                                  for elem in buffers do
                                                                                                    exp <- (Expr.Coerce(elem,typeof<obj>))::exp
                                                                                                  let exprTest = Expr.NewArray(typeof<obj>,exp)*)
                                                                                                  (*let exprAction = <@@ //Regarder.receiveMessage "agent" [message] role  listPayload
                                                                                                                       printf "HEY MATTHIEU"
                                                                                                                       Async.StartAsTask(Regarder.receiveMessage "agent" [message] role  listPayload)
                                                                                                                       //printf "NO WAYYYYYYYYY"
                                                                                                                       //deserializeTest listPayload// received //buffers //received listPayload |> ignore
                                                                                                                       (*received.ContinueWith(fun (listTask:Task<byte[] list>) -> 
                                                                                                                                                    deserializeTest buffers (listTask.Result) listPayload) *)
                                                                                                                                                    (*if listTask.IsCompleted then
                                                                                                                                                        listTask.Result |> List.iteri(fun i buf ->
                                                                                                                                                                                    deserializeTest (%%buffers.[i]) buf listPayload.[i]
                                                                                                                                                                               )
                                                                                                                                                    else
                                                                                                                                                        failwith "Heyyyyyyyyy"                
                                                                                                                                                  )*)
                                                                                                                                                  (*      deserializeTest      )
                                                                                                                       let truc = Async.AwaitIAsyncResult(received) |> ignore
                                                                                                                       
                                                                                                                       async{
                                                                                                                        let! result = Async.AwaitTask(received)*)
                                                                                                                       
                                                                                                                       //deserializeTest (%%exprTest:obj[]) received listPayload
                                                                                                                       
                                                                                                                       //let result = received //Async.RunSynchronously(received) 
                                                                                                                       //result |> List.iter (printfn "ALLEZ PUTINNNNNNNNN : %A")  
                                                                                                                       //received
                                                                                                                       @@> // We can a TimeOut if we wait to long*)
                                                                                                                       
                                                                                                  //let exprDeserialize = deserializeMessage buffers exprAction listPayload
                                                                                                  //let expr = Expr.Sequential(exprTest,exprAction)
                                                                                            let exprDes = deserialize buffers listPayload [message] role
                                                                                            Expr.Sequential(exprDes,exprState) )
                                   let myMethodAsync =  ProvidedMethod((methodName+"Async"),listParam,nextType,
                                                                        IsStaticMethod = false,
                                                                        InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                                let listPayload = (toList event.Payload)

                                                                                                let exprDes = deserializeAsync buffers listPayload [message] role
                                                                                                Expr.Sequential(exprDes,exprState) )
                                   aType 
                                    |> addMethod myMethod
                                    |> addMethod myMethodAsync
                                    |> ignore                 
                
                    | _ -> failwith " Mistake !!!!!!"                      
                   (*let myMethod = ProvidedMethod(methodName,listParam,nextType,
                                                        IsStaticMethod = false,
                                                        InvokeCode = fun args-> let buffers = args.Tail.Tail.Tail
                                                                                match methodName with
                                                                                    |"send" -> let buf = serializeMessage fullName (toList event.Payload) buffers
                                                                                               //let buf = serialize fullName
                                                                                               let exprAction = <@@ Regarder.sendMessage "agent" (%%buf:byte[]) role @@>
                                                                                               //let exprAction = <@@ Regarder.sendMessage "agent" buf role @@> 
                                                                                               Expr.Sequential(exprAction,exprState)
                                                                                    |"receive" -> let listPayload = (toList event.Payload)
                                                                                                  (*let mutable exp = []
                                                                                                  for elem in buffers do
                                                                                                    exp <- (Expr.Coerce(elem,typeof<obj>))::exp
                                                                                                  let exprTest = Expr.NewArray(typeof<obj>,exp)*)
                                                                                                  (*let exprAction = <@@ //Regarder.receiveMessage "agent" [message] role  listPayload
                                                                                                                       printf "HEY MATTHIEU"
                                                                                                                       Async.StartAsTask(Regarder.receiveMessage "agent" [message] role  listPayload)
                                                                                                                       //printf "NO WAYYYYYYYYY"
                                                                                                                       //deserializeTest listPayload// received //buffers //received listPayload |> ignore
                                                                                                                       (*received.ContinueWith(fun (listTask:Task<byte[] list>) -> 
                                                                                                                                                    deserializeTest buffers (listTask.Result) listPayload) *)
                                                                                                                                                    (*if listTask.IsCompleted then
                                                                                                                                                        listTask.Result |> List.iteri(fun i buf ->
                                                                                                                                                                                    deserializeTest (%%buffers.[i]) buf listPayload.[i]
                                                                                                                                                                               )
                                                                                                                                                    else
                                                                                                                                                        failwith "Heyyyyyyyyy"                
                                                                                                                                                  )*)
                                                                                                                                                  (*      deserializeTest      )
                                                                                                                       let truc = Async.AwaitIAsyncResult(received) |> ignore
                                                                                                                       
                                                                                                                       async{
                                                                                                                        let! result = Async.AwaitTask(received)*)
                                                                                                                       
                                                                                                                       //deserializeTest (%%exprTest:obj[]) received listPayload
                                                                                                                       
                                                                                                                       //let result = received //Async.RunSynchronously(received) 
                                                                                                                       //result |> List.iter (printfn "ALLEZ PUTINNNNNNNNN : %A")  
                                                                                                                       //received
                                                                                                                       @@> // We can a TimeOut if we wait to long*)
                                                                                                                       
                                                                                                  //let exprDeserialize = deserializeMessage buffers exprAction listPayload
                                                                                                  //let expr = Expr.Sequential(exprTest,exprAction)
                                                                                                  let exprDes = deserializeTest buffers listPayload [message] role
                                                                                                  Expr.Sequential(exprDes,exprState)
                                                                                    |_ -> <@@ printfn "Error" @@> )
       
                   aType 
                        |> addMethod myMethod
                        |> ignore                *)
                   goingThrough methodName providedList aType tl mLabel mRole fsmInstance 


let internal getAllChoiceLabels (indexList : int list) (fsmInstance:ScribbleProtocole.Root []) =
    let rec aux list acc =
        match list with
            |[] -> acc
            |hd::tl -> let labelBytes = fsmInstance.[hd].Label |> serialize
                       aux tl (labelBytes::acc) 
    in aux indexList []


let rec addProperties (providedListStatic:ProvidedTypeDefinition list) (providedList:ProvidedTypeDefinition list) (stateList: int list) 
                      (mLabel:Map<string,System.Type>) (mRole:Map<string,ProvidedTypeDefinition>) (fsmInstance:ScribbleProtocole.Root []) =
    let currentState = stateList.Head
    let indexOfState = findCurrentIndex currentState fsmInstance
    let indexList = findSameCurrent currentState fsmInstance 
    let mutable choiceIter = 1
    let mutable methodName = "finish"
    if indexOfState <> -1 then
        methodName <- fsmInstance.[indexOfState].Type
    match providedList with
        |[] -> ()
        |[aType] -> match methodName with
                        |"send" ->  goingThrough methodName providedListStatic aType indexList mLabel mRole fsmInstance
                        |"receive" -> goingThrough methodName providedListStatic aType indexList mLabel mRole fsmInstance 
                        |"choice" -> let labelType = mLabel.["Choice" + string currentState]
                                     let c = labelType.GetConstructors().[0]
                                     let exprLabel = Expr.NewObject(c,[])
                                     let listExpectedMessages = getAllChoiceLabels indexList fsmInstance
                                     let event = fsmInstance.[indexOfState]
                                     let role = event.Partner
                                     let myMethod = ProvidedMethod("branch",[],labelType,
                                                                                    IsStaticMethod = false,
                                                                                    InvokeCode = fun args-> let listPayload = (toList event.Payload)
                                                                                                            let exprAction = <@@ Regarder.receiveMessage "agent" listExpectedMessages role listPayload @@>
                                                                                                            let label = mLabel.["GoodMornin()"] // Change to be the correct one
                                                                                                            let ctor = label.GetConstructors().[0]
                                                                                                            let exprReturn = Expr.NewObject(ctor,[])
                                                                                                            Expr.Sequential(exprAction,exprReturn) )

                                     aType |> addMethod myMethod |> ignore
                        |"finish" ->  goingThrough methodName providedListStatic aType indexList mLabel mRole fsmInstance 
                        | _ -> printfn "Not correct"
                    aType |> addProperty (<@@ "essaye Bateau" @@> |> createPropertyType "MyProperty" typeof<string> ) |> ignore
        |hd::tl ->  match methodName with
                        |"send" -> goingThrough methodName providedListStatic hd indexList mLabel mRole fsmInstance 
                        |"receive" -> goingThrough methodName providedListStatic hd indexList mLabel mRole fsmInstance 
                        |"choice" -> let labelType = mLabel.["Choice"+ string currentState]
                                     let c = labelType.GetConstructors().[0]
                                     let exprLabel = Expr.NewObject(c,[])
                                     let listExpectedMessages = getAllChoiceLabels indexList fsmInstance
                                     let event = fsmInstance.[indexOfState]
                                     let role = event.Partner
                                     let myMethod = ProvidedMethod("branch",[],labelType,
                                                                                    IsStaticMethod = false,
                                                                                    InvokeCode = fun args-> let listPayload = (toList event.Payload)
                                                                                                            let exprAction = <@@ Regarder.receiveMessage "agent" listExpectedMessages role listPayload  @@> // TO CHANGE !!!!!!!!!!!!!!!!
                                                                                                            let label = mLabel.["GoodMornin()"]
                                                                                                            let ctor = label.GetConstructors().[0]
                                                                                                            let exprReturn = Expr.NewObject(ctor,[])
                                                                                                            Expr.Sequential(exprAction,exprReturn) )
                                     hd |> addMethod myMethod |> ignore
                        |"finish" -> goingThrough methodName providedListStatic hd indexList mLabel mRole fsmInstance 
                        | _ -> printfn "Not correct"    
                    hd |> addProperty (<@@ "Test" @@> |> createPropertyType "MyProperty" typeof<string> ) |> ignore
                    addProperties providedListStatic tl (stateList.Tail) mLabel mRole fsmInstance 


let internal contains (aSet:Set<'a>) x = 
    Set.exists ((=) x) aSet

let internal stateSet (fsmInstance:ScribbleProtocole.Root []) =
    let firstState = fsmInstance.[0].CurrentState
    let mutable setSeen = Set.empty
    let mutable counter = 0
    for event in fsmInstance do
        if (not(contains setSeen event.CurrentState) || not(contains setSeen event.NextState)) then
            setSeen <- setSeen.Add(event.CurrentState)
            setSeen <- setSeen.Add(event.NextState)
    (setSeen.Count,setSeen,firstState)

let internal makeRoleList (fsmInstance:ScribbleProtocole.Root []) =
    let mutable setSeen = Set.empty
    [yield fsmInstance.[0].LocalRole
     for event in fsmInstance do
        if not(setSeen |> contains <| event.Partner) then
            setSeen <- setSeen.Add(event.Partner)
            yield event.Partner]