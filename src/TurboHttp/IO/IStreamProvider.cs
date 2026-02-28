using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Servus.Akka.IO;

public interface IStreamProvider
{
    EndPoint? RemoteEndPoint { get; }
    Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default);
    void Close();
}

public class TcpStreamProvider : IStreamProvider
{
    private readonly AddressFamily _addressFamily;
    private Socket? _socket;

    public TcpStreamProvider(TcpOptions options)
        : this(options.AddressFamily)
    {
    }

    public TcpStreamProvider(AddressFamily addressFamily)
    {
        _addressFamily = addressFamily;
    }

    public EndPoint? RemoteEndPoint => _socket?.RemoteEndPoint;

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _socket = CreateSocket();
        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new ArgumentException($"Could not resolve any IP addresses for host '{host}'.", nameof(host));
        }

        await _socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
        return new NetworkStream(_socket, ownsSocket: false);
    }

    public void Close()
    {
        if (_socket is null)
        {
            return;
        }

        try
        {
            _socket.Close();
            _socket.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // noop
        }
        finally
        {
            _socket = null;
        }
    }

    private Socket CreateSocket()
    {
        var result = new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            LingerState = new LingerOption(true, 0)
        };

        if (_addressFamily is AddressFamily.Unspecified)
        {
            result = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 2)
            };
        }

        result.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        if (_addressFamily is AddressFamily.InterNetworkV6)
        {
            result.DualMode = true;
        }

        return result;
    }
}

public class TlsStreamProvider : IStreamProvider
{
    private readonly TcpStreamProvider _tcpStreamProvider;
    private readonly TlsOptions _tlsOptions;
    private SslStream? _sslStream;

    public TlsStreamProvider(TlsOptions options)
    {
        _tcpStreamProvider = new TcpStreamProvider(options);
        _tlsOptions = options;
    }

    public EndPoint? RemoteEndPoint => _tcpStreamProvider.RemoteEndPoint;

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var networkStream = await _tcpStreamProvider.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            _tlsOptions.ServerCertificateValidationCallback
        );

        var targetHost = _tlsOptions.TargetHost ?? host;
        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = _tlsOptions.EnabledSslProtocols,
            ClientCertificates = _tlsOptions.ClientCertificates,
        };

        await _sslStream.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);

        return _sslStream!;
    }

    public void Close()
    {
        if (_sslStream is not null)
        {
            try
            {
                _sslStream.Close();
                _sslStream.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // noop
            }
            finally
            {
                _sslStream = null;
            }
        }

        _tcpStreamProvider.Close();
    }
}

public record TlsOptions : TcpOptions
{
    public string? TargetHost { get; set; }
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
}

public record TcpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int MaxFrameSize { get; init; } = 128 * 1024;
    public AddressFamily AddressFamily { get; set; } = AddressFamily.Unspecified;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnetInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; set; } = 10;
}