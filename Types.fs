namespace SlackToTelegram

open System

type Source = 
    | Slack of string
    | Gitter of string

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

type ChannelForUser = 
    { user : User
      id : ChannelId }

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