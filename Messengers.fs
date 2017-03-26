namespace SlackToTelegram

type SlackChannel = { channel_id: string; name: string }

module Messengers =
    open System.Collections.Generic
    open System.IO
    open System.Net
    open System.Net.Http
    open System.Reactive.Linq
    open Newtonsoft.Json
    open Telegram.Bot
    open SlackToTelegram.Storage

    let getNewBotMessages token =
        let offset = getOffset () |> Option.defaultValue 0
        let msgs = TelegramBotClient(token).GetUpdatesAsync(offset).Result |> Array.toList
        msgs |> List.map (fun x -> x.Id) |> List.sortDescending |> List.tryHead
             |> (fun x -> match x with | Some offset -> setOffset (offset + 1) | _ -> ())
        msgs |> List.map (fun x -> { text = x.Message.Text; user = string x.Message.From.Id })

    let private download (url: string) = HttpClient().GetStringAsync(url).Result |> StringReader |> JsonTextReader

    type UserResponse = { user_id: string; name: string }
    type RelatedResponse = { users: Dictionary<string, UserResponse> }
    type MessagesResponse = { messages: SlackMessage[]; related: RelatedResponse }
    let getSlackMessages (channelId: string) =
        "http://api.slackarchive.io/v1/messages?size=5&channel=" + channelId
        |> download |> JsonSerializer().Deserialize<MessagesResponse> 
        |> (fun r -> r.messages 
                     |> Array.toList 
                     |> List.map (fun x -> { user = r.related.users.[x.user].name; text = x.text}))

    type ChannelsResponse = { channels: SlackChannel[] }
    let getSlackChannels () =
        "http://api.slackarchive.io/v1/channels?team_id=T09229ZC6" 
        |> download |> JsonSerializer().Deserialize<ChannelsResponse> 
        |> (fun x -> x.channels |> Array.toList)

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