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
        db)

    let private querySql<'T> sql args = connection.Value.Query<'T>(format sql args) |> Seq.toList
    let private execute sql args = connection.Value.Execute(format sql args) |> ignore

    let query (user: User) = 
        querySql<Channel> "select * from subscriptions where user = {0}" [user]
    let remove (user: User) (id: ChannelId) =
        execute "delete * from subscriptions where user = {0} and id = {1}" [user; id]
    let add (user: User) (id: ChannelId) =
        execute "insert into subscriptions (id, user) values ({0}, {1})" [user; id]