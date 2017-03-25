open System
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Messengers
open SlackToTelegram.Storage
open SlackToTelegram.Utils

(*
http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C2X2LMYQ2&offset=0
http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C09222272&offset=0

#r "../../.nuget/packages/system.reactive.windows.threading/3.1.1/lib/net45/System.Reactive.Windows.Threading.dll"
#r "../../.nuget/packages/system.reactive.platformservices/3.1.1/lib/net45/System.Reactive.PlatformServices.dll"
#r "../../.nuget/packages/system.reactive.interfaces/3.1.1/lib/net45/System.Reactive.Interfaces.dll"
#r "../../.nuget/packages/system.reactive.core/3.1.1/lib/net45/System.Reactive.Core.dll"
#r "../../.nuget/packages/system.reactive.linq/3.1.1/lib/net45/System.Reactive.Linq.dll"

#r "../../.nuget/packages/telegram.bot/10.4.0/lib/net45/Telegram.Bot.dll"
#r "../../.nuget/packages/newtonsoft.json/9.0.1/lib/net45/Newtonsoft.Json.dll"

System.Reactive.Linq.Observable.Timer(System.TimeSpan.Zero, System.TimeSpan.FromSeconds(30.))
|> Observable.subscribe (fun xs -> printfn "hello world")
|> ignore
*)

let nowUtc () = DateTime.UtcNow.Subtract(DateTime(1970, 1, 1)).TotalSeconds

let parseMessage (message: string) = 
    match message.Split(' ') |> Seq.toList with
    | "list"::_      -> List
    | "add"::x::_    -> Add x
    | "remove"::x::_ -> Remove x
    | _              -> Help

let executeCommand (user: User) = function
    | List     -> query user |> List.fold (fun a x -> x.id + ", " + a) "List: "
    | Add x    -> add user x; "completed"
    | Remove x -> remove user x; "completed"
    | Help     -> "Commands: list, add <channel>, remove <channel>"

[<EntryPoint>]
let main argv =
    let token = argv.[0]

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(5.))
        |> Observable.map (fun _ -> getNewBotMessages token)
        |> flatMap (fun x -> x.ToObservable())
        |> Observable.map (fun x -> (x.user, x.text |> parseMessage |> executeCommand x.user))
        |> Observable.subscribe (fun (user, response) ->
            sendToTelegramSingle token user response
            printfn "Message = %O" response)
        |> ignore

    // Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
    //     |> Observable.map (fun _ -> 
    //         getSlackMessages () 
    //         |> (fun x -> (Array.toList x.messages, x.messages.[0].ts)))
    //     |> Observable.scan (fun state (xs, stamp) -> 
    //         match state with
    //         | ([], 0.) -> (xs |> List.take 1, stamp)
    //         | (_, prevStamp) -> (xs |> List.filter (fun x -> x.ts > prevStamp), stamp)
    //         ) ([], 0.)
    //     |> Observable.map (fun (xs, _) -> xs |> Seq.toList |> List.rev |> List.map (fun x -> x.text))
    //     |> Observable.subscribe (fun xs -> 
    //         printfn "=== === === === === === (%O)" DateTime.Now
    //         for x in xs do printfn "New message = %O" x
    //         sendToTelegram token xs)
    //     |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0