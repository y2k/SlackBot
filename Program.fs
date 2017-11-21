open System
open SlackToTelegram
open Infrastructure
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

    let createDownloadCommands (channelForUsers : ChannelForUser list) offsetForChannels =
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

    let makeMessagesForUsers channelId (newMessages : Message list) (channelForUsers : ChannelForUser list) =
        let users =
            channelForUsers
            |> List.filter (fun x -> x.id = channelId)
            |> List.map (fun x -> x.user)
            |> fun xs -> if List.isEmpty newMessages then [] else xs

        let text =
            Message.makeUpdateMessage newMessages channelId
       
        SendToUserMessageCommand { text = text ; userIds = users }

    let makeSaveNewOffsetCommand channelId (newMessages : Message list) =
        let offset = newMessages |> List.tryPick (fun x -> x.ts)
        match offset with
        | Some o -> SaveOffsetCommand { channelId = channelId ; offset = o }
        | None -> EmptyCommand

module CommandExecutor =

    let sendToUserMessageCommand token (x : SendToUserMessageCommand) = 
        Telegram.sendBroadcast token x.text x.userIds |> Async.Ignore

    let saveOffset (x : SaveOffsetCommand) = 
        Storage.saveOffset x.channelId x.offset

    let private getChanneId =
        function
        | Slack name -> name
        | Gitter url -> url

    let download (x : DownloadCommand) cfu gitterToken =
        async {
            let! msgs =
                match x.source with
                | Slack name -> 
                    async {
                        let! chanIds = Slack.getSlackChannels ()
                        let id = chanIds |> List.filter (fun x -> x.name = name) 
                                         |> List.map (fun x -> string x.channel_id)
                                         |> List.head
                        return! Slack.getSlackMessages id
                    }
                | Gitter url -> Gitter.getMessages url gitterToken

            let cmd1 =
                ChannelUpdater.makeMessagesForUsers (getChanneId x.source) msgs cfu
            let cmd2 =
                ChannelUpdater.makeSaveNewOffsetCommand "" msgs

            return GroupCommand [ cmd1 ; cmd2 ]
        }

    let rec execute cmd gitterToken tgToken = 
        async {
            do! match cmd with
                | Initialize -> 
                    async {
                        let! cfu = Storage.queryChannelForUser ()
                        let! ofc = Storage.queryOffsetForChannel ()
                        let cmd = ChannelUpdater.createDownloadCommands cfu ofc
                        do! execute cmd gitterToken tgToken
                    }
                | GroupCommand subCmds -> 
                    async {
                        for subCmd in subCmds do
                            do! execute subCmd gitterToken tgToken
                    }
                | SaveOffsetCommand x -> saveOffset x
                | DownloadCommand x -> 
                    async {
                        let! cfu = Storage.queryChannelForUser ()
                        let! cmd2 = download x cfu gitterToken
                        do! execute cmd2 gitterToken tgToken
                    }
                | SendToUserMessageCommand x -> sendToUserMessageCommand tgToken x
                | EmptyCommand -> async.Zero ()
        }

    let executeCycle gitter telegramToken =
        async {
            while true do
                do! execute Initialize gitter telegramToken
                do! Async.Sleep 30_000
        }

module Domain = 
    let parseCommand command = 
        match String.split command with
        | "top" :: _ -> Top
        | "ls" :: _ -> Ls
        | "add" :: x :: _ -> Add x
        | "rm" :: x :: _ -> Rm x
        | _ -> Unknow

module Services = 
    let private tryAddChannel user textId = 
        Source.ComputeSource textId
        |> Option.mapAsync (fun _ -> DB.add user textId)
        |> Async.map (Message.subscribe textId)
    
    let handleTelegramCommand (user : string) (message : string) = 
        match Domain.parseCommand message with
        | Top -> 
            Slack.getSlackChannels() 
            |> Async.map Message.makeMessageForTopChannels
        | Ls -> 
            DB.queryUserChannels user 
            |> Async.map Message.makeMessageFromUserChannels
        | Add id -> tryAddChannel user id
        | Rm id -> DB.remove user id |> Async.ignore (Message.unsubscribe id)
        | Unknow -> Message.help |> async.Return

[<EntryPoint>]
let main _ = 
    printfn "listening for slack updates..."

    let gitterToken = Environment.GetEnvironmentVariable "GITTER_TOKEN"
    let telegramToken = Environment.GetEnvironmentVariable "TELEGRAM_TOKEN"

    Telegram.repl telegramToken Services.handleTelegramCommand

    CommandExecutor.executeCycle gitterToken telegramToken
    |> Async.RunSynchronously
    0