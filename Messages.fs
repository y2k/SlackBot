module Message

open System.Net
open SlackToTelegram

let parseCommand command = 
    match String.split command with
    | "top" :: _      -> Top
    | "ls" :: _       -> Ls
    | "add" :: x :: _ -> Add x
    | "rm" :: x :: _  -> Rm x
    | _               -> Unknow

let subscribe channel = function 
    | Some _ -> sprintf "Подписка на <code>%s</code> выполнена успешно" channel
    | None   -> sprintf "Подписка на <code>%s</code> не удалась. Неподдерживаемый тип подписок." channel

let unsubscribe = sprintf "Отписка от <code>%s</code> выполнена успешно"

let makeMessageForTopChannels channels = 
    channels
    |> List.filter (fun x -> x.num_members >= 150)
    |> List.sortByDescending (fun x -> x.num_members)
    |> List.map (fun x -> sprintf "• <code>%O</code> (%O)." x.name x.num_members)
    |> List.fold (sprintf "%s\n%s") "<b>Список доступных каналов:</b>"

let makeMessageFromUserChannels (xs: Channel list) = 
    match xs with
    | [] -> 
        "У вас нет подписок. Добавьте командой: <code>add [канал]</code>"
    | channels -> 
        channels
        |> List.sortBy (fun x -> x.id)
        |> List.map (fun x -> "• <code>" + x.id + "</code>")
        |> List.fold (sprintf "%s\n%s") "Каналы на которые вы подписаны:"

let help = "<b>Команды бота:</b>
• <b>top</b> - топ каналов kotlinlang.slack.com на которые можно подписаться
• <b>ls</b> - список каналов kotlinlang.slack.com на которые вы подписаны
• <b>add</b> [канал] - подписаться на обновления канала (пример: <code>add russian</code>)
• <b>add</b> [URL] - подписаться на обновления открытого Gitter чата (пример: <code>add https://gitter.im/fable-compiler/Fable</code>)
• <b>rm</b> [канал] - отписаться от канал (пример: <code>remove russian</code>)

<b>Исходный код</b>: https://github.com/y2k/SlackBot"

let makeUpdateMessage msgs (chName : string) = 
    msgs
    |> List.truncate 10
    |> List.map (fun x -> sprintf "(<b>%s</b>) %s" 
                              x.user 
                              (x.text |> WebUtility.HtmlDecode |> WebUtility.HtmlEncode))
    |> List.fold (sprintf "%s\n\n%s") ""
    |> sprintf "<pre>Новые сообщения в канале %s</pre>%s"  (chName.ToUpper ())