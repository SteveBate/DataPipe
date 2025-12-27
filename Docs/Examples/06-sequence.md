# Sequence

`Sequence<T>` groups multiple filters into a single logical step. It's not about parallelism — it's about expressing intent.

## Group related filters

```csharp
public async Task OnboardCustomer(Customer customer)
{
    var pipeline = new DataPipe<CustomerMessage>();
    
    pipeline.Add(new ValidateCustomerDetails());
    
    pipeline.Add(new Sequence<CustomerMessage>(
        new CreateCustomerRecord(),
        new GenerateAccountNumber(),
        new AssignDefaultPreferences()
    ));
    
    pipeline.Add(new Sequence<CustomerMessage>(
        new SendWelcomeEmail(),
        new ScheduleFollowUpCall(),
        new NotifySalesTeam()
    ));
    
    var message = new CustomerMessage { Customer = customer };
    await pipeline.Invoke(message);
}
```

## Why use Sequence?

- Groups related steps visually
- Makes intent clear ("these filters belong together")
- Reduces duplication when passing groups to `IfTrue<T>` or `Policy<T>`
- Still executes sequentially, top to bottom

It's a readability tool. The code documents that certain steps form a cohesive unit of work.
