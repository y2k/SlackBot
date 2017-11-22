module Message

open System.Net
open SlackToTelegram

module DB = SlackToTelegram.Storage
module I = SlackToTelegram.Infrastructure

let parseCommand command = 
    match String.split command with
    | "top" :: _ -> Top
    | "ls" :: _ -> Ls
    | "add" :: x :: _ -> Add x
    | "rm" :: x :: _ -> Rm x
    | _ -> Unknow

let subscribe channel = 
    function 
    | Some _ -> "Подписка на <code>" + channel + "</code> выполнена успешно"
    | None -> 
        "Подписка на <code>" + channel 
        + "</code> не удалась. Неподдерживаемый тип подписок."

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

let makeMessageFromUserChannels (xs: Channel list) = 
    match xs with
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
• <b>add</b> [URL] - подписаться на обновления открытого Gitter чата (пример: <code>add https://gitter.im/fable-compiler/Fable</code>)
• <b>rm</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)

<b>Исходный код</b>: https://github.com/y2k/SlackBot"

let makeUpdateMessage msgs (chName : string) = 
    msgs
    |> List.fold 
           (fun a x -> 
           "(<b>" + x.user + "</b>) " 
           + WebUtility.HtmlEncode(WebUtility.HtmlDecode(x.text)) + "\n\n" 
           + a) ""
    |> sprintf "<pre>Новые сообщения в канале %s</pre>\n\n%s"  (chName.ToUpper())

// let makeUpdateMessage' msgs (chName : string) = 
//     match msgs with
//     | [] -> None
//     | _ ->
//         msgs
//         |> List.fold 
//                (fun a x -> 
//                "(<b>" + x.user + "</b>) " 
//                + WebUtility.HtmlEncode(WebUtility.HtmlDecode(x.text)) + "\n\n" 
//                + a) ""
//         |> sprintf "<b>| Новые сообщения в канале %s |</b>\n\n%s"  (chName.ToUpper())
//         |> Some