namespace SlackToTelegram

module Storage =
    open System
    open Dapper
    open Microsoft.Data.Sqlite

    let private format template args =
        let xs = args |> List.map (fun x -> x :> obj) |> List.toArray
        String.Format(template, xs)

    let private connection = lazy (
        let db = new SqliteConnection("DataSource=main.db")
        db.Execute("create table if not exists subscriptions (id TEXT, user TEXT)") |> ignore
        db.Execute("create table if not exists offset (id INTEGER)") |> ignore
        db)

    let private querySql<'T> sql args = connection.Value.Query<'T>(format sql args) |> Seq.toList
    let private execute sql args = connection.Value.Execute(format sql args) |> ignore

    let query (user: User) = 
        querySql<Channel> "select * from subscriptions where user = '{0}'" [user]
    let remove (user: User) (id: ChannelId) =
        execute "delete from subscriptions where user = '{0}' and id = '{1}'" [user; id]
    let add (user: User) (id: ChannelId) =
        execute "insert into subscriptions (id, user) values ('{0}', '{1}')" [id; user]

    let getOffset () =
        querySql<TelegramOffset> "select id from offset" [] |> List.tryHead
    let setOffset (o:TelegramOffset) =
        execute "delete from offset; insert into offset (id) values ('{0}')" [o]

    let getAllChannels () =
        querySql<string> "select distinct id from subscriptions" []

    let getUsersForChannel (channel: string) =
        querySql<string> "select user from subscriptions where id = '{0}'" [channel]