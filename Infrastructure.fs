namespace SlackToTelegram

module String = 
    let split (s : string) = s.Split(' ') |> Seq.toList

[<AutoOpen>]
module Operators = 
    let inline always a _ = a
    let inline uncurry f (a, b) = f a b
    let inline curry f a b = f (a, b)
    let inline (>>=) x f = async.Bind (x, f)
    let inline (>>-) x f = async.Bind (x, async.Return << f)
    let inline (>=>) f1 f2 x = f1 x >>= f2
    let inline (<*>) a1 a2 =
        async {
            let! x1 = a1
            let! x2 = a2
            return x1, x2
        }

module Async = 
    let map3 f o =
        async {
            match o with
            | Some x -> 
                let! r = f x
                return Some r
            | None -> return None
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

module Http = 
    open System.IO
    open System.Net.Http
    open Newtonsoft.Json
    
    let private httpClient = new HttpClient()
    
    let downloadJson<'a> (headers : (string * string) list) (url : string) = 
        async { 
            let req = new HttpRequestMessage(HttpMethod.Get, url)
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