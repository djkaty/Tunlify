/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

            // Asynchronously forward the contents of a stream to a channel
            async Task forwardStreamToChannelAsync(NetworkStream stream, Channel<byte[]> channel) {
                var buffer = ArrayPool<byte>.Shared.Rent(65536);
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, 65536)) != 0) {
                    var block = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, block, 0, bytesRead);
                    await channel.Writer.WriteAsync(block);
                }
                ArrayPool<byte>.Shared.Return(buffer);

                Console.WriteLine("Stream exhausted");
            }

            // Consume the contents of a channel (the received data)
            async Task channelConsumer(Channel<byte[]> channel, NetworkStream dest) {
                await foreach (var block in channel.Reader.ReadAllAsync()) {
                    await Task.WhenAll(dest.WriteAsync(block).AsTask(), logFile.WriteAsync(block).AsTask());
                    await logFile.FlushAsync();
                }

                Console.WriteLine("Channel complete");
            }

            // Set channel options for performance optimization
            var channelOptions = new UnboundedChannelOptions();
            channelOptions.SingleWriter = channelOptions.SingleReader = true;
            channelOptions.AllowSynchronousContinuations = true;

            // Create a channel for each side of the connection
            var sourceChannel = Channel.CreateUnbounded<byte[]>(channelOptions);
            var destChannel = Channel.CreateUnbounded<byte[]>(channelOptions);

            try {
                // Set up stream-to-channel forwarders
                var srcToChannelTask = forwardStreamToChannelAsync(incomingStream, sourceChannel);
                var dstToChannelTask = forwardStreamToChannelAsync(outgoingStream, destChannel);

                // Set up channel consumers
                var srcChannelConsumer = channelConsumer(sourceChannel, outgoingStream);
                var dstChannelConsumer = channelConsumer(destChannel, incomingStream);

                // Wait for either side of the connection to be closed
                await Task.WhenAny(srcToChannelTask, dstToChannelTask);

                // Mark both channels complete
                sourceChannel.Writer.Complete();
                destChannel.Writer.Complete();

                // Wait for both channels to be emptied
                await Task.WhenAll(srcChannelConsumer, dstChannelConsumer);
            }
            catch (IOException ex) {
                // Don't generate an exception if one side terminated the connection
                if (!(ex.InnerException is SocketException socketEx) || socketEx.ErrorCode != 10053)
                    throw ex;
                else
                    Console.WriteLine("Connection was terminated");
            }
            finally {
                // Close both sides of the connection
                outgoingConnection.Close();
                incomingConnection.Close();

                Console.WriteLine("Connection closed");
            }
        }
    }
}
