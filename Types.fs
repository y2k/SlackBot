namespace SlackToTelegram

open System
open Infrastructure

type Source = 
    | Slack of string
    | Gitter of string
    static member ComputeSource id =
        match id with
        | Regex "https://gitter\\.im/\\w{1,10}/\\w{1,10}" [] -> Some <| Gitter id
        | Regex "[\\w_-]{1,10}" [] -> Some <| Slack id
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

type Channel = 
    { id : ChannelId
      user : User }

[<CLIMutable>]
type ChannelForUser = 
    { user : User
      id : ChannelId }

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
      ts : string option }

[<Obsolete>]
type TextMode = 
    | Plane
    | Styled