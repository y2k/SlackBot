open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module Bot = SlackToTelegram.Messengers
module DB = SlackToTelegram.Storage

module Domain = 
    let makeMessageForTopChannels channels = 
        channels
        |> List.filter (fun x -> x.num_members >= 100)
        |> List.sortByDescending (fun x -> x.num_members)
        |> List.map 
               (fun x -> 
               sprintf "• <b>%O</b> (%O) - %O" x.name x.num_members 
                   x.purpose.value)
        |> List.reduce (fun a x -> a + "\n" + x)
        |> (+) "<b>Список доступных каналов:</b> \n"
    
    let makeMessageFromUserChannels = 
        function 
        | [] -> 
            "У вас нет подписок. Добавьте командой: <code>add [канал]</code>"
        | channels -> 
            channels
            |> List.sortBy (fun x -> x.id)
            |> List.map (fun x -> "<code>" + x.id + "</code>")
            |> List.reduce (fun a x -> a + ", " + x)
            |> (+) "Каналы на которые вы подписаны: "
    
    let parseCommand (command : string) = 
        match command.Split(' ') |> Seq.toList with
        | "top" :: _ -> Top
        | "ls" :: _ -> Ls
        | "add" :: x :: _ -> Add x
        | "rm" :: x :: _ -> Rm x
        | _ -> Unknow
    
    let handleMessage (user : User) (message : string) = 
        match parseCommand message with
        | Top -> Bot.getSlackChannels() |> Async.map makeMessageForTopChannels
        | Ls -> 
            DB.queryUserChannels user |> Async.map makeMessageFromUserChannels
        | Add x -> 
            DB.add user x 
            |> Async.ignore 
                   ("Подписка на <code>" + x + "</code> выполнена успешно")
        | Rm x -> 
            DB.remove user x
            "Отписка от <code>" + x + "</code> выполнена успешно" 
            |> async.Return
        | Unknow -> 
            "<b>Команды бота:</b>
    • <b>top</b> - топ каналов kotlinlang.slack.com на которые можно подписаться
    • <b>ls</b> - список каналов kotlinlang.slack.com на которые вы подписаны
    • <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
    • <b>rm</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)" 
            |> async.Return
    
    let filterChannels (dbChannels, channels) = 
        channels |> List.where (fun x -> dbChannels |> List.contains x.name)
    
    let makeUpdateMessage msgs (chName : string) = 
        msgs
        |> List.fold 
               (fun a x -> 
               "(<b>" + x.user + "</b>) " 
               + WebUtility.HtmlEncode(WebUtility.HtmlDecode(x.text)) + "\n\n" 
               + a) ""
        |> sprintf "<b>| Новые сообщения в канале %s |</b>\n\n%s" 
               (chName.ToUpper())

[<EntryPoint>]
let main argv = 
    let token = argv.[0]
    Bot.repl' token Domain.handleMessage
    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
    |> Observable.ignore
    |> Observable.flatMapTask Bot.getSlackChannels
    |> Observable.map (fun slackChannels -> DB.getAllChannels(), slackChannels)
    |> Observable.map Domain.filterChannels
    |> Observable.flatMap (fun x -> x.ToObservable())
    |> Observable.map 
           (fun ch -> 
           let channelOffset = 
               DB.getOffsetWith ch.name |> Option.defaultValue "0"
           let newMessages = 
               Bot.getSlackMessages ch.channel_id 
               |> List.takeWhile (fun x -> x.ts <> channelOffset)
           newMessages
           |> List.tryPick (fun x -> Some x.ts)
           |> Option.map (fun x -> DB.setOffsetWith ch.name x)
           |> ignore
           DB.getUsersForChannel ch.name
           |> List.map (fun tid -> (tid, ch.name, newMessages))
           |> List.filter (fun (_, _, msgs) -> not msgs.IsEmpty)
           |> List.map 
                  (fun (tid, chName, msgs) -> 
                  (Domain.makeUpdateMessage msgs chName, tid)))
    |> flatMap (fun x -> x.ToObservable())
    |> Observable.map 
           (fun (message, tid) -> 
           (tid, message |> Bot.sendToTelegramSingle token tid Styled))
    |> Observable.map (fun (tid, x) -> 
           match x with
           | Bot.BotBlockedResponse -> DB.removeChannelsForUser tid
           | _ -> ())
    |> (fun o -> o.Subscribe(DefaultErrorHandler()))
    |> ignore
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0