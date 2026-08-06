using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

public class LocalSelfSignedHttpsServer : IDisposable
{
    private HttpListener _listener;
    public int Port { get; private set; }

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("https://127.0.0");
        _listener.Start();

        Port = new Uri(_listener.Prefixes.First()).Port;

        Task.Run(async () =>
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    using (var response = context.Response)
                    {
                        var buffer = System.Text.Encoding.UTF8.GetBytes("OK");
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer);
                    }
                }
            }
            catch
            { /* Ignorar excepciones al cerrar el listener */
            }
        });
    }

    public void Dispose()
    {
        _listener?.Stop();
        ((IDisposable)_listener)?.Dispose();
    }
}
