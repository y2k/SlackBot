module SlackToTelegram.Storage

open System
open System.IO
open Dapper
open Microsoft.Data.Sqlite

let TelegramId = "_ _"

let private mkConnection () = 
    Directory.CreateDirectory("resources") |> ignore
    let db = new SqliteConnection("DataSource=resources/main.db")
    db.Execute("
        create table if not exists channels (id TEXT, user TEXT);
        drop table if exists offsets;
        create table offsets (id TEXT, ts TEXT);") 
    |> ignore
    db

let private format template args = 
    args
    |> List.map (fun x -> x :> obj)
    |> List.toArray
    |> uncurry String.Format template 

let private querySql<'T> (connection : SqliteConnection) sql args = 
    connection.QueryAsync<'T>(format sql args) 
    |> Async.AwaitTask
    >>- Seq.toList

let private execute (connection : SqliteConnection) sql args = 
    connection.ExecuteAsync(format sql args)
    |> Async.AwaitTask
    |> Async.Ignore

let db = MailboxProcessor.Start(fun inbox -> 
    let connection = mkConnection ()
    let rec loop () =
        async {
            let! cmd = inbox.Receive ()
            do! match cmd with
                | QueryUsers reply -> 
                    querySql<Channel> connection "select * from channels" []
                    >>- reply.Reply
                | QueryChannels reply -> 
                    querySql<OffsetForChannel> connection "select * from offsets" []
                    >>- reply.Reply
                | Execute (sql, args, reply) -> 
                    execute connection sql args
                    >>- reply.Reply
                | SaveOffset (id, offset, reply) ->
                    execute connection
                            "delete from offsets where id = '{0}'; insert into offsets (id, ts) values ('{0}', '{1}')"
                            [ id; offset ]
                    >>- reply.Reply
                | Remove (user, id, reply) ->
                    execute connection
                            "delete from channels where user = '{0}' and id = '{1}'"
                            [ user; id ]
                    >>- reply.Reply
                | AddCmd (user, id, reply) ->
                    execute connection
                            "insert into channels (id, user) values ('{0}', '{1}')"
                            [ id; user ]                            
                    >>- reply.Reply
            return! loop ()
        }
    loop ())
