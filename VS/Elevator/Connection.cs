/// Connection module for an elevator
/// Maintains connection with the dispatcher
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Elev.Formats;

namespace Elev.Connection
{
    public class DataReceivedEventArgs : EventArgs
    {
        public DataReceivedEventArgs(Order order)
        {
            OrderInfo = order;
        }
        public Order OrderInfo
        { get; }
    }
    /// <summary>
    /// TCP-client that maintains connection with the dispatcher
    /// </summary>
    public class WebClient
    {
        public event Action<DataReceivedEventArgs> OrderReceived;
        public event Action<DataReceivedEventArgs> ServedReceived;
        public event Action<DataReceivedEventArgs> ToServeReceived;
        public event Action ConnectionLost;
        
        /// <summary>
        /// Saves address of the dispatcher and intializes variables
        /// </summary>
        public WebClient(string ipv4, int port)
        {
            m_dispatcherAddr = new IPEndPoint(IPAddress.Parse(ipv4), port);

            m_serlzr = null;
            m_client = null;
            m_unsent = new List<Datagram>();
            Running = false;

            Console.WriteLine("Web client initialized");
        }
        public bool Running
        {
            get; private set;
        }
        /// <summary>
        /// Connects to the dispatcher and then enters the main loop.
        /// While alive, checks incoming messages from dispatcher, and processes them asynchronously
        /// </summary>
        public void Run()
        {
            while (true)
            {
                try
                {
                    m_client = new TcpClient();
                    m_client.Connect(m_dispatcherAddr);
                    Running = true;
                    m_serlzr = new NetSerializer<Datagram>(m_client.GetStream(), new object());

                    Console.WriteLine("Web client started");

                    Datagram[] tempData = new Datagram[m_unsent.Count];
                    m_unsent.CopyTo(tempData);
                    foreach(Datagram data in tempData)
                        SendData(data);

                    m_unsent.Clear();

                    while (true)
                    {
                        Datagram data = m_serlzr.ExtractFromStream();
                        Task.Run(() => ProcessData(data));
                    }
                }
                catch (Exception)
                {
                    try { m_client.Close(); }
                    catch { }
                    ConnectionLost();
                    Thread.Sleep(1000);
                }
            }
        }
        /// <summary>
        /// Sends a received order to the dispatcher
        /// </summary>
        public void SendOrder(Order order)
        {
            if (Running)
            {
                SendData(Datagram.CreateOrder(order));
            }
            else
                throw new InvalidOperationException("Client is not started");
        }
        /// <summary>
        /// Sends status update to the dispatcher
        /// </summary>
        public void SendStatus(State status)
        {
            if (Running)
            {
                SendData(Datagram.CreateStatus(status));
            }
            else
                throw new InvalidOperationException("Client is not started");
        }
        /// <summary>
        /// Sends to the dispatcher an order that has just been served
        /// </summary>
        public void SendServed(Order served)
        {
            if (Running)
            {
                SendData(Datagram.CreateServed(served));
            }
        }

        private void SendData(Datagram data)
        {
            try
            {
                m_unsent.Add(data);
                m_serlzr.WriteToStream(data);
                m_unsent.Remove(data);
                Console.WriteLine("Data is sent");
            }
            catch (NullReferenceException) { }
            catch (Exception e)
            {
                if ((e is IOException || e is SocketException || e is ObjectDisposedException)
                    && Running)
                {
                    try { m_client.Close(); }
                    catch { }
                    ConnectionLost();
                }
                else
                    throw;
            }
        }
        private void ProcessData(Datagram msg)
        {
            switch (msg.Type)
            {
                case Datagram.MsgType.Order:
                    Console.WriteLine("Received: Order");
                    OrderReceived(new DataReceivedEventArgs((Order)msg.Content));
                    return;
                case Datagram.MsgType.Served:
                    Console.WriteLine("Received: Served");
                    ServedReceived(new DataReceivedEventArgs((Order)msg.Content));
                    return;
                case Datagram.MsgType.ToServe:
                    Console.WriteLine("Received: ToServe");
                    ToServeReceived(new DataReceivedEventArgs((Order)msg.Content));
                    return;
            }
        }

        readonly IPEndPoint m_dispatcherAddr;
        NetSerializer<Datagram> m_serlzr;
        volatile List<Datagram> m_unsent;
        volatile TcpClient m_client;
    }
}
