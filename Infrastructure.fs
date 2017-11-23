namespace SlackToTelegram

module String = 
    let split (s : string) = s.Split(' ') |> Seq.toList

module Async = 
    let map3 f o =
        async {
            match o with
            | Some x -> 
                let! r = f x
                return Some r
            | None -> return None
        }
    let mapOption f mAsync = 
        async { 
            let! ma = mAsync
            return ma
                   |> f
                   |> Option.get
        }
    
    let forAll f a = 
        async { 
            let! xs = a
            for x in xs do
                do! f x
        }

    let rec seq axs =
        async {
            match axs with
            | [] -> return []
            | ax :: axs ->
                let! rx = ax
                let! rxs = seq axs
                return rx :: rxs
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

module Option =
    let mapAsync f o =
        async {
            match o with
            | Some x -> return! f x |> Async.map Some
            | None -> return None
        }

module Http = 
    open System
    open System.IO
    open System.Net.Http
    open Newtonsoft.Json
    
    let private httpClient = new HttpClient()
    
    let downloadJson<'a> (headers : (string * string) list) (url : string) = 
        async { 
            let req = new HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.Referrer <- Uri("https://kotlinlang.slackarchive.io/")
            for k, v in headers do
                req.Headers.Add(k, v)
            let! resp = httpClient.SendAsync(req) |> Async.AwaitTask
            let! content = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
            use reader = new JsonTextReader(new StringReader(content))
            return reader |> JsonSerializer().Deserialize<'a>
        }
    
    let downloadString (headers : (string * string) list) (url : string) = 
        async { 
            let req = new HttpRequestMessage(HttpMethod.Get, url)
            for k, v in headers do
                req.Headers.Add(k, v)
            let! resp = httpClient.SendAsync(req) |> Async.AwaitTask
            return! resp.Content.ReadAsStringAsync() |> Async.AwaitTask
        }

module Infrastructure = 
    open System.Text.RegularExpressions
    
    let (|Regex|_|) pattern input = 
        let m = Regex.Match(input, pattern)
        if m.Success then 
            Some(List.tail [ for g in m.Groups -> g.Value ])
        else None
    
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
    
    let tryGet (dict : Dictionary<'k, 'v>) (key : 'k) = 
        let (success, value) = dict.TryGetValue(key)
        if (success) then Some value
        else None
    
    let nowUtc() = DateTime.UtcNow.Subtract(DateTime(1970, 1, 1)).TotalSeconds