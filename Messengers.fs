namespace SlackToTelegram
module Messengers =
    open System.IO
    open System.Net
    open System.Net.Http
    open System.Reactive.Linq
    open Telegram.Bot
    open Newtonsoft.Json
    open SlackToTelegram.Storage

    let getNewBotMessages token =
        let offset = getOffset () |> Option.defaultValue 0
        printfn "offfset = %O" offset
        let msgs = TelegramBotClient(token).GetUpdatesAsync(offset).Result |> Array.toList
        msgs |> List.map (fun x -> x.Id) |> List.sortDescending |> List.tryHead
             |> (fun x -> match x with | Some offset -> setOffset (offset + 1) | _ -> ())
        msgs |> List.map (fun x -> { text = x.Message.Text; user = string x.Message.From.Id })

    let private download (url: string) = HttpClient().GetStringAsync(url).Result |> StringReader |> JsonTextReader
    let getSlackMessages() =
        "http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C09222272&offset=0"
        |> download |> JsonSerializer().Deserialize<Response> 

    let sendToTelegramSingle (token: Token) (user: User) message =
        let bot = TelegramBotClient(token)
        bot.SendTextMessageAsync(user, message).Result |> ignore

    let sendToTelegram token messages =
        let bot = TelegramBotClient(token)
        let chats =
            bot.GetUpdatesAsync().Result 
            |> Array.toList
            |> List.rev
            |> List.map (fun x -> x.Message) 
            |> List.takeWhile (fun x -> isNull x.LeftChatMember)
            |> List.map (fun x -> string x.Chat.Id) 
            |> List.distinct
        let ms = messages |> List.filter ((<>) "") |> List.map WebUtility.HtmlDecode
        for chat in chats do
            for m in ms do
                bot.SendTextMessageAsync(chat, m).Result |> ignore
        ()