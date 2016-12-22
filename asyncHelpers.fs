namespace swrhimbot

  open System

  module AsyncHelpers = 
    // --------------------------------------------------------------------------------------
    // Helper to await IObservable (and properly unregister the event handler)
    // --------------------------------------------------------------------------------------

    /// Helper that can be used for writing CPS-style code that resumes
    /// on the same thread where the operation was started.
    let internal synchronize f = 
      let ctx = System.Threading.SynchronizationContext.Current 
      f (fun g ->
        let nctx = System.Threading.SynchronizationContext.Current 
        if ctx <> null && ctx <> nctx then ctx.Post((fun _ -> g()), null)
        else g() )

    type Microsoft.FSharp.Control.Async with 
      /// Creates an asynchronous workflow that will be resumed when the 
      /// specified observables produces a value. The workflow will return 
      /// the value produced by the observable.
      static member AwaitObservable(ev1:IObservable<'T1>) =
        synchronize (fun f ->
          Async.FromContinuations((fun (cont,econt,ccont) -> 
            let called = ref false
            let rec finish cont value = 
              remover.Dispose()
              f (fun () -> lock called (fun () -> 
                  if not called.Value then
                    cont value
                    called.Value <- true) )
            and remover : IDisposable = 
              ev1.Subscribe
                ({ new IObserver<_> with
                      member x.OnNext(v) = finish cont v
                      member x.OnError(e) = finish econt e
                      member x.OnCompleted() = 
                        let msg = "Cancelling the workflow, because the Observable awaited using AwaitObservable has completed."
                        finish ccont (new System.OperationCanceledException(msg)) }) 
            () )))

  type SchedulerAgent<'T>() = 
    let event = new Event<'T>()
    let error = new Event<_>()
    let agent = MailboxProcessor<DateTime * 'T>.Start(fun inbox -> 

      // We keep a list of events together with the DateTime when they should occur
      let rec loop events = async {

        // Report events that are happening now & forget them
        let events, current = 
          events |> List.partition (fun (time, e) -> time > DateTime.UtcNow)
        for _, e in current do 
          try event.Trigger(e) 
          with e -> error.Trigger(e)

        // Sleep until new events are added or until the first upcoming event
        let timeout = 
          if List.isEmpty events then System.Threading.Timeout.Infinite else 
            let t = int ((events |> List.map fst |> List.min) - DateTime.UtcNow).TotalMilliseconds
            max 10 t
        let! newEvents = inbox.TryReceive(timeout)
        let newEvents = match newEvents with Some v -> [v] | _ -> []
        return! loop (if List.isEmpty newEvents then events else events @ newEvents) }
      loop [])

    /// Schedule new events to happen in the future
    member x.AddEvent(event) = agent.Post(event)
    /// Triggered when an event happens
    member x.EventOccurred = event.Publish
    /// Exception has been thrown when triggering `EventOccurred`
    member x.ErrorOccurred = Event.merge agent.Error error.Publish
  
  module Observable =
    let private observable f = 
      { new IObservable<_> with
          member x.Subscribe(obs) = f obs }
    let replay (source:IObservable<_>) = 
      observable (fun obs ->
        let sched = SchedulerAgent()
        sched.EventOccurred.Add(obs.OnNext)
        sched.ErrorOccurred.Add(obs.OnError)
        { 
            new IObserver<_> with
              member x.OnCompleted() = obs.OnCompleted()
              member x.OnError(e) = obs.OnError(e)
              member x.OnNext(v) = sched.AddEvent(v) }
        |> source.Subscribe )