
/*
    For Sftp connections using the Renci.SSHNet.Async nuget package, copy these two constructs to your project 
    and add pipe.Run(new OpenSftpConnection( ...inner filters go here... )) as a filter to your DataPipe pipeline.
    
    OpenSftpConnection<T> - creates, opens, and closes a connection to an sftp server.
    It lives for as long as needed to run the inner filters instantiated in its constructor.

    The inner filters can access the Sftp client because the message desecnds BaseMessage and MUST implement the
    ISupportSftp interface.
*/

public interface ISupportSftp
{
    SftpClient Client { get; set; }
}

internal class OpenSftpConnection<T> : Filter<T> where T : BaseMessage, ISupportStp
{
    private readonly Filter<T>[] _filters;

    public OpenSftpConnection(params Filter<T>[] filters)
    {
        _filters = filters;
    }

    public async Task Execute(T msg)
    {
        var ci = new ConnectionInfo(AppSettings.Instance.Sftp.Address, AppSettings.Instance.Sftp.Port, AppSettings.Instance.Sftp.UserName, new PasswordAuthenticationMethod(AppSettings.Instance.Sftp.UserName, AppSettings.Instance.Sftp.Password));
        msg.Client = new SftpClient(ci);
        msg.Client.Connect();
        msg.OnLog?.Invoke($"Connected to {msg.Client.ConnectionInfo.Host}");

        try
        {
            foreach (var f in _filters)
            {
                if (msg.CancellationToken.Stopped) break;

                await f.Execute(msg);
            }
        }
        finally
        {
            if (msg.Client != null && msg.Client.IsConnected)
            {
                msg?.Client.Disconnect();
                msg.OnLog?.Invoke($"Disconnected from {msg.Client.ConnectionInfo.Host}");
            }
        }
    }
}