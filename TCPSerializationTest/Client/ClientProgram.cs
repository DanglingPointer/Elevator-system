using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Net.Sockets;
using Elev.Formats;

namespace Client
{
    class ClientProgram
    {
        static void Main(string[] args)
        {
            NetworkStream stream=null;
            try
            {
                int port = 2055;
                var client = new TcpClient("localhost", port);

                var serlzr = new NetSerializer<Datagram>(client.GetStream(), new object());

                Console.WriteLine("Sending objects: ");
                for (int i = 0; i < 3; i++)
                {
                    var dg1 = Datagram.CreateOrder(new Order(Direction.Up, 2));
                    PrintDatagram(dg1);
                    serlzr.WriteToStream(dg1);

                    Thread.Sleep(500);

                    var dg2 = Datagram.CreateServed(new Order(Direction.Up, 2));
                    PrintDatagram(dg2);
                    serlzr.WriteToStream(dg2);

                    Thread.Sleep(500);

                    var dg3 = Datagram.CreateToServe(new Order(Direction.Up, 2));
                    PrintDatagram(dg3);
                    serlzr.WriteToStream(dg3);

                    Thread.Sleep(500);

                    var dg4 = Datagram.CreateStatus(new State(Direction.Up, 2));
                    PrintDatagram(dg4);
                    serlzr.WriteToStream(dg4);

                    Thread.Sleep(1000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                Console.ReadKey();
                if (stream != null) stream.Close();
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
