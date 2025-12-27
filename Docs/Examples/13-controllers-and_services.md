# Controllers and Services

DataPipe can be integrated simply into controller and service classes to handle complex processing workflows. Generally, a service class creates the pipelines that are then injected into controllers or other services. The following example demonstrates this pattern in a real-world ASP.NET Core Web API context.

## Service interface

```csharp
public interface IOrderService
{
    Task Invoke(ListOrdersMessage msg);
    Task Invoke(ViewOrderDetailsMessage msg);
    Task Invoke(CreateOrderMessage msg);
    Task Invoke(CancelOrderMessage msg);
    Task Invoke(DeleteOrderMessage msg);
    Task Invoke(UpdateOrderMessage msg);
}
```

## Service with injected pipeline

```csharp
public class OrderService(ILogger<OrderService> logger) : IOrderService
{
    public async Task Invoke(ListOrdersMessage msg)
    {
        var pipe = new DataPipe<ListOrdersMessage>() { DebugOn = true};
        pipe.Use(new LoggingAspect<ListOrdersMessage>(logger));
        pipe.Use(new OnErrorAspect<ListOrdersMessage>());
        
        pipe.Add(
            new ValidateListOrdersMessage(),
            new OpenSqlConnection<ListOrdersMessage>(AppSettings.Instance.Crm,
                new LoadListOfClientOrders()));

        await pipe.Invoke(msg);
    }

    public async Task Invoke(ViewOrderDetailsMessage msg)
    {
        var pipe = new DataPipe<ViewOrderDetailsMessage>();
        pipe.Use(new LoggingAspect<ViewOrderDetailsMessage>(logger));
        pipe.Use(new OnErrorAspect<ViewOrderDetailsMessage>());
        
        pipe.Add(                
            new ValidateViewOrderDetailsMessage(),
            new OpenSqlConnection<ViewOrderDetailsMessage>(AppSettings.Instance.Crm,
                new LoadOrderDetails(),
                new LoadHeldOrderLineReasons(),
                new SetResultStatus()));

        await pipe.Invoke(msg);
    }

    public async Task Invoke(CreateOrderMessage msg)
    {
        var pipe = new DataPipe<CreateOrderMessage>();
        pipe.Use(new LoggingAspect<CreateOrderMessage>(logger));
        pipe.Use(new OnErrorAspect<CreateOrderMessage>());

        pipe.Add(               
            new ValildateCreateOrderMessage(),
            new StartTransaction<CreateOrderMessage>(
                new OpenSqlConnection<CreateOrderMessage>(AppSettings.Instance.Crm,
                    new GetNextOrderNumber(),
                    new AddOrderHeader(),
                    new Policy<CreateOrderMessage>(m => m.AccountImportKind switch
                    {
                        AccountImport.AccountsOnly => new AddOrderAccounts(),
                        AccountImport.AddressesOnly => new AddOrderAddress(),
                        AccountImport.AccountsAndAddresses => new AddOrderAccountAndAddresses(),
                        _ => null
                    }),
                    new AddOrderLines(),
                    new SubmitOrder())));

        await pipe.Invoke(msg);
    }

    // Additional methods for CancelOrderMessage, DeleteOrderMessage, UpdateOrderMessage...
}
```

## Controller using the service

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListOrders([FromQuery] ListOrdersRequest request)
    {
        var msg = new ListOrdersMessage { Request = request };
        await orderService.Invoke(msg);
        return Ok(msg.Result);
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> ViewOrderDetails(string orderId)
    {
        var msg = new ViewOrderDetailsMessage { OrderId = orderId };
        await orderService.Invoke(msg);
        return Ok(msg.Result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var msg = new CreateOrderMessage { Request = request };
        await orderService.Invoke(msg);
        return CreatedAtAction(nameof(ViewOrderDetails), new { orderId = msg.Result.OrderId }, msg.Response);
    }

    // Additional endpoints for CancelOrder, DeleteOrder, UpdateOrder...
}
```


