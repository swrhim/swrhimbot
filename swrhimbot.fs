namespace swrhimbot

module setup = 

    open System
    open System.Text
    open System.Web
    open Microsoft.FSharp.Control
    open swrhimbot.AsyncHelpers
    open Suave                 // always open suave
    open Suave.Http
    open Suave.Filters
    open Suave.Operators
    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.Sockets.AsyncSocket
    open Suave.Successful      // for OK-result
    open Suave.Utils
    open Suave.Web             // for config
    open Suave.WebSocket
    open FSharp.Data

    //Types
    type SampleMessage = {
        metadata : string
        paylaod : string
    }

    let socketOfObservable (updates:IObservable<string>) (webSocket:WebSocket) cx = socket {
    while true do
        let! update = updates |> Async.AwaitObservable |> Suave.Sockets.SocketOp.ofAsync
        do! webSocket.send Text (Encoding.UTF8.GetBytes update) true 
    }

    //respond to slack here
    let respondToSlack = 
        

    let part =
        let root = IO.Path.Combine(__SOURCE_DIRECTORY__, "web")
        choose 
            [ 
            path "/" >=> handShake (socketOfObservable respondToSlack)
            Files.browse root ]

    let start = 
        let config =
            { 
                defaultConfig with 
                    logger = Logging.Loggers.saneDefaultsFor Logging.LogLevel.Verbose
                    bindings = [ HttpBinding.mkSimple Protocol.HTTP "127.0.0.1" 8083 ]
            }
        let start, run = startWebServerAsync config part
        let ct = new System.Threading.CancellationTokenSource()
        Async.Start(run, ct.Token)

    start
