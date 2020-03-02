/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Tunlify.Common
{
    public class NetworkStreamEventArgs : EventArgs
    {
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Decorator class allowing data in a stream to be captured and dispatched
    /// </summary>
    public class ExtendedNetworkStream : Stream
    {
        private readonly NetworkStream underlyingStream;

        // Events that fire when bytes are read from or written to the stream
        public event EventHandler<NetworkStreamEventArgs> ReadBytes;
        public event EventHandler<NetworkStreamEventArgs> WriteBytes;

        internal ExtendedNetworkStream(NetworkStream stream) => underlyingStream = stream;

        #region Surrogate properties and methods
        public override void Flush() {
            underlyingStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            return underlyingStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) {
            return underlyingStream.Seek(offset, origin);
        }

        public override void SetLength(long value) {
            underlyingStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count) {
            underlyingStream.Write(buffer, offset, count);
        }

        public override bool CanRead => underlyingStream.CanRead;

        public override bool CanSeek => underlyingStream.CanSeek;

        public override bool CanWrite => underlyingStream.CanWrite;

        public override long Length => underlyingStream.Length;

        public override long Position {
            get => underlyingStream.Position;
            set => underlyingStream.Position = value;
        }
#endregion

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            var bytesRead = await underlyingStream.ReadAsync(buffer, offset, count, cancellationToken);

            ReadBytes?.Invoke(this, new NetworkStreamEventArgs { Data = buffer.Skip(offset).Take(bytesRead).ToArray() });
            return bytesRead;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            await underlyingStream.WriteAsync(buffer, offset, count, cancellationToken);

            WriteBytes?.Invoke(this, new NetworkStreamEventArgs {Data = buffer.Skip(offset).Take(count).ToArray() });
        }
    }

    /// <summary>
    /// Class encapsulating two TCP endpoints that forward to each other
    /// </summary>
    public class Tunnel
    {
        // The endpoint to listen on for incoming connections
        public IPEndPoint Source { get; }

        // The endpoint to forward to
        public IPEndPoint Destination { get; }

        public Tunnel(IPEndPoint src, IPEndPoint dst) => (Source, Destination) = (src, dst);

        // Start the tunnel
        // Throws socket error 10048 if unable to bind to TCP port
        public async Task StartAsync() {

            // Start listening on source
            var listeningSocket = new TcpListener(Source);
            listeningSocket.Start();

            // Wait for inbound connection
            var incomingConnection = await listeningSocket.AcceptTcpClientAsync();
            var incomingStream = new ExtendedNetworkStream(incomingConnection.GetStream());

            Console.WriteLine("Connection accepted from " + Source);

            // Create outbound connection
            var outgoingConnection = new TcpClient();
            await outgoingConnection.ConnectAsync(Destination.Address, Destination.Port);
            var outgoingStream = new ExtendedNetworkStream(outgoingConnection.GetStream());

            Console.WriteLine("Connection established with " + Destination);

            // Capture data to log file
            using var logFile = new FileStream("log.bin", FileMode.Create, FileAccess.Write, FileShare.Read);

            incomingStream.ReadBytes += async (s, e) => await logFile.WriteAsync(e.Data);
            outgoingStream.ReadBytes += async (s, e) => await logFile.WriteAsync(e.Data);

            // Start tunneling
            var srcToDstForwarderTask = incomingStream.CopyToAsync(outgoingStream);
            var dstToSrcForwarderTask = outgoingStream.CopyToAsync(incomingStream);

            try {
                // Wait for either side of the connection to be closed, then close the other side
                var completed = await Task.WhenAny(srcToDstForwarderTask, dstToSrcForwarderTask);

                Console.WriteLine("Connection was closed by the " + (completed == srcToDstForwarderTask ? "client" : "server"));
            }
            catch (IOException ex) {
                // Don't generate an exception if one side terminated the connection
                if (!(ex.InnerException is SocketException socketEx) || socketEx.ErrorCode != 10053)
                    throw ex;
                Console.WriteLine("Connection was terminated");
            }
            finally {
                // Close both sides of the connection
                // Check Connected property to avoid ObjectDisposedException because one of these will be closed already
                if (outgoingConnection.Connected)
                    outgoingConnection.Close();
                if (incomingConnection.Connected)
                    incomingConnection.Close();

                // One of these tasks will throw an exception because the transport is already closed
                try {
                    await Task.WhenAll(srcToDstForwarderTask, dstToSrcForwarderTask);
                }
                catch (IOException ex) {
                    if (!(ex.InnerException is SocketException socketEx) || socketEx.ErrorCode != 995)
                        throw ex;
                    Console.WriteLine("Streams closed");
                }

                Console.WriteLine("Connection closed");
            }
        }
    }
}
