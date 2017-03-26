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
    | "list"::_      -> db.query user |> List.map (fun x -> "<code>" + x.id + "</code>")
                                      |> List.reduce (fun a x -> a + ", " + x)
                                      |> (+) "Каналы на которые вы подписанны: "
    | "add"::x::_    -> db.add user x; "Подписка на <code>" + x + "</code> выполнена успешно"
    | "remove"::x::_ -> db.remove user x; "Отписка от <code>" + x + "</code> выполнена успешно"
    | _              -> "<b>Команды бота:</b>
• <b>list</b> - список каналов kotlinlang.slack.com на которые вы подписанны
• <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
• <b>remove</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)"

[<EntryPoint>]
let main argv =
    let token = argv.[0]

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(5.))
        |> Observable.map (fun _ -> bot.getNewBotMessages token)
        |> flatMap (fun x -> x.ToObservable())
        |> Observable.map (fun x -> (x.user, x.text |> parseMessage x.user))
        |> Observable.subscribe (fun (user, response) ->
            printfn "Message = %O" response
            bot.sendToTelegramSingle token user Styled response)
        |> ignore

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
        |> Observable.map (fun _ -> 
            let dbChannels = db.getAllChannels ()
            bot.getSlackChannels () |> List.where (fun x -> dbChannels |> List.contains x.name))
        |> flatMap (fun x -> x.ToObservable())
        |> flatMap (fun ch -> 
            let channelOffset = db.getOffsetWith ch.name |> Option.defaultValue "0"
            let newMessages = bot.getSlackMessages ch.channel_id
                              |> List.takeWhile (fun x -> x.ts <> channelOffset)
            newMessages |> List.tryPick (fun x -> Some x.ts) 
                        |> Option.map (fun x -> db.setOffsetWith ch.name x) |> ignore
            db.getUsersForChannel ch.name 
                |> List.map (fun tid -> (tid, ch.name, newMessages))
                |> (fun x -> x.ToObservable()))
        |> Observable.filter (fun (_, _, msgs) -> not msgs.IsEmpty)
        |> Observable.subscribe (fun (tid, chName, msgs) -> 
            msgs |> List.fold (fun a x -> "(" + x.user + ") " + WebUtility.HtmlDecode(x.text) + "\n\n" + a) ""
                 |> ((+) ("=== New messages from " + chName.ToUpper() + " ===\n\n"))
                 |> bot.sendToTelegramSingle token tid Plane)
        |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0