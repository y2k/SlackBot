namespace SlackToTelegram

open Infrastructure

type Source = 
    | Slack of string
    | Gitter of string
    static member ComputeSource id =
        match id with
        | Regex "^https://gitter\\.im/[\\w\\._-]{1,40}/[\\w\\._-]{1,40}$" [] -> Some <| Gitter id
        | Regex "^[\\w_-]{1,16}$" [] -> Some <| Slack id
        | _ -> None

type Command = 
    | Top
    | Ls
    | Add of string
    | Rm of string
    | Unknow

type TelegramOffset = string
type User = string
type Token = string
type ChannelId = string

[<CLIMutable>]
type Channel = 
    { id   : ChannelId
      user : User }

[<CLIMutable>]
type OffsetForChannel =
    { id : ChannelId 
      ts : string }

type ChannelOffset = 
    | ChannelOffset of string
    | EmptyOffset

type Message = 
    { text : string
      user : string
      ts   : string }

type TextMode = 
    | Plane
    | Styled

type StorageCmd = 
    | QueryUsers of AsyncReplyChannel<Channel list> 
    | QueryChannels of AsyncReplyChannel<OffsetForChannel list> 
    | SaveOffset of id : string  * offset : string * AsyncReplyChannel<unit>
    | Remove of user : User * id : ChannelId * AsyncReplyChannel<unit>
    | AddCmd of user : User * id : ChannelId * AsyncReplyChannel<unit>