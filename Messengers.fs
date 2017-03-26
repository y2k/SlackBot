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
    
    module db = SlackToTelegram.Storage

    let getNewBotMessages token =
        let offset = db.getOffsetWith db.TelegramId |> Option.defaultValue "0"
        let msgs = TelegramBotClient(token).GetUpdatesAsync(int offset).Result |> Array.toList
        msgs |> List.map (fun x -> x.Id) |> List.sortDescending |> List.tryHead
             |> (fun x -> match x with 
                          | Some offset -> db.setOffsetWith db.TelegramId (string (offset + 1)) 
                          | _ -> ())
        msgs |> List.map (fun x -> { text = x.Message.Text; user = string x.Message.From.Id; ts = "" })

    let private download (url: string) = HttpClient().GetStringAsync(url).Result |> StringReader |> JsonTextReader

    type UserResponse = { user_id: string; name: string; ts: double }
    type RelatedResponse = { users: Dictionary<string, UserResponse> }
    type MessagesResponse = { messages: SlackMessage[]; related: RelatedResponse }
    
    let getSlackMessages (channelId: string) =
        "http://api.slackarchive.io/v1/messages?size=5&channel=" + channelId
        |> download |> JsonSerializer().Deserialize<MessagesResponse> 
        |> (fun r -> r.messages
                     |> Array.toList 
                     |> List.map (fun x -> { x with user = r.related.users.[x.user].name }))

    type ChannelsResponse = { channels: SlackChannel[] }
    let getSlackChannels () =
        "http://api.slackarchive.io/v1/channels?team_id=T09229ZC6" 
        |> download |> JsonSerializer().Deserialize<ChannelsResponse> 
        |> (fun x -> x.channels |> Array.toList)

    let sendToTelegramSingle (token: Token) (user: User) message =
        let bot = TelegramBotClient(token)
        bot.SendTextMessageAsync(user, message).Result |> ignore