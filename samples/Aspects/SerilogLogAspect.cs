using System;
using System.Threading.Tasks;
using DataPipe.Core;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

/*
For Serilog logging, copy this class to your project and add SerilogAspect to your DataPipe pipeline.
Use the example configuration in appsettings.json

This example logs to Seq, the distributed logging library. Test this by downloading the Docker Seq image

"Serilog": {
    "MinimumLevel": "Debug",
    "Override": {
      "Microsoft.AspNetCore": "Warning"
    },
    "WriteTo": [
      { "Name": "Seq", "Args": { "serverUrl": "http://seq:5341" } },      
    ]
  }
*/

public class SerilogAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
{
    public SerilogAspect()
    {
        var cb = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        _logger = new LoggerConfiguration()
            .ReadFrom.Configuration(cb)
            .CreateLogger();
    }

    public async Task Execute(T msg)
    {
        msg.OnLog = WriteToDebugLog;

        msg.OnLog?.Invoke($"{DateTime.Now} - START - {msg.GetType().Name}");
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
            msg.OnLog?.Invoke($"{DateTime.Now} - END - {msg.GetType().Name}" + Environment.NewLine);
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