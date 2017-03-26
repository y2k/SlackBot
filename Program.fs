open System
open System.Net
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
    | List     -> query user |> List.map (fun x -> x.id) 
                             |> List.reduce (fun a x -> a + ", " + x) 
                             |> (+) "Your channels: "
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

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
        |> Observable.map (fun _ -> 
            let dbChannels = getAllChannels ()
            getSlackChannels () |> List.where (fun x -> dbChannels |> List.contains x.name))
        |> flatMap (fun x -> x.ToObservable())
        |> flatMap (fun ch -> 
            let msgs = getSlackMessages ch.channel_id
            getUsersForChannel ch.name 
                |> List.map (fun tid -> (tid, ch.name, msgs))
                |> (fun x -> x.ToObservable()))
        |> Observable.filter (fun (_, _, msgs) -> not msgs.IsEmpty)
        |> Observable.subscribe (fun (tid, chName, msgs) -> 
            msgs |> List.fold (fun a x -> "(" + x.user + ") " + WebUtility.HtmlDecode(x.text) + "\n" + a) ""
                 |> ((+) ("=== New pots from " + chName.ToUpper() + " ===\n\n"))
                 |> sendToTelegramSingle token tid)
        |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0