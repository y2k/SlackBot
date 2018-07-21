namespace SlackToTelegram

open System
open Infrastructure

type SlackChannel = 
    { channel_id  : string
      name        : string
      num_members : int
      purpose     : string }

module Gitter = 
    let private token = lazy (Environment.GetEnvironmentVariable "GITTER_TOKEN")

    type GitterMessageUser = { username: string }
    type GitterMessage = { text: string; fromUser: GitterMessageUser; id: string }
    type GitterMessageList = GitterMessage list
    
    let toMessage (x : GitterMessage) = 
        { text = x.text
          user = x.fromUser.username
          ts   = x.id }

    let extractIdFromHtml = function 
        | Regex "\"troupe\":{\"id\":\"([^\"]+)" [ id ] -> Some id
        | _ -> None
    
    let getMessages (url : string) = 
        url
        |> Http.downloadString []
        |> Async.mapOption extractIdFromHtml
        >>- sprintf "https://api.gitter.im/v1/rooms/%s/chatMessages"
        >>= Http.downloadJson<GitterMessageList> [ "x-access-token", token.Value ]
        >>- (List.map toMessage >> List.rev)

module Slack = 
    open SlackAPI

    let private userId = lazy (Environment.GetEnvironmentVariable "SLACK_API_USERID")
    let private password = lazy (Environment.GetEnvironmentVariable "SLACK_API_PASSWORD")
    let private cachedClient : SlackTaskClient option ref = ref None

    let private makeClient =
        async {
            match !cachedClient with
            | Some client -> return client
            | None ->
                let! response =
                    SlackTaskClient.AuthSignin (userId.Value, "T09229ZC6", password.Value)
                    |> Async.AwaitTask
                let x = SlackTaskClient response.token
                cachedClient := Some x
                return x
        }

    let getSlackChannels = 
        async {
            let! client = makeClient
            let! channels = client.GetChannelListAsync () |> Async.AwaitTask

            return
                channels.channels
                |> Array.map (fun x -> { channel_id  = x.id
                                         name        = x.name
                                         num_members = x.num_members
                                         purpose     = "" })
                |> Array.toList
        }

    let getSlackMessages name =
        async {
            let! client = makeClient
            let! channels = client.GetChannelListAsync () |> Async.AwaitTask

            let id =
                channels.channels
                |> Array.find (fun x -> x.name = name)

            let! history =
                client.GetChannelHistoryAsync (id, count = Nullable 10)
                |> Async.AwaitTask

            return
                history.messages
                |> Array.map (fun x ->         
                    { text = x.text
                      user = x.username
                      ts   = string x.ts.Ticks } )
                |> Array.toList
        }

module Telegram = 
    open Telegram.Bot
    open Telegram.Bot.Types

    let private token = lazy (Environment.GetEnvironmentVariable "TELEGRAM_TOKEN")
    
    type TelegramResponse = 
        | SuccessResponse
        | BotBlockedResponse of string
        | UnknownErrorResponse of exn
    
    let sendToTelegramSingle textMode message (user : string) = 
        async { 
            try 
                let bot = TelegramBotClient (token.Value)
                let userId = user |> ChatId.op_Implicit
                do! match textMode with
                    | Styled -> Enums.ParseMode.Html
                    | Plane  -> Enums.ParseMode.Default
                    |> fun x -> bot.SendTextMessageAsync (userId, message, x)
                    |> Async.AwaitTask
                    |> Async.Ignore
                return SuccessResponse
            with
            | :? AggregateException as ae -> 
                match ae.InnerException with
                | :? Exceptions.ApiRequestException as e -> return BotBlockedResponse e.Message
                | e                                      -> return UnknownErrorResponse e
            | e -> return UnknownErrorResponse e
        }
    
    let sendBroadcast message (users: string list) =
        users
        |> List.map (sendToTelegramSingle Styled message)
        |> Async.seq

    let repl callback = 
        let bot = TelegramBotClient (token.Value)
        bot.OnUpdate 
        |> Event.add (fun x -> 
            async { 
                try 
                    let user = string x.Update.Message.From.Id
                    let! response = callback user x.Update.Message.Text
                    do! sendToTelegramSingle Styled response user
                        >>- function SuccessResponse -> () | e -> printfn "Telegram error = %O" e
                with e -> printfn "ERROR: %O" e
            } |> Async.Start)
        bot.StartReceiving()