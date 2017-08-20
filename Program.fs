open System
open System.Net
open System.Reactive.Linq
open System.Threading
open SlackToTelegram
open SlackToTelegram.Utils

module DB = SlackToTelegram.Storage
module I = SlackToTelegram.Infrastructure
open Infrastructure

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
    let private computeSource id =
        match id with
        | Regex "https://gitter\\.im/\\w{1,10}/\\w{1,10}" [] -> Gitter "" |> Some
        | Regex "[\\w_-]{1,10}" [] -> Slack id |> Some
        | _ -> None

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
                computeSource id
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

    let sendToUserMessageCommand (x : SendToUserMessageCommand) = 
        Telegram.sendBroadcast x.text x.userIds |> Async.Ignore

    let saveOffset (x : SaveOffsetCommand) = 
        Storage.saveOffset x.channelId x.offset

    let private getChanneId =
        function
        | Slack name -> name
        | Gitter url -> url

    let download (x : DownloadCommand) cfu gitterToken =
        async {
            let! chanIds = Slack.getSlackChannels ()
            let! msgs =
                match x.source with
                | Slack name -> 
                    let id = chanIds |> List.filter (fun x -> x.name = name) 
                                     |> List.map (fun x -> string x.channel_id)
                                     |> List.head
                    Slack.getSlackMessages id
                | Gitter url -> Gitter.getMessages url gitterToken

            let cmd1 =
                ChannelUpdater.makeMessagesForUsers (getChanneId x.source) msgs cfu
            let cmd2 =
                ChannelUpdater.makeSaveNewOffsetCommand "" msgs

            return GroupCommand [ cmd1 ; cmd2 ]
        }

    let rec execute cmd = 
        async {
            do! match cmd with
                | Initialize -> 
                    async {
                        let! cfu = Storage.queryChannelForUser ()
                        let! ofc = Storage.queryOffsetForChannel ()
                        let cmd = ChannelUpdater.createDownloadCommands cfu ofc
                        do! execute cmd
                    }
                | GroupCommand subCmds -> 
                    async {
                        for subCmd in subCmds do
                            do! execute subCmd
                    }
                | SaveOffsetCommand x -> saveOffset x
                | DownloadCommand x -> 
                    async {
                        let! cfu = Storage.queryChannelForUser ()
                        let! cmd2 = download x cfu
                        do! execute cmd2
                    }
                | SendToUserMessageCommand x -> sendToUserMessageCommand x
                | EmptyCommand -> async.Zero ()
        }

    let executeCycle gitter =
        async {
            while true do
                do! execute Initialize
                do! Async.Sleep 30_000
        }
































// type Task = 
//     { source : Source
//       offset : ChannelOffset
//       id : string
//       originId : string }

// type UserTask = 
//     { message : string option
//       offset : ChannelOffset }

// module Domain = 
//     let parseCommand command = 
//         match String.split command with
//         | "top" :: _ -> Top
//         | "ls" :: _ -> Ls
//         | "add" :: x :: _ -> Add x
//         | "rm" :: x :: _ -> Rm x
//         | _ -> Unknow
    
//     // let filterChannelsWithIds dbChannels channels = 
//     //     channels |> List.where (fun x -> dbChannels |> List.contains x.name)
//     // [<Obsolete>]
//     // let private limitMessagesToOffset offset slackMessages = 
//     //     let channelOffset = offset |> Option.defaultValue "0"
//     //     slackMessages |> List.takeWhile (fun x -> x.ts <> channelOffset)
//     // [<Obsolete>]
//     // let private makeTelegramMessageAboutUpdates (usersForChannel : User list) ch 
//     //     newMessages = 
//     //     match newMessages with
//     //     | [] -> []
//     //     | _ -> 
//     //         usersForChannel 
//     //         |> List.map 
//     //                (fun tid -> 
//     //                Message.makeUpdateMessage newMessages ch.name, tid)
//     // [<Obsolete>]
//     // let toUpdateNotificationWithOffset ch 
//     //     (slackMessages, offset, usersForChannel) = 
//     //     let newMessages = limitMessagesToOffset offset slackMessages
//     //     let newOffset = newMessages |> List.tryPick (fun x -> Some x.ts)
//     //     let msgs = 
//     //         makeTelegramMessageAboutUpdates usersForChannel ch newMessages
//     //     newOffset, msgs

//     let toTasks slackChannels localIds (offsets : (string * ChannelOffset) list) = 
//         let setOffset (ch: Task) = 
//             { ch with offset = 
//                           offsets
//                           |> List.tryFind (fun (id, _) -> id = ch.originId)
//                           |> Option.map (fun (_, o) -> o)
//                           |> Option.defaultValue EmptyOffset }
        
//         let toTask id = 
//             match SourceF.computeSource id with
//             | Some Gitter -> 
//                 Some { source = Gitter
//                        offset = EmptyOffset
//                        id = id
//                        originId = id }
//             | Some Slack -> 
//                 slackChannels
//                 |> List.tryFind (fun ch -> ch.name = id)
//                 |> Option.map (fun ch -> 
//                        { source = Slack
//                          offset = EmptyOffset
//                          id = ch.channel_id
//                          originId = id })
//             | None -> None
//             |> Option.map setOffset
        
//         localIds
//         |> List.map toTask
//         |> List.choose id
    
//     let toUserMessageAndOffset messages offset channelName = 
//         let newMessages = 
//             match offset with
//             | ChannelOffset _ -> 
//                 messages |> List.takeWhile (fun x -> x.ts <> offset)
//             | EmptyOffset -> messages
        
//         let newOffset = 
//             messages
//             |> List.map (fun x -> x.ts)
//             |> List.tryHead
//             |> Option.defaultValue EmptyOffset
        
//         let message = Message.makeUpdateMessage' newMessages channelName
//         { message = message
//           offset = newOffset }

// module Services = 
//     let private tryAddChannel user textId = 
//         SourceF.computeSource textId
//         |> Option.mapAsync (fun _ -> DB.add user textId)
//         |> Async.map (Message.subscribe textId)
    
//     let handleTelegramCommand (user : User) (message : string) = 
//         match Domain.parseCommand message with
//         | Top -> 
//             Slack.getSlackChannels() 
//             |> Async.map Message.makeMessageForTopChannels
//         | Ls -> 
//             DB.queryUserChannels user 
//             |> Async.map Message.makeMessageFromUserChannels
//         | Add id -> tryAddChannel user id
//         | Rm id -> DB.remove user id |> Async.ignore (Message.unsubscribe id)
//         | Unknow -> Message.help |> async.Return
    
//     let private loadChannelUpdates ch = 
//         async { let! slackMessages = Slack.getSlackMessages ch.channel_id
//                 let! offset = DB.getOffsetWith ch.name
//                 let! users = DB.getUsersForChannel ch.name
//                 return slackMessages, offset, users }
    
//     let private notifyUpdatesAndSaveOffset token ch (newOffset, msgs) = 
//         async { 
//             do! match newOffset with
//                 | Some x -> DB.setOffsetWith ch.name x
//                 | None -> async.Zero()
//             for (message, tid) in msgs do
//                 let! r = message 
//                          |> Telegram.sendToTelegramSingle token tid Styled
//                 if r = Telegram.BotBlockedResponse then 
//                     do! DB.removeChannelsForUser tid
//         }
    
//     // let private getChannelForUpdateChecks() = 
//     //     Slack.getSlackChannels()
//     //     |> Async.zip (DB.getAllChannels())
//     //     |> Async.map2 Domain.filterChannelsWithIds
//     // [<Obsolete>]
//     // let private handleChannel token ch = 
//     //     loadChannelUpdates ch
//     //     |> Async.map (Domain.toUpdateNotificationWithOffset ch)
//     //     |> Async.bind (notifyUpdatesAndSaveOffset token ch)
//     let todo gitterToken = 
//         let handle (task : Task) = 
//             async { 
//                 let! newMessages = match task.source with
//                                    | Slack -> Slack.getSlackMessages task.id
//                                    | Gitter -> 
//                                        Gitter.getMessages task.id gitterToken
//                 let (message, newOffset) = 
//                     Domain.toUserMessageAndOffset newMessages task.offset 
//                         task.originId
//                 match newOffset with
//                 | Some x -> do! DB.setOffsetWith task.originId x
//                 | _ -> ()
//                 // do! newOffset 
//                 //     |> Async.map3 (DB.setOffsetWith task.originId)
//                 //     |> Async.Ignore
//                 // do! DB.setOffsetWith task.originId newOffset
//                 let! users = DB.getUsersForChannel task.originId
//                 let! response = Telegram.sendBroadcast message users
//                 do! response
//                     |> List.zip users
//                     |> List.filter 
//                            (fun (_, s) -> s = Telegram.BotBlockedResponse)
//                     |> List.map (fun (u, _) -> DB.removeChannelsForUser u)
//                     |> Async.Parallel
//                     |> Async.Ignore
//                 return ()
//             }
//         async { 
//             let! slackChannels = Slack.getSlackChannels()
//             let! localChannels = DB.getAllChannels()
//             let offsets = []
//             return! Domain.toTasks slackChannels localChannels offsets
//                     |> async.Return
//                     |> Async.forAll handle
//         }

// let checkUpdates token = 
//     getChannelForUpdateChecks() |> Async.forAll (handleChannel token)
[<EntryPoint>]
let main argv = 
    printfn "listening for slack updates..."
    let (token, gitterApi) = argv.[0], argv.[1]
    // Telegram.repl token Services.handleTelegramCommand
    // I.loop (fun _ -> Services.checkUpdates token)
    0