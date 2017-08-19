namespace SlackToTelegram

module Storage = 
    open System
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
        lazy (let db = new SqliteConnection("DataSource=main.db")
              db.Execute("
            create table if not exists channels (id TEXT, user TEXT);
            create table if not exists offsets (id INTEGER, ts TEXT);") 
              |> ignore
              db)
    
    let private lock = new SemaphoreSlim(0, 1)
    
    let private querySqlAsync<'T> sql args = 
        async { 
            do! lock.WaitAsync() |> Async.AwaitTask
            let! q = connection.Value.QueryAsync<'T>(format sql args) 
                     |> Async.AwaitTask
            lock.Release() |> ignore
            return q |> Seq.toList
        }
    
    [<Obsolete>]
    let private querySql<'T> sql args = 
        querySqlAsync<'T> sql args |> Async.RunSynchronously
    
    let private executeAsync sql args = 
        async { 
            do! lock.WaitAsync() |> Async.AwaitTask
            do! connection.Value.ExecuteAsync(format sql args)
                |> Async.AwaitTask
                |> Async.Ignore
            lock.Release() |> ignore
        }
    
    [<Obsolete>]
    let private execute sql args = 
        executeAsync sql args |> Async.RunSynchronously
    
    let queryUserChannels (user : User) = 
        querySqlAsync<Channel> "select * from channels where user = '{0}'" 
            [ user ]
    let remove (user : User) (id : ChannelId) = 
        execute "delete from channels where user = '{0}' and id = '{1}'" 
            [ user; id ]
    let add (user : User) (id : ChannelId) = 
        executeAsync "insert into channels (id, user) values ('{0}', '{1}')" 
            [ id; user ]
    let removeChannelsForUser (user : User) = 
        execute "delete from channels where user = '{0}'" [ user ]
    let getAllChannels() = 
        querySql<string> "select distinct id from channels" []
    let getUsersForChannel (channel : string) = 
        querySql<string> "select user from channels where id = '{0}'" 
            [ channel ]
    let getOffsetWith (id : string) = 
        querySql<TelegramOffset> "select ts from offsets where id = '{0}'" 
            [ id ] |> List.tryHead
    let setOffsetWith (id : string) (o : TelegramOffset) = 
        execute 
            "delete from offsets where id = '{0}'; insert into offsets (id, ts) values ('{0}', '{1}')" 
            [ id; o ]