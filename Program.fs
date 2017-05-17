open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module bot = SlackToTelegram.Messengers
module db  = SlackToTelegram.Storage
module o   = Observable

module Domain =
    let handleMessage (user: User) (message: string) = 
        match message.Split(' ') |> Seq.toList with
        | "top"::_    -> bot.getSlackChannels () 
                        |> List.filter (fun x -> x.num_members >= 100)
                        |> List.sortByDescending (fun x -> x.num_members)
                        |> List.map (fun x -> sprintf "• <b>%O</b> (%O) - %O" x.name x.num_members x.purpose.value) 
                        |> List.reduce (fun a x -> a + "\n" + x)
                        |> (+) "<b>Список доступных каналов:</b> \n"
        | "ls"::_     -> match db.query user with
                        | [] -> "У вас нет подписок. Добавьте командой: <code>add [канал]</code>"
                        | channels -> channels |> List.sortBy (fun x -> x.id)
                                                |> List.map (fun x -> "<code>" + x.id + "</code>")
                                                |> List.reduce (fun a x -> a + ", " + x)
                                                |> (+) "Каналы на которые вы подписаны: "
        | "add"::x::_ -> db.add user x; "Подписка на <code>" + x + "</code> выполнена успешно"
        | "rm"::x::_  -> db.remove user x; "Отписка от <code>" + x + "</code> выполнена успешно"
        | _           -> "<b>Команды бота:</b>
    • <b>top</b> - топ каналов kotlinlang.slack.com на которые можно подписаться
    • <b>ls</b> - список каналов kotlinlang.slack.com на которые вы подписаны
    • <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
    • <b>rm</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)"

    let filterChannels (dbChannels, channels) =
        channels |> List.where (fun x -> dbChannels |> List.contains x.name)

[<EntryPoint>]
let main argv =
    let token = argv.[0]

    bot.getNewBotMessages token
        |> o.map (fun x -> (x.user, x.text |> Domain.handleMessage x.user))
        |> o.map (fun (user, response) -> bot.sendToTelegramSingle token user Styled response)
        |> (fun o -> o.Subscribe(DefaultErrorHandler())) |> ignore

    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
        |> o.map (fun _ -> (db.getAllChannels(), bot.getSlackChannels()))
        |> o.map Domain.filterChannels
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
        |> o.filter (fun (_, _, msgs) -> not msgs.IsEmpty)
        |> o.map (fun (tid, chName, msgs) -> 
            msgs |> List.fold (fun a x -> "(<b>" + x.user + "</b>) " + WebUtility.HtmlEncode(WebUtility.HtmlDecode(x.text)) + "\n\n" + a) ""
                 |> (+) ("<b>| Новые сообщения в канале " + chName.ToUpper() + " |</b>\n\n")
                 |> bot.sendToTelegramSingle token tid Styled)
        |> (fun o -> o.Subscribe(DefaultErrorHandler())) |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0