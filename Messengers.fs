namespace SlackToTelegram

type SlackChannelPurpose = 
    { value : string }

type SlackChannel = 
    { channel_id : string
      name : string
      num_members : int
      purpose : SlackChannelPurpose }

module Messengers = 
    open System
    open System.Collections.Generic
    open System.IO
    open System.Net
    open System.Net.Http
    open Newtonsoft.Json
    open Telegram.Bot
    open Telegram.Bot.Types
    open SlackToTelegram.Utils
    
    module I = Infrastructure
    module db = SlackToTelegram.Storage
    
    let getNewBotMessages token = 
        let bot = TelegramBotClient(token)
        
        let result = 
            bot.OnUpdate
            |> Observable.map (fun x -> x.Update)
            |> Observable.map (fun x -> 
                   { text = x.Message.Text
                     user = string x.Message.From.Id
                     ts = "" })
        bot.StartReceiving()
        result

    type ProfileResponse = 
        { real_name : string }
    
    type UserResponse = 
        { user_id : string
          name : string
          profile : ProfileResponse }
    
    type RelatedResponse = 
        { users : Dictionary<string, UserResponse> }
    
    type MessagesResponse = 
        { messages : SlackMessage []
          related : RelatedResponse }
    
    let getSlackMessages (channelId : string) = 
        "https://api.slackarchive.io/v1/messages?size=5&channel=" + channelId
        |> I.download<MessagesResponse>
        |> (fun r -> 
        r.messages
        |> Array.toList
        |> List.map (fun x -> 
               tryGet r.related.users x.user
               |> Option.map (fun x -> x.profile.real_name)
               |> Option.defaultValue "Unknown"
               |> fun name -> { x with user = name }))
    
    type ChannelsResponse = 
        { channels : SlackChannel [] }
    
    let getSlackChannels() = 
        "https://api.slackarchive.io/v1/channels?team_id=T09229ZC6"
        |> I.downloadAsync<ChannelsResponse>
        |> Async.map (fun x -> x.channels |> Array.toList)

    type TelegramResponse = 
        | SuccessResponse
        | BotBlockedResponse
        | UnknownErrorResponse
    
    let sendToTelegramSingle (token : Token) (user : string) html message = 
        try 
            let bot = TelegramBotClient(token)
            let userId = user |> ChatId.op_Implicit
            match html with
            | Styled -> 
                bot.SendTextMessageAsync(userId, message, parseMode = Types.Enums.ParseMode.Html).Result
            | Plane -> bot.SendTextMessageAsync(userId, message).Result
            |> ignore
            SuccessResponse
        with
        | :? AggregateException as ae -> 
            printfn "Telegram aggregate error: %O" ae.InnerException.Message
            match ae.InnerException with
            | :? Telegram.Bot.Exceptions.ApiRequestException -> 
                BotBlockedResponse
            | _ -> UnknownErrorResponse
        | ex -> 
            printfn "Telegram error: %O" ex.Message
            UnknownErrorResponse
    
    [<Obsolete>]
    let repl token callback = 
        getNewBotMessages token
        |> Observable.map (fun x -> 
               let (user, response) = x.user, x.text |> callback x.user
               sendToTelegramSingle token user Styled response |> ignore)
        |> fun o -> o.Subscribe(DefaultErrorHandler())
        |> ignore
    
    let repl' token callback = 
        getNewBotMessages token
        |> Observable.flatMapTask 
               (fun x -> 
               async 
                   { let! response = callback x.user x.text
                     sendToTelegramSingle token x.user Styled response |> ignore })
        |> fun o -> o.Subscribe(DefaultErrorHandler())
        |> ignore