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
    let map f a = async { let! r = a
                          return f r }
    let ignore x a = async { let! _ = a
                             return x }

module Infrastructure = 
    open System.IO
    open System.Net.Http
    open Newtonsoft.Json
    
    let private httpClient = new HttpClient()
    
    let download<'a> (url : string) = 
        let req = new HttpRequestMessage(HttpMethod.Get, url)
        req.Headers.Referrer <- Uri("https://kotlinlang.slackarchive.io/")
        let resp = httpClient.SendAsync(req).Result
        resp.Content.ReadAsStringAsync().Result
        |> StringReader
        |> JsonTextReader
        |> JsonSerializer().Deserialize<'a>
    
    let downloadAsync<'a> (url : string) = 
        async { 
            let req = new HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.Referrer <- Uri("https://kotlinlang.slackarchive.io/")
            let! resp = httpClient.SendAsync(req) |> Async.AwaitTask
            let! content = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
            return content
                   |> StringReader
                   |> JsonTextReader
                   |> JsonSerializer().Deserialize<'a>
        }

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