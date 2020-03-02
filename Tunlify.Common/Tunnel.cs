/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Tunlify.Common
{
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
            var incomingStream = incomingConnection.GetStream();

            Console.WriteLine("Connection accepted from " + Source);

            // Create outbound connection
            var outgoingConnection = new TcpClient();
            await outgoingConnection.ConnectAsync(Destination.Address, Destination.Port);
            var outgoingStream = outgoingConnection.GetStream();

            Console.WriteLine("Connection established with " + Destination);

            using var logFile = new FileStream("log.bin", FileMode.Create, FileAccess.Write, FileShare.Read);

            // Asynchronously forward the contents of a stream to a pipe
            async Task forwardStreamToPipeAsync(NetworkStream stream, Pipe pipe) {
                try {
                    int bytesRead;
                    do {
                        var buffer = pipe.Writer.GetMemory(65536);
                        bytesRead = await stream.ReadAsync(buffer);
                        pipe.Writer.Advance(bytesRead);
                    } while (!(await pipe.Writer.FlushAsync()).IsCompleted && bytesRead > 0);

                    Console.WriteLine("Stream exhausted");
                }

                // This exception occurs when we close the stream from outside the task
                catch (IOException ex) {
                    if (!(ex.InnerException is SocketException socketEx) || socketEx.ErrorCode != 995)
                        throw ex;
                    Console.WriteLine("Underlying socket was closed by us");
                }

                // Notify the pipe reader that there is no further data coming
                finally {
                    pipe.Writer.Complete();
                }
            }

            // Consume the contents of a pipe
            async Task pipeConsumer(Pipe pipe, NetworkStream dest) {
                ReadResult readResult;
                do {
                    readResult = await pipe.Reader.ReadAsync();
                    var block = readResult.Buffer.ToArray();

                    try {
                        await Task.WhenAll(dest.WriteAsync(block).AsTask(), logFile.WriteAsync(block).AsTask());
                    }

                    // ObjectDisposedException occurs when trying to write a stream that has been closed
                    catch (Exception ex) {
                        if (!(ex is ObjectDisposedException))
                            throw ex;
                    }

                    await logFile.FlushAsync();

                    pipe.Reader.AdvanceTo(readResult.Buffer.End);
                } while (!readResult.IsCompleted);

                Console.WriteLine("Pipe complete");
            }

            // Create a pipe for each side of the connection
            var sourcePipe = new Pipe();
            var destPipe = new Pipe();

            // Set up stream-to-pipe forwarders
            var srcToPipeTask = forwardStreamToPipeAsync(incomingStream, sourcePipe);
            var dstToPipeTask = forwardStreamToPipeAsync(outgoingStream, destPipe);

            // Set up pipe consumers
            var srcPipeConsumer = pipeConsumer(sourcePipe, outgoingStream);
            var dstPipeConsumer = pipeConsumer(destPipe, incomingStream);

            // Wait for either side of the connection to be closed, then close the other side
            var completed = await Task.WhenAny(srcToPipeTask, dstToPipeTask);

            Console.WriteLine("Connection was closed by the " + (completed == srcToPipeTask ? "client" : "server"));

            // Close both sides of the connection, causing IOException in the task thread
            // Check Connected property to avoid ObjectDisposedException because one of these will be closed already
            if (outgoingConnection.Connected)
                outgoingConnection.Close();
            if (incomingConnection.Connected)
                incomingConnection.Close();

            await Task.WhenAll(srcToPipeTask, dstToPipeTask);

            // Wait for both pipes to be emptied
            await Task.WhenAll(srcPipeConsumer, dstPipeConsumer);

            Console.WriteLine("Connection closed");
        }
    }
}
