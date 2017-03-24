namespace SlackToTelegram

type User = string

type ChannelId = string

type Channel = { id: ChannelId; user: User }

type Action = List | Help | Add of string | Remove of string