namespace SlackToTelegram

type SlackChannelPurpose = { value: string }
type SlackChannel = { channel_id: string; name: string; num_members: int; purpose: SlackChannelPurpose }

module Messengers =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Net
    open System.Net.Http
    open Newtonsoft.Json
    open Telegram.Bot
    open SlackToTelegram.Utils
    
    module db = SlackToTelegram.Storage

    let getNewBotMessages token =
        let offset = db.getOffsetWith db.TelegramId |> Option.defaultValue "0"
        let msgs = TelegramBotClient(token).GetUpdatesAsync(int offset).Result |> Array.toList
        msgs |> List.map (fun x -> x.Id) |> List.sortDescending |> List.tryHead
             |> (fun x -> match x with 
                          | Some offset -> db.setOffsetWith db.TelegramId (string (offset + 1)) 
                          | _ -> ())
        msgs |> List.map (fun x -> { text = x.Message.Text; user = string x.Message.From.Id; ts = "" })

    let private download (url: string) = 
        let req = new HttpRequestMessage(HttpMethod.Get, url)
        req.Headers.Referrer <- Uri("http://kotlinlang.slackarchive.io/")
        let resp = HttpClient().SendAsync(req).Result
        resp.Content.ReadAsStringAsync().Result |> StringReader |> JsonTextReader

    type ProfileResponse = { real_name: string }
    type UserResponse = { user_id: string; name: string; profile: ProfileResponse }
    type RelatedResponse = { users: Dictionary<string, UserResponse> }
    type MessagesResponse = { messages: SlackMessage[]; related: RelatedResponse }
    
    let getSlackMessages (channelId: string) =
        "http://api.slackarchive.io/v1/messages?size=5&channel=" + channelId
        |> download |> JsonSerializer().Deserialize<MessagesResponse> 
        |> (fun r -> r.messages
                     |> Array.toList 
                     |> List.map (fun x -> tryGet r.related.users x.user 
                                           |> Option.map (fun x -> x.profile.real_name) 
                                           |> Option.defaultValue "Unknown"
                                           |> (fun name -> { x with user = name })))

    type ChannelsResponse = { channels: SlackChannel[] }
    let getSlackChannels () =
        "http://api.slackarchive.io/v1/channels?team_id=T09229ZC6" 
        |> download |> JsonSerializer().Deserialize<ChannelsResponse> 
        |> (fun x -> x.channels |> Array.toList)

    let sendToTelegramSingle (token: Token) (user: User) html message =
        try
            let bot = TelegramBotClient(token)
            match html with
            | Styled  -> bot.SendTextMessageAsync(user, message, parseMode = Types.Enums.ParseMode.Html).Result
            | Plane -> bot.SendTextMessageAsync(user, message).Result
            |> ignore
        with
        | ex -> printfn "Telegram error: %O" ex