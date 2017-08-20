open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module Bot = SlackToTelegram.Messengers
module DB = SlackToTelegram.Storage
module I = SlackToTelegram.Infrastructure

module Message = 
    let subscribe x = "Подписка на <code>" + x + "</code> выполнена успешно"
    let unsubscribe x = "Отписка от <code>" + x + "</code> выполнена успешно"
    
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
    
    let help = "<b>Команды бота:</b>
• <b>top</b> - топ каналов kotlinlang.slack.com на которые можно подписаться
• <b>ls</b> - список каналов kotlinlang.slack.com на которые вы подписаны
• <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
• <b>rm</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)"

module Domain = 
    let parseCommand (command : string) = 
        match command.Split(' ') |> Seq.toList with
        | "top" :: _ -> Top
        | "ls" :: _ -> Ls
        | "add" :: x :: _ -> Add x
        | "rm" :: x :: _ -> Rm x
        | _ -> Unknow
    
    let filterChannelsWithIds (dbChannels, channels) = 
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
    
    let private limitMessagesToOffset offset slackMessages = 
        let channelOffset = offset |> Option.defaultValue "0"
        slackMessages |> List.takeWhile (fun x -> x.ts <> channelOffset)
    
    let private makeTelegramMessageAboutUpdates (usersForChannel : User list) ch 
        newMessages = 
        match newMessages with
        | [] -> []
        | _ -> 
            usersForChannel 
            |> List.map (fun tid -> makeUpdateMessage newMessages ch.name, tid)
    
    let toUpdateNotificationWithOffset (ch, slackMessages, offset, 
                                        usersForChannel) = 
        let newMessages = limitMessagesToOffset offset slackMessages
        let newOffset = newMessages |> List.tryPick (fun x -> Some x.ts)
        let msgs = 
            makeTelegramMessageAboutUpdates usersForChannel ch newMessages
        newOffset, ch, msgs

let handleTelegramCommand (user : User) (message : string) = 
    match Domain.parseCommand message with
    | Top -> 
        Bot.getSlackChannels() |> Async.map Message.makeMessageForTopChannels
    | Ls -> 
        DB.queryUserChannels user 
        |> Async.map Message.makeMessageFromUserChannels
    | Add x -> DB.add user x |> Async.ignore (Message.subscribe x)
    | Rm x -> DB.remove user x |> Async.ignore (Message.unsubscribe x)
    | Unknow -> Message.help |> async.Return

let loadChannelUpdates ch = async { let! slackMessages = Bot.getSlackMessages 
                                                             ch.channel_id
                                    let! offset = DB.getOffsetWith ch.name
                                    let! users = DB.getUsersForChannel ch.name
                                    return ch, slackMessages, offset, users }

let notifyUpdatesAndSaveOffset token (newOffset, ch, msgs) = 
    async { 
        do! match newOffset with
            | Some x -> DB.setOffsetWith ch.name x
            | None -> async.Zero()
        for (message, tid) in msgs do
            let! r = message |> Bot.sendToTelegramSingle token tid Styled
            if r = Bot.BotBlockedResponse then do! DB.removeChannelsForUser tid
    }

let checkUpdates token = 
    Bot.getSlackChannels()
    |> Async.combine (fun _ -> DB.getAllChannels())
    |> Async.map Domain.filterChannelsWithIds
    |> Async.bind (fun channels -> 
           async { 
               for x in channels do
                   do! loadChannelUpdates x
                       |> Async.map Domain.toUpdateNotificationWithOffset
                       |> Async.bind (notifyUpdatesAndSaveOffset token)
           })

[<EntryPoint>]
let main argv = 
    printfn "listening for slack updates..."
    Bot.repl argv.[0] handleTelegramCommand
    I.loop (fun _ -> checkUpdates argv.[0])
    0