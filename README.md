# DataPipe 2.0

## What
DataPipe is a small opinionated framework for easily implementing the vertical slice architectural style in .Net Core applications. You can find it on nuget as **SCB.DataPipe**

## Background
I've been developing vertical slice architectures since around 2013, mostly taking inspiration from the Pipe and Filters integration pattern and Unix pipes. I first tentatively blogged about it in [messaging as a programming model](https://stevebate.wordpress.com/2013/08/12/messaging-as-a-programming-model-part-1/) but since that time things have moved on a lot with many refinements based on learning and solving issues encountered in my day to day work with this model. 

DataPipe has been in constant use at a large multi-national warehouse and logistics enterprise (i.e. my employer) since then. Nearly everything we have developed over this period uses this approach, from APIs to domain services, and batch jobs, etc., and has resulted in a small team being able to achieve more than would otherwise possibly be expected, and more importantly, with high quality and few defects. Up to this point DataPipe has been an internal project with customizations specific to our needs but has now been stripped back to the core to make it generally applicable again. It's easy to use and fully supports the async/await model meaning you can also use any async apis you might need along with it.

## Why

Vertical slice architectures are an alternative to the common, traditional layered or hexagonal architectural styles found in the large majority of applications today. While these approaches are well understood, designing your application using vertical slices leads to these benefits:
* A smaller, flatter codebase
* Reduction in unnecessary abstractions and interfaces thus simplifying the application code
* Enhanced reasoning of how the codebase works due to locality and higher readability, maintainability, and testability
* Promotes evolutionary design without side-effects or fear of unintended consequences
* Simple to add common cross-cutting concerns that each slice can benefit from
* Scale - no matter the size of the team, the codebase remains consistent in both style and amount of complexity
* Fits perfectly with "Package by feature" solution structures resulting in high cohesion, modularity, and minimal coupling
* Similar to microservices, each vertical slice can use different technologies than the others

## How

Vertical slices rely on data coupling, which is generally regarded as the loosest possible form of coupling, or to put it another way, messaging. Just like the well-established Pipe and Filters integration pattern, Datapipe pushes a message through the pipeline giving each filter a chance to intercept and enhance or modify the message as it passes through. The difference is that DataPipe does it all in-process. All you need to do to use DataPipe is ensure your message descends from **BaseMessage** which implements some common functionality that the pipeline itself uses internally or exposes for you to use.

A pipeline is built from the steps (filters) needed to perform a request (the slice of your application) from start to finish, all of which are coupled to nothing other than the shape of the message and are themselves fully encapsulated. When a request is invoked, the message is pushed through the pipeline (by DataPipe) and each step is responsible for doing one thing against that message whether that be simple CRUD operations or invoking a domain model. When the request has finished, the message may have some data that is useful or required to return to the client.

The following is a real-world example from an internal API project. The message shape represents a request to create an entry in a calendar for a warehouse delivery booking system that needs to organize times and dates of pallet deliveries into a specific warehouse:

```csharp
public class CreateBookingMessage : AppContext
{
    public string SiteCode { get; init; }
    public string SiteName { get; init; }
    public string ClientCode { get; init; }
    public string ClientName { get; init; }
    public DateTime From { get; init; }
    public int Duration { get; init; }
    public int Pallets { get; init; }
    public string PalletType { get; init; }
    [JsonIgnore] public Out Outgoing { get; } = new();

    public class Out
    {
        public string BookingRef { get; set; }
    }
}
```
The details aren't that important here but note that first **AppContext** is just an extension of BaseMessage. It's a user-defined class, not part of DataPipe, and you can call it what you like. All it does is allow you to extend BaseMessage with properties applicable to your domain to hold values you might need to generate, for example, a MongoDB connection.  

Also note that this message shape defines an Out class to hold data that will be returned to the client. This isn't required either, it's just a convention I use to make it explicit to any other developers reading my code about which data is from the incoming request and that which will be used in any outgoing response.


In my **BookingController** class I inject a service:
```csharp
public BookingController(IBookingService service)
{
    _service = service;
}
```
which defines the vertical slice operations:
```csharp
public interface IBookingService
{
    Task Invoke(CreateBookingMessage msg);
    Task Invoke(AddPartsToBookingMessage msg);
}
```
The implementation on the controller of a request to create a booking looks like this:
```csharp
[Authorize(Roles = "EditUser")]
[HttpPost("Create")]
public async Task<ActionResult> Post(CreateBookingMessage msg)
{
    await _service.Invoke(msg);
    return new ObjectResult(new { msg.Outgoing.BookingRef, msg.StatusMessage, msg.StatusCode });

}
```
Upon completion of the pipeline's execution we return a status code, status message, and the generated booking reference. Of course, if there was an issue fulfilling this request, the booking reference would be empty, and the status code and status message would indicate the reason for failure as set somewhere in the pipeline.

So what would a vertical slice look like using DataPipe as defined in the BookingService implementation? Well, for an end-to-end synchronous request/response i.e. data in, data out, it will look something like this:
```csharp
public async Task Invoke(CreateBookingMessage msg)
{
    var pipe = new DataPipe<CreateBookingMessage>();
    pipe.Use(new OnErrorAspect<CreateBookingMessage>());
    pipe.Use(new SerilogAspect<CreateBookingMessage>());
    pipe.Use(new MongoDBAspect<CreateBookingMessage>(cns));
    pipe.Run(new ValidateRequestToCreateBooking());
    pipe.Run(new CheckRequestedBookingSlotIsFree());
    pipe.Run(new GetNextBookingReferenceNumber());
    pipe.Run(new CreateNewBooking());
    pipe.Run(new CreateUIProjectionData());
    pipe.Run(new CreateFatIntegrationEvent());
    pipe.Run(new SaveToDatabase());
    pipe.Run(new AuditRequest<CreateBookingMessage>());

    await pipe.Invoke(msg);
}
```
From a top level view of the code, this pipeline describes everything. No jumping around between layers to follow the flow, Everything needed to fulfill the request is defined here and hopefully is self-describing. 

On the other hand you could easily implement an asynchronous messaging pipeline. This next version does some basic validation upfront, sends a command via a message bus to be consumed out-of-process and returns a generated unique id that can be returned to the client, to allow them to poll for the status of the request to create a booking.
```csharp
public async Task Invoke(CreateBookingMessage msg)
{
    var pipe = new DataPipe<CreateBookingMessage>();
    pipe.Use(new OnErrorAspect<CreateBookingMessage>());
    pipe.Use(new SerilogAspect<CreateBookingMessage>());
    pipe.Use(new MongoDBAspect<CreateBookingMessage>(_mgoDb));
    pipe.Run(new ValidateRequestToCreateBooking());
    pipe.Run(new GeneareteTempIdToTrackStatus());
    pipe.Run(new SendToAzureServiceBus());
    
    await pipe.Invoke(msg);
}
```
 The Controller's implementation might look like this instead:
```csharp
[Authorize(Roles = "EditUser")]
[HttpPost("Create")]
public async Task<ActionResult> Post(CreateBookingMessage msg)
{
    await _service.Invoke(msg);
    return new ObjectResult(new { msg.Outgoing.Ref = "api/status/12345", msg.StatusMessage, msg.StatusCode // 202 Accepted });
}
```

Following a more RESTful approach, the returned link allows the consumer to query the status api until it gets back the generated booking reference on successful completion or, alternatively, notification that it has failed.

These are just a couple of examples of how one might use the DataPipe to implement a vertical slice architectural style.

## Cross-cutting Concerns

The examples above show the use of **Use** statements. These are analogous to the Use middleware statements in the Asp.Net Core configuration pipeline and are implemented in DataPipe as **Aspects**. These allow you to declare the cross-cutting concerns applicable to your pipeline (and remember each pipeline can be implemented differently). In the examples shown, there is a top-level exception handler which will return the appropriate status code and message, logging provided by Serilog, and a MongoDB connection. You'll most likely find that you have need for some Aspects that I haven't thought of. Fortunately, they are easy to write.

## Implementation

What does an Aspect look like? Take for example the Serilog implementation. The appSettings.json file might look like the following:
```json
{
  "Serilog": {
    "MinimumLevel": "Debug",
    "Override": {
      "Microsoft.AspNetCore": "Warning"
    },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "Seq", "Args": { "serverUrl": "http://seq:5341" } },
      {
        "Name": "File",
        "Args": {
          "path": "./logs/log.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```
Here the configuration is set to write to the console, the distributed logging service called Seq, and text files. The implementation for the SerilogAspect is quite trivial:
```csharp
public class SerilogAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
{
    public SerilogAspect()
    {
        // load the appsettings.json file
        var cb = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        // configure the logger
        _logger = new LoggerConfiguration()
            .ReadFrom.Configuration(cb)
            .CreateLogger();
    }

    public async Task Execute(T msg)
    {
        // hook into the BaseMessage's OnStep delegate
        msg.OnStep = WriteToDebugLog;

        msg.OnStep?.Invoke($"{DateTime.Now} - START - {msg.GetType().Name}");
        try
        {
            await Next.Execute(msg);
        }
        catch (Exception ex)
        {
            WriteToErrorLog(ex);
            throw;
        }
        finally
        {
            msg.OnStep?.Invoke($"{DateTime.Now} - END - {msg.GetType().Name}" + Environment.NewLine);
        }
    }

    public Aspect<T> Next { get; set; }

    private void WriteToDebugLog(string message)
    {
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(message);
        }
    }

    private void WriteToErrorLog(Exception ex)
    {
        if (_logger.IsEnabled(LogEventLevel.Error))
        {
            _logger.Error(ex.ToString());
        }

        if (_logger.IsEnabled(LogEventLevel.Fatal))
        {
            _logger.Fatal(ex.ToString());
        }
    }

    private ILogger _logger;
}
```
Similarly (and even simpler), the MongoDB Aspect is implemented as follows:
```csharp
public class MongoDBAspect<T> : Aspect<T>, Filter<T> where T : AppContext
{
    private readonly IMongoDatabase _db;

    public MongoDBAspect(IMongoDatabase db)
    {            
        _db = db;
    }

    public async Task Execute(T msg)
    {
        msg.Mongo = _db;
        await Next.Execute(msg);
    }

    public Aspect<T> Next { get; set; }
}
```
Reminder: AppContext is just a class descending from BaseMessage that I chose for these examples to hold useful properties and info that I need to make available through the pipeline's execution. As our message (CreateBookingMessage) descends from AppContext and AppContext defines the MongoDB connection, every filter in the pipeline can read from or write to the database.
```csharp
public class AppContext : BaseMessage
{
    public IMongoDatabase Mongo { get; set; }
}
```

## Filters
All Filters for your application start life the same way. You declare them to operate on your chosen message type:
```csharp
public class CheckRequestedBookingSlotIsFree : Filter<CreateBookingMessage>
{
    public async Task Execute(CreateBookingMessage msg)
    {
        msg.OnStep("log something here");
    }
}
```

As AppContext declares a MongoDB database property and we are using the MongoAspect to initialize it, any filter within the pipeline can use it. This filter shows how we might check to see if the time and date for a given warehouse is available.
```csharp
public class CheckRequestedBookingSlotIsFree : Filter<CreateBookingMessage>
{
    public async Task Execute(CreateBookingMessage msg)
    {
        var collection = msg.Mongo.GetCollection<BookingData>("bookings");
        var site = Builders<BookingData>.Filter.Eq("SiteCode", msg.SiteCode);
        var start = Builders<BookingData>.Filter.Gte("From", msg.From);
        var end = Builders<BookingData>.Filter.Lte("To", msg.From.AddMinutes(msg.Duration));
        var between = Builders<BookingData>.Filter.And(start, end);
        var notCancelled = Builders<BookingData>.Filter.Ne("Status", "Cancelled");
        var filter = Builders<BookingData>.Filter.And(site, notCancelled, between);

        var matches = await collection.CountDocumentsAsync(filter);

        if (matches > 0)
        {
            msg.StatusCode = 403;
            msg.StatusMessage = "Booking slot already reserved";
            msg.CancellationToken.Stop(msg.StatusMessage);
        }
    }
}
```

## Built-in Aspects

There are a number of out of the box. ready to use aspects that you can plug in to a DataPipe slice and immediately benefit from. These include:

* BasicLoggingAspect
* ForEachAspect
* IsFeatureEnabledAspect
* ~~RetryAspect~~ - Obsolete - See the **Composable Filters** section below
* ~~TransactionAspect~~
* WhileTrueAspect

All of these provide ways to implement more complex pipelines or easily implement sometimes more difficult functionality such as retry operations.

## Extensions

As custom Aspects (MongoDB, Sql Server, Serilog, etc.) are generally coupled to specific versions of the libraries they represent they are not included in the project. However, the **samples/Aspects** directory does contain some of those custom aspects that you could use either directly or as examples for your own.

The following example shows how simple it is to configure a pipeline to use a mix of these built-in and custom aspects and benefit easily from transaction support, configurable retries, sql server, and logging via Serilog.

```csharp
    var pipe = new DataPipe<CreateBookingMessage>();
    pipe.Use(new OnErrorAspect<CreateBookingMessage>());
    pipe.Use(new SerilogAspect<CreateBookingMessage>());
    pipe.Use(new TransactionAspect<CreateBookingMessage>());
    pipe.Use(new RetryAspect<CreateBookingMessage>(maxRetries: 3));
    pipe.Use(new SqlCommandAspect<CreateBookingMessage>());
    pipe.Run(new DoSomeDataImportStuff());
    pipe.Run(...etc)
    
    await pipe.Invoke(msg);
```

## Composable Filters

The big new feature of v2 of Datapipe is the ability to now compose filters inline which not only helps to make things explicit as the implementation is declared right in front of you as opposed to buried in a filter but also that certain operations such as transactions and retries can benefit from being scoped or localised to the filters they directly operate on instead of the whole pipeline which was the only way prior to v2. In other words, you can be far more granular and therefore sure of the intended effect on the pipeline. To this end the previous ```RetryAspect``` and ```TransactionAspect``` aspects have been deprecated and will be removed in a future release as the entirety of their functionality can be achieved much more easily with the ```OnTimeoutRetry``` and ```StartTransaction``` filters respectively.

The namespace ```DataPipe.Core.Filters``` contains the new additions that enable this new composable functionality and consists of the following out-of-the-box filters. 

```CancelPipeline```

```IfTrue```

```OnTimeoutRetry```

```Repeat```

```StartTransaction```

You are, of course, free to create your own by simply copying the way these are implemented. Check out the **samples/Filters** directory for two other useful implementations.

The following example pipeline loads orders from a table one at a time (SELECT TOP 1 *) into JSON files, updates the table to mark each one as processed i.e. loaded and written to disk, and then uploads the created files to an SFTP server. The actual implementation details are unimportant. The point here is to show how the new composable filters are constructed to provide a targeted scope for their capabilities.

```
public static async Task ProcessOrders(OrderMessage msg)
{
    msg.ProcessName = "ProcessOrders";
    msg.OnStart = (x) => Console.WriteLine("Invoking ProcessOrders");
    msg.OnSuccess = (x) => Console.WriteLine("ProcessOrders completed");
    msg.OnError = (x, y) => Console.WriteLine("ERROR - See Log for details");

    var pipe = new DataPipe<OrderMessage>();
    pipe.Use(new DatabaseLoggingAspect<OrderMessage>());
    pipe.Use(new SerilogAspect<OrderMessage>("OrdersLog"));
    pipe.Run(
        new OnTimeoutRetry<OrderMessage>(
            new OpenSqlConnection<OrderMessage>(
                new Repeat<OrderMessage>(
                    new LoadClientOrders(), // reads JSON and writes to disk
                    new MarkSpeedOrderAsProcessed()))));
    pipe.Run(
        new OnTimeoutRetry<OrderMessage>(
            new OpenSftpConnection<OrderMessage>(
                new UploadOrders())));

    await pipe.Invoke(msg);
}
```

As you can see ```OnTimeoutRetry``` wraps ```OpenSqlConnection``` which wraps the ```Repeat``` filter. This in turn, has the actual business logic of loading an order and marking it as processed in the same table. The Repeat filter will execute as a loop until there are no more orders to read. All of this takes place in the context of a single ```pipe.Run(...)``` outer filter.

Once the orders have been read and converted to files on disk, the next ```pipe.Run(...)``` filter starts another composition. This time, a new ```OnTimeoutRetry``` filter wraps the ```OpenSftpConnection``` filter which in turn contains the logic to upload the files on disk

In both cases the logic of actual work required by the business is served by the need for either a sql or sftp connection right at the point of need (rather than being open for the entire pipeline's lifetime) but also protected by network timeouts so that it can automaticaly retry a number of times (default 3) before continuing or giving up.

In this example the whole pipeline is wrapped by an Aspect to log (availble to every filter) using Serilog but also a top level exception handler which records uncaught errors and writes them to a table elsewhere.

## What Now?

Hopefully this has given you an idea of how to try using the vertical slice architectural style in your applications. 

## Building the Source

If you want to build the source, clone the repository, and open up the DataPipe.sln.

```csharp
git clone https://github.com/SteveBate/DataPipe.git
```

## Supported Platforms
DataPipe was written using .Net 5.0 and tested on Windows and Linux

## License
[MIT License](http://opensource.org/licenses/MIT)

