using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using Elev.Formats;

namespace Server
{
    class ServerProgram
    {
        static void Main(string[] args)
        {
            NetworkStream tcpstream = null;
            try
            {
                int port = 2055;
                IPAddress ipAddress = Dns.GetHostEntry("localhost").AddressList[0];
                TcpListener listener = new TcpListener(ipAddress, port);
                listener.Start();
                Console.WriteLine("Listening...");
                Socket sock = listener.AcceptSocket();
                Console.WriteLine("Connected");

                tcpstream = new NetworkStream(sock);
                var serlzr = new NetSerializer<Datagram>(tcpstream, new object());

                Console.WriteLine("Receiving objects: ");
                while (true)
                {
                    Thread.Sleep(30);
                    var dg = serlzr.ExtractFromStream();
                    PrintDatagram(dg);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
                if (tcpstream != null) tcpstream.Close();
                Console.ReadKey();
            }
        }
        static void PrintDatagram(Datagram dg)
        {
            switch (dg.Type)
            {
                case Datagram.MsgType.Served:
                case Datagram.MsgType.ToServe:
                case Datagram.MsgType.Order:
                    Console.WriteLine("Type {0}, Order direction: {1}, Order destination: {2}",
                        dg.Type, ((Order)dg.Content).dirn, ((Order)dg.Content).destFloor);
                    return;
                case Datagram.MsgType.Status:
                    Console.WriteLine("Type {0}, Status direction: {1}, Status floor: {2}",
                        dg.Type, ((State)dg.Content).dirn, ((State)dg.Content).lastFloor);
                    return;
            }
        }
    }
}
