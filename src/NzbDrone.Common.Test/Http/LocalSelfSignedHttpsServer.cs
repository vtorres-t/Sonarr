using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class LocalSelfSignedHttpsServer : IDisposable
{
    private TcpListener _listener;
    private X509Certificate2 _certificate;
    private CancellationTokenSource _cts;
    public int Port { get; private set; }

    public void Start()
    {
        // 1. Generamos un certificado autofirmado efímero directamente en memoria
        _certificate = CreateSelfSignedCertificate();

        // 2. Levantamos un listener TCP en un puerto libre aleatorio
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();

        // 3. Escuchamos peticiones en un hilo de fondo para no bloquear el test
        Task.Run(() => ListenAsync(_cts.Token));
    }

    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(tcpClient, token), token);
            }
        }
        catch
        { /* Ignorar excepciones al cerrar el servidor */
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())

        // SslStream se encarga de negociar el TLS de forma nativa multiplataforma
        using (var sslStream = new SslStream(stream, false))
        {
            try
            {
                // Forzamos el Handshake TLS usando nuestro certificado inválido
                await sslStream.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

                if (token.IsCancellationRequested) return;

                // Leemos los bytes de la petición HTTP entrante para limpiar el buffer
                byte[] buffer = new byte[1024];
                await sslStream.ReadAsync(buffer, 0, buffer.Length, token);

                // Respondemos con una estructura HTTP/1.1 cruda válida sobre TLS
                byte[] responseBytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");
                await sslStream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                await sslStream.FlushAsync(token);
            }
            catch { /* Ignorar fallos de handshake; son normales en el test que valida que falle */ }
        }
    }

    // Genera un certificado RSA rápido para los tests (no confiable para el OS)
    private X509Certificate2 CreateSelfSignedCertificate()
    {
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            // Exportar e importar es necesario en .NET (especialmente en Linux) para asociar correctamente la clave privada
            return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string)null, X509KeyStorageFlags.MachineKeySet);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _certificate?.Dispose();
    }
}
