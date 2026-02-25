## Example Pipeline Diagram

Below is a diagram represent the Order Processing Pipeline example described in the Basics section. It illustrates the flow of an `OrderMessage` through various filters and decision points within the pipeline.

```pgsql

        ┌──────────────────┐
        │  OrderMessage    │
        │  (initial state) │
        └───────┬──────────┘
                │
                ▼
    ┌────────────────────────────┐
    │ Exception + Logging Aspect │
    └───────────┬────────────────┘
                │
                ▼
    ┌────────────────────────────┐                    ┌────────────────────────────┐                    ┌─────────────────────────────┐ 
    │     Telemetry Aspect       │───────────────────>│      Telemetry Adapter     │───────────────────>│ OTel, Sql Server, File, etc │
    └───────────┬────────────────┘                    └────────────────────────────┘                    └─────────────────────────────┘
                │
                ▼                
    ┌────────────────────────────┐
    │ OnTimeoutRetry             │
    │ └─ DownloadOrderFromClient │
    │    └─ ValidateOrder        │
    └───────────┬────────────────┘
                │
                ▼
    ┌────────────────────────────┐
    │ Policy Decision            │
    ├───────────────┬────────────┤
    │ Invalid Order │ Valid      │
    └────┬───────────────────┬───┘
         │                   │
         ▼                   ▼
 ┌───────────────┐    ┌────────────────────────────┐
 │ MarkOrderAs   │    │ RequiresSpecialProcessing? │
 │ Failed        │    ├───────────────┬────────────┤
 └───────────────┘    │      Yes      │     No     │
                      └────────┬────────────┬──────┘
                               ▼            │                
                      RaiseNotification     │
                                            │
                                            ▼
                                ┌──────────────────────────────────┐
                                │ StartTransaction                 │
                                │ └─ OpenSqlConnection             │
                                │    └─ ProcessStandardOrder       │
                                │    └─ AddOrderCreatedOutboxEvent │
                                └────────-─────────────────────────┘

```