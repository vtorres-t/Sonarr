using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class LocalSelfSignedHttpsServer : IDisposable
{
    private HttpListener _listener;
    public int Port { get; private set; }

    public void Start()
    {
        Port = GetFreeTcpPort();

        _listener = new HttpListener();

        var prefix = $"https://127.0.0.1:{Port}/";
        _listener.Prefixes.Add(prefix);

        _listener.Start();

        // Responder en bucle de fondo para no bloquear el hilo del test
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

    // Método auxiliar para obtener un puerto libre real antes de iniciar el Listener
    private int GetFreeTcpPort()
    {
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint).Port;
        }
    }

    public void Dispose()
    {
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
                ((IDisposable)_listener).Dispose();
            }
            catch
            { /* Ignorar fallos de limpieza en el desmantelamiento */
            }
        }
    }
}
