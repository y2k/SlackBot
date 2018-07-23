open SlackToTelegram

type SaveOffsetCommand = 
    { channelId : ChannelId
      offset    : string option
      text      : string
      userIds   : User list }

module UpdateDomain =
    let private tryFindOffset offsetForChannels id =
        offsetForChannels
        |> List.tryFind (fun x -> x.id = id)
        |> Option.map (fun x -> x.ts)
    let mkDownloadRequests (channelForUsers : Channel list) offsetForChannels =
        channelForUsers
        |> List.map (fun x -> x.id)
        |> List.distinct
        |> List.choose 
            (fun id -> 
                Source.ComputeSource id
                |> Option.map (fun source -> source, tryFindOffset offsetForChannels id))
    let private mkMessagesForUsers channelId (newMessages : Message list) (channelForUsers : Channel list) =
        let users =
            channelForUsers
            |> List.filter (fun x -> x.id = channelId)
            |> List.map (fun x -> x.user)
            |> fun xs -> if List.isEmpty newMessages then [] else xs
        { channelId = channelId
          offset = newMessages |> List.tryPick (fun x -> Some x.ts)
          text = Message.makeUpdateMessage newMessages channelId
          userIds = users }
    let private filterNewMessages optOffset messages =
        match optOffset with
        | None -> messages
        | Some offset ->
            messages
            |> List.takeWhile (fun x -> x.ts <> offset)
    let toSendMessageCmd source optOffset messages cfu =
        let newMessages = filterNewMessages optOffset messages
        let id = match source with | Slack name -> name | Gitter url -> url
        mkMessagesForUsers id newMessages cfu

module Updater =
    let private mkSendMessageCmd source offset =
        match source with
        | Slack name -> Slack.getSlackMessages name
        | Gitter url -> Gitter.getMessages url
        <*> Storage.db.PostAndAsyncReply QueryUsers
        >>- uncurry (UpdateDomain.toSendMessageCmd source offset)
    let rec start () =
        async {
            let! commands =
                Storage.db.PostAndAsyncReply QueryUsers
                <*> Storage.db.PostAndAsyncReply QueryChannels
                >>- uncurry UpdateDomain.mkDownloadRequests
                >>= (List.map (uncurry mkSendMessageCmd) >> Async.seq)

            do! commands
                |> List.map (fun x -> Telegram.sendBroadcast x.text x.userIds)
                |> Async.seq >>- ignore

            do! commands
                |> List.choose (fun x -> x.offset |> Option.map (fun o -> x.channelId, o))
                |> List.map (
                       fun (channelId, offset) -> 
                           Storage.db.PostAndAsyncReply (
                               fun r -> SaveOffset (channelId, offset, r)))
                |> Async.seq >>- ignore

            #if DEBUG
            do! Async.Sleep 2_000
            #else
            do! Async.Sleep 30_000
            #endif
            do! start ()
        }

module Services = 
    let private tryAddChannel user textId = 
        async {
            let source = Source.ComputeSource textId
            do! match source with
                | Some _ -> Storage.db.PostAndAsyncReply (fun r -> AddCmd (user, textId, r))
                | None   -> async.Zero ()
            return Message.subscribe textId source
        }
    let handleTelegramCommand (user : string) (message : string) = 
        printfn "handleTelegramCommand | %s" message
        match Message.parseCommand message with
        | Top -> 
            Slack.getSlackChannels
            >>- Message.makeMessageForTopChannels
        | Ls -> 
            Storage.db.PostAndAsyncReply QueryUsers
            >>- (List.filter (fun x -> x.user = user) >> Message.makeMessageFromUserChannels)
        | Add id -> tryAddChannel user id
        | Rm id -> Storage.db.PostAndAsyncReply (fun r -> Remove (user, id, r))
                   |> Async.ignore (Message.unsubscribe id)
        | Unknow -> Message.help |> async.Return

[<EntryPoint>]
let main _ = 
    printfn "listening for slack updates..."
    Telegram.repl Services.handleTelegramCommand
    Updater.start () |> Async.RunSynchronously
    0