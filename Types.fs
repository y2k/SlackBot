namespace SlackToTelegram

open System

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

type SlackMessage = 
    { text : string
      user : string
      ts : string }

type TextMode = 
    | Plane
    | Styled

type DefaultErrorHandler() = 
    interface IObserver<unit> with
        member this.OnError(e) = printfn "Error = %O" e
        member this.OnNext(s) = printfn "Status OK (%O)" DateTime.Now
        member this.OnCompleted() = ()

module Observable = 
    open System.Reactive.Linq
    
    let flatMap (f : 'a -> IObservable<'b>) (o : IObservable<'a>) = 
        o.Select(f).Merge(3)
    
    let flatMapTask (f : 'a -> Async<'b>) (o : IObservable<'a>) = 
        let action x = f x |> Async.StartAsTask
        o.SelectMany action
    
    let ignore (o : IObservable<'a>) = o.Select(fun _ -> ())

module Async = 
    let forAll f a =
        async {
            let! xs = a
            for x in xs do
                do! f x
        }

    let bind f a = async { let! r = a
                           return! f r }
    let map f a = async { let! r = a
                          return f r }
    let map2 f a = async { let! (r1, r2) = a
                           return f r1 r2 }
    let ignore x a = async { let! _ = a
                             return x }
    let combine f a = async { let! r = a
                              let! x = f r
                              return x, r }
    let zip a2 a1 = async { let! r1 = a1
                            let! r2 = a2
                            return r2, r1 }

module Infrastructure = 
    open System.IO
    open System.Net.Http
    open Newtonsoft.Json
    
    let private httpClient = new HttpClient()
    
    let download<'a> (url : string) = 
        async { 
            let req = new HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.Referrer <- Uri("https://kotlinlang.slackarchive.io/")
            let! resp = httpClient.SendAsync(req) |> Async.AwaitTask
            let! content = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
            use reader = new JsonTextReader(new StringReader(content))
            return reader |> JsonSerializer().Deserialize<'a>
        }
    
    let loop action = 
        let rec loopInner() = 
            async { 
                try 
                    do! action()
                with e -> printfn "ERROR: %O" e
                do! Async.Sleep 30000
                do! loopInner()
            }
        loopInner() |> Async.RunSynchronously

module Utils = 
    open System
    open System.Collections.Generic
    open System.Reactive.Linq
    
    let tryGet (dict : Dictionary<'k, 'v>) (key : 'k) = 
        let (success, value) = dict.TryGetValue(key)
        if (success) then Some value
        else None
    
    let nowUtc() = DateTime.UtcNow.Subtract(DateTime(1970, 1, 1)).TotalSeconds
    let flatMap (f : 'a -> IObservable<'b>) (o : IObservable<'a>) = 
        o.Select(f).Merge(3)