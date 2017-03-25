namespace SlackToTelegram

type TelegramOffset = int

type User = string
type Token = string

type ChannelId = string

type Channel = { id: ChannelId; user: User }

type Action = List | Help | Add of string | Remove of string

type Message = { text: string; user: string }
type Response = { messages: Message[] }

module Utils =
    open System
    open System.Reactive.Linq

    let flatMap (f: 'a -> IObservable<'b>) (o: IObservable<'a>) = Observable.SelectMany(o, f)