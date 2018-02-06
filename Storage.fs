module SlackToTelegram.Storage

open System
open System.IO
open System.Threading
open Dapper
open Microsoft.Data.Sqlite

let TelegramId = "_ _"

let private format template args = 
    let xs = 
        args
        |> List.map (fun x -> x :> obj)
        |> List.toArray
    String.Format(template, xs)

let private connection = 
    lazy (
        Directory.CreateDirectory("resources") |> ignore
        let db = new SqliteConnection("DataSource=resources/main.db")
        db.Execute("
            create table if not exists channels (id TEXT, user TEXT);
            drop table if exists offsets;
            create table offsets (id TEXT, ts TEXT);") 
        |> ignore
        db)

let private lock = new SemaphoreSlim(1)

let private querySql<'T> sql args = 
    async { 
        do! lock.WaitAsync() |> Async.AwaitTask
        let! q = connection.Value.QueryAsync<'T>(format sql args) 
                 |> Async.AwaitTask
        lock.Release() |> ignore
        return q |> Seq.toList
    }

let private execute sql args = 
    async { 
        do! lock.WaitAsync() |> Async.AwaitTask
        do! connection.Value.ExecuteAsync(format sql args)
            |> Async.AwaitTask
            |> Async.Ignore
        lock.Release() |> ignore
    }

let private querySql2<'T> sql args = 
    connection.Value.QueryAsync<'T>(format sql args) 
    |> Async.AwaitTask
    <!> Seq.toList

let private execute2 sql args = 
    connection.Value.ExecuteAsync(format sql args)
    |> Async.AwaitTask
    |> Async.Ignore

let agent = MailboxProcessor.Start(fun inbox -> 
                let rec loop () =
                    async {
                        let! cmd = inbox.Receive ()
                        do! match cmd with
                            | QueryUsers reply -> 
                                querySql2<Channel> "select * from channels" []
                                <!> reply.Reply
                            | QueryChannels reply -> 
                                querySql2<OffsetForChannel> "select * from offsets" []
                                <!> reply.Reply
                            | Execute(sql, args, reply) -> 
                                execute2 sql args
                                <!> reply.Reply
                        return! loop ()
                    }
                loop ())

let saveOffset (id : string) (offset : string) = 
    execute 
        "delete from offsets where id = '{0}'; insert into offsets (id, ts) values ('{0}', '{1}')" 
        [ id; offset ]
let remove (user : User) (id : ChannelId) = 
    execute "delete from channels where user = '{0}' and id = '{1}'" 
        [ user; id ]
let add (user : User) (id : ChannelId) = 
    execute "insert into channels (id, user) values ('{0}', '{1}')" 
        [ id; user ]