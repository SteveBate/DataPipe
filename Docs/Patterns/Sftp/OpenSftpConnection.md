# OpenSftpConnection Filter

This `OpenSftpConnection` filter is responsible for establishing and managing an SFTP connection. This example uses the `Renci.SshNet` library but you can always use another library if you prefer. It opens a connection to the specified SFTP server, executes a series of provided filters while the connection is open, and ensures that the connection is properly closed afterward. Any message wishing to use this filter must implement the `IUseSftp` interface, which includes a property for the `SftpClient`, an object representing the SFTP client in the `Renci.SshNet` library. The connection details are provided via a factory function that generates a `Credentials` object based on the message.

## Usage Example

The filter provides a hook by way of a factory function to create SFTP credentials. Depending on your authentication method, you can use username and password authentication:

```csharp
var credentialsFactory = () => new Credentials
{
    Host = "sftp.example.com",
    Port = 22,
    UserName = "sftpuser",
    AuthMethod = new PasswordAuthenticationMethod("sftpuser", "sftppassword")
};
```

or alternatively, you could use private key authentication:

```csharp
var credentialsFactory = () => new Credentials
{
    Host = "sftp.example.com",
    Port = 22,
    UserName = "sftpuser",
    AuthMethod = new PrivateKeyAuthenticationMethod("sftpuser", new PrivateKeyFile(@"\path\to\key.ssh"))
};
```

Adding the `OpenSftpConnection` filter to your DataPipe pipeline is straightforward. Below is an example of how to set up a DataPipe pipeline that uses the `OpenSftpConnection` filter along with some hypothetical `UploadFileFilter` and `DownloadFileFilter` filters and a callback to create the credentials. In the filters you would access the `SftpClient` via the message's `SftpClient` property to perform SFTP operations:

```csharp
var pipe = new DataPipe<TestMessage>();
pipe.Use(new ExceptionAspect<TestMessage>());
pipe.Add(
    new OpenSftpConnection<TestMessage>(msg => credentialsFactory(),
        new UploadFileFilter(),
        new ListFilesFilter(),
        new DownloadFileFilter()
    ));

public class TestMessage : BaseMessage, IUseSftp
{
    public SftpClient SftpClient { get; set; } = default!;
    // Additional properties related to SFTP operations
}
```

# Implementation

```csharp
// You can define other methods, fields, classes and namespaces here
public interface IUseSftp
{
    public SftpClient SftpClient { get; set; }
}

public class Credentials
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string UserName { get; set; }
    public AuthenticationMethod AuthMethod { get; set; }
}

public class OpenSftpConnection<T>(Func<T, Credentials> factory, params Filter<T>[] filters) : Filter<T>, IAmStructural where T : BaseMessage, IUseSftp
{
    public bool EmitTelemetryEvent => false;

    public async Task Execute(T msg)
    {
        // Track timing and outcome for this structural filter
        var structuralSw = Stopwatch.StartNew();
        var structuralOutcome = TelemetryOutcome.Success;
        var structuralReason = string.Empty;

        Credentials cr = factory(msg);
        var ci = new ConnectionInfo(cr.Host, cr.Port, cr.UserName, cr.AuthMethod);
        msg.SftpClient = new SftpClient(ci);
        
        msg.OnLog?.Invoke($"OPENING CONNECTION: {msg.SftpClient.ConnectionInfo.Host}");
        
        await msg.SftpClient.ConnectAsync(msg.CancellationToken);

        // Build start attributes including any annotations from parent
        var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
        {
            ["host"] = cr.Host,
            ["port"] = cr.Port
        };
        
        // Clear annotations after consuming them for Start event
        msg.Execution.TelemetryAnnotations.Clear();

        var @sftpStart = new TelemetryEvent
        {
            Actor = msg.Actor,
            Component = nameof(OpenSftpConnection<T>),
            PipelineName = msg.PipelineName,
            Service = msg.Service,
            Scope = TelemetryScope.Filter,
            Role = FilterRole.Structural,
            Phase = TelemetryPhase.Start,
            MessageId = msg.CorrelationId,
            Timestamp = DateTimeOffset.UtcNow,
            Attributes = startAttributes
        };
        if (msg.ShouldEmitTelemetry(@sftpStart)) msg.OnTelemetry?.Invoke(@sftpStart);

        try
        {
            await FilterRunner.ExecuteFiltersAsync(filters, msg, msg.PipelineName);
        }
        catch (Exception ex)
        {
            structuralOutcome = TelemetryOutcome.Exception;
            structuralReason = ex.Message;
            throw;
        }
        finally
        {
            if (msg.SftpClient != null && msg.SftpClient.IsConnected)
            {
                msg.SftpClient.Disconnect();
                msg.OnLog?.Invoke($"CLOSING CONNECTION: {msg.SftpClient.ConnectionInfo.Host}");
            }
            
            structuralSw.Stop();
            
            var @sftpEnd = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(OpenSftpConnection<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.End,
                MessageId = msg.CorrelationId,
                Outcome = structuralOutcome,
                Reason = structuralReason,
                Timestamp = DateTimeOffset.UtcNow,
                DurationMs = structuralSw.ElapsedMilliseconds,
            };
            if (msg.ShouldEmitTelemetry(@sftpEnd)) msg.OnTelemetry?.Invoke(@sftpEnd);
            
            // Clear any remaining annotations to prevent leaking
            msg.Execution.TelemetryAnnotations.Clear();
        }
    }
}
```