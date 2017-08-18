namespace SlackToTelegram

open System

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