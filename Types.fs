namespace SlackToTelegram

type TelegramOffset = int

type User = string

type ChannelId = string

type Channel = { id: ChannelId; user: User }

type Action = List | Help | Add of string | Remove of string

type Message = { text: string; user: string; ts: double }
type Response = { messages: Message[] }