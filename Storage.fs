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
        CREATE TABLE IF NOT EXISTS channels (id TEXT, user TEXT);
        DROP TABLE IF EXISTS offsets;
        CREATE TABLE offsets (id TEXT, ts TEXT);") 
    |> ignore
    db

let private format template args = 
    args
    |> List.map (fun x -> x :> obj)
    |> List.toArray
    |> curry String.Format template 

let private queryAndReply<'a> (connection : SqliteConnection) (reply : AsyncReplyChannel<'a list>) sql args = 
    connection.QueryAsync<'a>(format sql args) 
    |> Async.AwaitTask
    >>- (Seq.toList >> reply.Reply)

let private executeAndReply (connection : SqliteConnection) (reply : AsyncReplyChannel<unit>) sql args = 
    connection.ExecuteAsync(format sql args)
    |> Async.AwaitTask
    >>- (ignore >> reply.Reply)

let db = MailboxProcessor.Start(fun inbox -> 
    let connection = mkConnection ()
    let rec loop () =
        async {
            let! cmd = inbox.Receive ()
            do! match cmd with
                | QueryUsers reply -> 
                    queryAndReply<Channel> connection reply "SELECT * FROM channels" []
                | QueryChannels reply -> 
                    queryAndReply<OffsetForChannel> connection reply "SELECT * FROM offsets" []
                | SaveOffset (id, offset, reply) ->
                    executeAndReply connection reply
                            "DELETE FROM offsets WHERE id = '{0}'; INSERT INTO offsets (id, ts) VALUES ('{0}', '{1}')"
                            [ id; offset ]
                | Remove (user, id, reply) ->
                    executeAndReply connection reply
                            "DELETE FROM channels WHERE user = '{0}' AND id = '{1}'"
                            [ user; id ]
                | AddCmd (user, id, reply) ->
                    executeAndReply connection reply
                            "INSERT INTO channels (id, user) VALUES ('{0}', '{1}')"
                            [ id; user ]
            return! loop ()
        }
    loop ())