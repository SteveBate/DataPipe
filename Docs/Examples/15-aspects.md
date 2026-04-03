# Aspects

Aspects wrap the entire pipeline to handle cross-cutting concerns like logging, exception handling, and instrumentation.

They keep your business logic clean.

## Apply concerns once

```csharp
public async Task ImportProducts(List<Product> products)
{
    var pipeline = new DataPipe<ProductImportMessage>();
    
    // Exception handling wraps everything
    pipeline.Use(new ExceptionAspect<ProductImportMessage>());
    
    // Logging aspect sits inside exception handling
    pipeline.Use(new BasicConsoleLoggingAspect<ProductImportMessage>("ProductImport"));
    
    // Business logic
    pipeline.Add(new ValidateProductData());
    pipeline.Add(new DeduplicateProducts());
    pipeline.Add(new SaveProductsToDatabase());
    
    var message = new ProductImportMessage { Products = products };
    await pipeline.Invoke(message);
}
```

## Key points

- `ExceptionAspect` should always be registered first - it wraps the entire pipeline to catch unhandled errors
- Logging and other aspects are registered after
- Aspects apply to all filters automatically
- No try/catch blocks in business logic
- No manual logging calls scattered through filters unless you choose to (e.g. msg.OnLog?.Invoke(...))

Aspects execute in the order they're registered, wrapping inward. The outermost aspect executes first on entry and last on exit.
