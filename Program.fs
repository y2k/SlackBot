open System
open SlackToTelegram

module DB = SlackToTelegram.Storage
module I = SlackToTelegram.Infrastructure

type DownloadCommand = 
    { source : Source
      offset : string option }

type SaveOffsetCommand = 
    { channelId : ChannelId
      offset : string }

type SendToUserMessageCommand =
    { text : string
      userIds : User list }

type Command =
    | Initialize
    | EmptyCommand
    | GroupCommand of Command list
    | SaveOffsetCommand of SaveOffsetCommand
    | DownloadCommand of DownloadCommand
    | SendToUserMessageCommand of SendToUserMessageCommand

module ChannelUpdater =
    let tryFindOffset offsetForChannels id =
        offsetForChannels
        |> List.tryFind (fun x -> x.id = id)
        |> Option.map (fun x -> x.ts)

    let createDownloadCommands (channelForUsers : Channel list) offsetForChannels =
        channelForUsers
        |> List.map (fun x -> x.id)
        |> List.distinct
        |> List.choose 
            (fun id -> 
                Source.ComputeSource id
                |> Option.map
                    (fun source -> 
                        { source = source
                          offset = tryFindOffset offsetForChannels id }))
        |> List.map DownloadCommand
        |> GroupCommand

    let makeMessagesForUsers channelId (newMessages : Message list) (channelForUsers : Channel list) =
        let users =
            channelForUsers
            |> List.filter (fun x -> x.id = channelId)
            |> List.map (fun x -> x.user)
            |> fun xs -> if List.isEmpty newMessages then [] else xs

        let text =
            Message.makeUpdateMessage newMessages channelId
       
        SendToUserMessageCommand { text = text ; userIds = users }

    let makeSaveNewOffsetCommand channelId (newMessages : Message list) =
        let offset = newMessages |> List.map (fun x -> x.ts) |> List.tryHead
        match offset with
        | Some o -> SaveOffsetCommand { channelId = channelId ; offset = o }
        | None -> EmptyCommand

    let private filterNewMessages optOffset messages =
        match optOffset with
        | None -> messages
        | Some offset ->
            messages
            |> List.takeWhile (fun x -> x.ts <> offset)

    let filterChannelForNewsAndOffset source cfu optOffset messages =
        let newMessages = filterNewMessages optOffset messages
        let getChanneId = function | Slack name -> name | Gitter url -> url
        let cmd1 =
            makeMessagesForUsers (getChanneId source) newMessages cfu
        let cmd2 =
            makeSaveNewOffsetCommand (getChanneId source) newMessages
        GroupCommand [ cmd1 ; cmd2 ]

module CommandExecutor =
    let rec private execute cmd gitterToken tgToken = 
        async {
            match cmd with
            | Initialize -> 
                let! cfu = Storage.agent.PostAndAsyncReply QueryUsers
                let! ofc = Storage.agent.PostAndAsyncReply QueryChannels
                let cmd = ChannelUpdater.createDownloadCommands cfu ofc
                do! execute cmd gitterToken tgToken
            | GroupCommand subCmds -> 
                for subCmd in subCmds do
                    do! execute subCmd gitterToken tgToken
            | SaveOffsetCommand x -> 
                do! Storage.saveOffset x.channelId x.offset
            | DownloadCommand x -> 
                let! cfu = Storage.agent.PostAndAsyncReply QueryUsers
                do! match x.source with
                    | Slack name -> Slack.getSlackMessages name
                    | Gitter url -> Gitter.getMessages url gitterToken
                    >>- ChannelUpdater.filterChannelForNewsAndOffset x.source cfu x.offset
                    >>= fun cmd2 -> execute cmd2 gitterToken tgToken
            | SendToUserMessageCommand x -> 
                do! Telegram.sendBroadcast tgToken x.text x.userIds |> Async.Ignore
            | EmptyCommand -> do! async.Zero ()
        }

    let start gitter telegramToken =
        async {
            while true do
                do! execute Initialize gitter telegramToken
                do! Async.Sleep 30_000
        }

module Services = 
    let private tryAddChannel user textId = 
        async {
            let source = Source.ComputeSource textId
            do! match source with
                | Some _ -> DB.add user textId
                | None   -> async.Zero ()
            return Message.subscribe textId source
        }
    
    let handleTelegramCommand (user : string) (message : string) = 
        printfn "handleTelegramCommand | %s" message
        match Message.parseCommand message with
        | Top -> 
            Slack.getSlackChannels() 
            >>- Message.makeMessageForTopChannels
        | Ls -> 
            DB.agent.PostAndAsyncReply QueryUsers
            >>- (List.filter (fun x -> x.user = user) >> Message.makeMessageFromUserChannels)
        | Add id -> tryAddChannel user id
        | Rm id -> DB.remove user id |> Async.ignore (Message.unsubscribe id)
        | Unknow -> Message.help |> async.Return

[<EntryPoint>]
let main _ = 
    printfn "listening for slack updates..."

    let gitterToken = Environment.GetEnvironmentVariable "GITTER_TOKEN"
    let telegramToken = Environment.GetEnvironmentVariable "TELEGRAM_TOKEN"

    Telegram.repl telegramToken Services.handleTelegramCommand

    CommandExecutor.start gitterToken telegramToken
    |> Async.RunSynchronously
    0