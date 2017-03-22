namespace SlackToTelegram
module Messengers =
    open System.IO
    open System.Net
    open System.Net.Http
    open System.Reactive.Linq
    open Telegram.Bot
    open Newtonsoft.Json

    type Message = { text: string; user: string; ts: double }
    type Response = { messages: Message[] }

    let private download (url: string) = HttpClient().GetStringAsync(url).Result |> StringReader |> JsonTextReader
    let getSlackMessages() =
        "http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C09222272&offset=0"
        |> download |> JsonSerializer().Deserialize<Response> 

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