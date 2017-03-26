open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module bot = SlackToTelegram.Messengers
module db  = SlackToTelegram.Storage

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

*)

let parseMessage (user: User) (message: string) = 
    match message.Split(' ') |> Seq.toList with
    | "list"::_      -> db.query user |> List.map (fun x -> x.id) 
                                      |> List.reduce (fun a x -> a + ", " + x) 
                                      |> (+) "Your channels: "
    | "add"::x::_    -> db.add user x; "completed"
    | "remove"::x::_ -> db.remove user x; "completed"
    | _              -> "Commands: list, add <channel>, remove <channel>"

[<EntryPoint>]
let main argv =
    let token = argv.[0]

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(5.))
        |> Observable.map (fun _ -> bot.getNewBotMessages token)
        |> flatMap (fun x -> x.ToObservable())
        |> Observable.map (fun x -> (x.user, x.text |> parseMessage x.user))
        |> Observable.subscribe (fun (user, response) ->
            bot.sendToTelegramSingle token user response
            printfn "Message = %O" response)
        |> ignore

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
        |> Observable.map (fun _ -> 
            let dbChannels = db.getAllChannels ()
            bot.getSlackChannels () |> List.where (fun x -> dbChannels |> List.contains x.name))
        |> flatMap (fun x -> x.ToObservable())
        |> flatMap (fun ch -> 
            let msgs = bot.getSlackMessages ch.channel_id
                       |> List.takeWhile (fun x -> false)
            db.getUsersForChannel ch.name 
                |> List.map (fun tid -> (tid, ch.name, msgs))
                |> (fun x -> x.ToObservable()))
        |> Observable.filter (fun (_, _, msgs) -> not msgs.IsEmpty)
        |> Observable.subscribe (fun (tid, chName, msgs) -> 
            msgs |> List.fold (fun a x -> "(" + x.user + ") " + WebUtility.HtmlDecode(x.text) + "\n\n" + a) ""
                 |> ((+) ("=== New messages from " + chName.ToUpper() + " ===\n\n"))
                 |> bot.sendToTelegramSingle token tid)
        |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0