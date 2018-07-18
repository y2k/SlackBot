namespace SlackToTelegram

open FSharpPlus

open SlackToTelegram.Utils
open Infrastructure

type SlackChannelPurpose = 
    { value : string }

type SlackChannel = 
    { channel_id : string
      name : string
      num_members : int
      purpose : SlackChannelPurpose }

module Gitter = 
    type GitterMessageUser = { username: string }
    type GitterMessage = { text: string; fromUser: GitterMessageUser; id: string }
    type GitterMessageList = GitterMessage list
    
    let toMessage (x : GitterMessage) = 
        { text = x.text
          user = x.fromUser.username
          ts = x.id }

    let extractIdFromHtml = 
        function 
        | Regex "\"troupe\":{\"id\":\"([^\"]+)" [ id ] -> Some id
        | _ -> None
    
    let getMessages (url : string) (token : string) = 
        url
        |> Http.downloadString []
        |> Async.mapOption extractIdFromHtml
        |>> sprintf "https://api.gitter.im/v1/rooms/%s/chatMessages"
        >>= Http.downloadJson<GitterMessageList> [ "x-access-token", token ]
        |>> (List.map toMessage >> List.rev)

module Slack = 
    open System.Collections.Generic
    
    type ProfileResponse = 
        { real_name : string }
    
    type UserResponse = 
        { user_id : string
          name : string
          profile : ProfileResponse }
    
    type RelatedResponse = 
        { users : Dictionary<string, UserResponse> }
    
    type MessagesResponse = 
        { messages : Message []
          related : RelatedResponse }
    
    let fixUserNames r = 
        r.messages
        |> Array.toList
        |> List.map (fun x -> 
               tryGet r.related.users x.user
               |> Option.map (fun x -> x.profile.real_name)
               |> Option.defaultValue "Unknown"
               |> fun name -> { x with user = name })
    
    type ChannelsResponse = 
        { channels : SlackChannel [] }
    
    let getSlackChannels() = 
        "https://api.slackarchive.io/v1/channels?team_id=T09229ZC6"
        |> Http.downloadJson<ChannelsResponse> []
        |>> fun x -> x.channels |> Array.toList

    let private getChannelIdFromName name (chanIds: SlackChannel list) =
        chanIds |> List.filter (fun x -> x.name = name) 
        |> List.map (fun x -> string x.channel_id)
        |> List.head

    let getSlackMessages name = 
        getSlackChannels ()
        |>> getChannelIdFromName name
        |>> sprintf "https://api.slackarchive.io/v1/messages?size=5&channel=%s"
        >>= Http.downloadJson<MessagesResponse> []
        |>> fixUserNames

module Telegram = 
    open System
    open Telegram.Bot
    open Telegram.Bot.Types
    
    type TelegramResponse = 
        | SuccessResponse
        | BotBlockedResponse
        | UnknownErrorResponse
    
    let sendToTelegramSingle (token : Token) (user : string) textMode message = 
        async { 
            try 
                let bot = TelegramBotClient(token)
                let userId = user |> ChatId.op_Implicit
                do! match textMode with
                    | Styled -> 
                        bot.SendTextMessageAsync
                            (userId, message, 
                             parseMode = Enums.ParseMode.Html)
                    | Plane -> bot.SendTextMessageAsync(userId, message)
                    |> Async.AwaitTask
                    |> Async.Ignore
                return SuccessResponse
            with
            | :? AggregateException as ae -> 
                printfn "Telegram aggregate error: %O" ae.InnerException.Message
                match ae.InnerException with
                | :? Exceptions.ApiRequestException -> 
                    return BotBlockedResponse
                | _ -> return UnknownErrorResponse
            | ex -> 
                printfn "Telegram error: %O" ex.Message
                return UnknownErrorResponse
        }
    
    let sendBroadcast token message users =
        users
        |> List.map (fun u -> sendToTelegramSingle token u Styled message)
        |> Async.seq

    let repl token callback = 
        let bot = TelegramBotClient(token)
        bot.OnUpdate 
        |> Event.add (fun x -> 
            async { 
                try 
                    let user = string x.Update.Message.From.Id
                    let! response = callback user x.Update.Message.Text
                    do! sendToTelegramSingle token user Styled response 
                        |> Async.Ignore
                with e -> printfn "ERROR: %O" e
            } |> Async.Start)
        bot.StartReceiving()