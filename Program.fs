open System
open System.Net.Http
open System.IO
open System.Reactive.Linq
open System.Threading
open Telegram.Bot
open Newtonsoft.Json

(*
http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C2X2LMYQ2&offset=0
http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C09222272&offset=0

#r "../../.nuget/packages/telegram.bot/10.4.0/lib/netstandard1.1/Telegram.Bot.dll"
#r "../../.nuget/packages/newtonsoft.json/9.0.1/lib/net45/Newtonsoft.Json.dll"
*)

type Message = { text: string; user: string; ts: double }
type Response = { messages: Message[] }

let download (url: string) = HttpClient().GetStringAsync(url).Result |> StringReader |> JsonTextReader
let nowUtc () = DateTime.UtcNow.Subtract(DateTime(1970, 1, 1)).TotalSeconds
let sendToTelegram token messages =
    let bot = TelegramBotClient(token)
    let chats =
        bot.GetUpdatesAsync().Result 
        |> Array.toList
        |> List.rev
        |> List.map (fun x -> x.Message) 
        |> List.takeWhile (fun x -> isNull x.LeftChatMember)
        |> List.map (fun x -> string x.Chat.Id) 
    for chat in chats do
        for m in messages do
            if m <> "" then bot.SendTextMessageAsync(chat, m).Result |> ignore
    ()

[<EntryPoint>]
let main argv =
    let token = argv.[0]
    Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30.))
        |> Observable.map (fun _ -> 
            "http://api.slackarchive.io/v1/messages?size=5&team=T09229ZC6&channel=C09222272&offset=0"
            |> download |> JsonSerializer().Deserialize<Response> 
            |> (fun x -> (Array.toList x.messages, x.messages.[0].ts)))
        |> Observable.scan (fun state (xs, stamp) -> 
            match state with
            | ([], 0.) -> (xs |> List.take 1, stamp)
            | (_, prevStamp) -> (xs |> List.filter (fun x -> x.ts > prevStamp), stamp)
            ) ([], 0.)
        |> Observable.map (fun (xs, _) -> xs |> Seq.toList |> List.rev |> List.map (fun x -> x.text))
        |> Observable.subscribe (fun xs -> 
            printfn "=== === === === === === (%O)" DateTime.Now
            for x in xs do printfn "New message = %O" x
            sendToTelegram token xs)
        |> ignore
    
    printfn "listening for slack updates..."
    Thread.Sleep(-1)
    0