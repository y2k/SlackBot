open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module bot = SlackToTelegram.Messengers
module db  = SlackToTelegram.Storage

let parseMessage (user: User) (message: string) = 
    match message.Split(' ') |> Seq.toList with
    | "list"::_      -> match db.query user with
                        | [] -> "У вас нет подписок. Добавьте командой: <code>add [канал]</code>"
                        | channels -> channels |> List.sortBy (fun x -> x.id)
                                               |> List.map (fun x -> "<code>" + x.id + "</code>")
                                               |> List.reduce (fun a x -> a + ", " + x)
                                               |> (+) "Каналы на которые вы подписаны: "
    | "add"::x::_    -> db.add user x; "Подписка на <code>" + x + "</code> выполнена успешно"
    | "remove"::x::_ -> db.remove user x; "Отписка от <code>" + x + "</code> выполнена успешно"
    | _              -> "<b>Команды бота:</b>
• <b>list</b> - список каналов kotlinlang.slack.com на которые вы подписаны
• <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
• <b>remove</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)"

[<EntryPoint>]
let main argv =
    let token = argv.[0]

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(5.))
        |> Observable.map (fun _ -> bot.getNewBotMessages token)
        |> flatMap (fun x -> x.ToObservable())
        |> Observable.map (fun x -> (x.user, x.text |> parseMessage x.user))
        |> Observable.map (fun (user, response) ->
            printfn "Message = %O" response
            bot.sendToTelegramSingle token user Styled response)
        |> (fun o -> o.Subscribe(DefaultErrorHandler()))
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
        |> Observable.map (fun (tid, chName, msgs) -> 
            msgs |> List.fold (fun a x -> "(<b>" + x.user + "</b>) " + WebUtility.HtmlEncode(WebUtility.HtmlDecode(x.text)) + "\n\n" + a) ""
                 |> ((+) ("<b>=== Новые сообщения в канале " + chName.ToUpper() + " ===</b>\n\n"))
                 |> bot.sendToTelegramSingle token tid Styled)
        |> (fun o -> o.Subscribe(DefaultErrorHandler()))
        |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0