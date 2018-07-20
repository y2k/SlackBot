namespace SlackToTelegram

open Infrastructure

type SlackChannel = 
    { channel_id  : string
      name        : string
      num_members : int
      purpose     : string }

module Gitter = 
    type GitterMessageUser = { username: string }
    type GitterMessage = { text: string; fromUser: GitterMessageUser; id: string }
    type GitterMessageList = GitterMessage list
    
    let toMessage (x : GitterMessage) = 
        { text = x.text
          user = x.fromUser.username
          ts = x.id }

    let extractIdFromHtml = function 
        | Regex "\"troupe\":{\"id\":\"([^\"]+)" [ id ] -> Some id
        | _ -> None
    
    let getMessages (url : string) (token : string) = 
        url
        |> Http.downloadString []
        |> Async.mapOption extractIdFromHtml
        >>- sprintf "https://api.gitter.im/v1/rooms/%s/chatMessages"
        >>= Http.downloadJson<GitterMessageList> [ "x-access-token", token ]
        >>- (List.map toMessage >> List.rev)

module Slack = 
    open System
    open SlackAPI

    let private userId = Environment.GetEnvironmentVariable "SLACK_API_USERID"
    let private password = Environment.GetEnvironmentVariable "SLACK_API_PASSWORD"

    let private makeClient =
        async {
            let! response =
                SlackTaskClient.AuthSignin (userId, "T09229ZC6", password)
                |> Async.AwaitTask
            return SlackTaskClient response.token
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
                      ts   = string x.id } )
                |> Array.toList
        }

module Telegram = 
    open System
    open Telegram.Bot
    open Telegram.Bot.Types
    
    type TelegramResponse = 
        | SuccessResponse
        | BotBlockedResponse
        | UnknownErrorResponse
    
    let sendToTelegramSingle (token : Token) textMode message (user : string) = 
        async { 
            try 
                let bot = TelegramBotClient (token)
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
                | :? Exceptions.ApiRequestException -> return BotBlockedResponse
                | _                                 -> return UnknownErrorResponse
            | _ -> return UnknownErrorResponse
        }
    
    let sendBroadcast token message (users: string list) =
        users
        |> List.map (sendToTelegramSingle token Styled message)
        |> Async.seq

    let repl token callback = 
        let bot = TelegramBotClient(token)
        bot.OnUpdate 
        |> Event.add (fun x -> 
            async { 
                try 
                    let user = string x.Update.Message.From.Id
                    let! response = callback user x.Update.Message.Text
                    do! sendToTelegramSingle token Styled response user
                        |> Async.Ignore
                with e -> printfn "ERROR: %O" e
            } |> Async.Start)
        bot.StartReceiving()