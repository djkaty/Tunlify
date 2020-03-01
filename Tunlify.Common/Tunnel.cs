/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Tunlify.Common
{
    public class Tunnel
    {
        public IPEndPoint Source { get; }
        public IPEndPoint Destination { get; }

        public Tunnel(IPEndPoint src, IPEndPoint dst) => (Source, Destination) = (src, dst);

        public async Task StartAsync() {

            var listeningSocket = new TcpListener(Source);
            listeningSocket.Start();

            var incomingConnection = await listeningSocket.AcceptTcpClientAsync();
            var incomingStream = incomingConnection.GetStream();

            Console.WriteLine("Connection accepted from " + Source);

            var outgoingConnection = new TcpClient();
            await outgoingConnection.ConnectAsync(Destination.Address, Destination.Port);

            Console.WriteLine("Connection established with " + Destination);

            var outgoingStream = outgoingConnection.GetStream();

            try {
                var srcToDstForwarderTask = incomingStream.CopyToAsync(outgoingStream);
                var dstToSrcForwarderTask = outgoingStream.CopyToAsync(incomingStream);
                await Task.WhenAll(srcToDstForwarderTask, dstToSrcForwarderTask);
            }
            catch (IOException ex) {
                // Don't generate an exception if one side terminated the connection
                if (!(ex.InnerException is SocketException socketEx) || socketEx.ErrorCode != 10053)
                    throw ex;
            }
            finally {
                outgoingConnection.Close();
                incomingConnection.Close();

                Console.WriteLine("Connection closed");
            }
        }
    }
}
