﻿/// This module contains the ClientHandler class that is used for
/// connection with an individual elevator. Each elevator connecting
/// to the dispatcher is immediately assigned a unique ClientHandler.
/// Once the connection is lost, the appropriate ClientHandler is
/// disposed.
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Elev.Formats;

namespace Elev.Dispatcher
{
    internal class StatusReceivedEventArgs : EventArgs
    {
        /// <summary/>
        /// <param name="h"></param>
        /// <param name="newStatus"></param>
        /// <param name="orders">List of unserved orders to be copied</param>
        public StatusReceivedEventArgs(State newStatus, List<Order> orders)
        {
            NewStatus = newStatus;
            Time = DateTime.Now;

            var temp = new Order[orders.Count];
            orders.CopyTo(temp);
            UnservedOrders = temp;
        }
        public DateTime Time
        { get; }
        public State NewStatus
        { get; }
        public Order[] UnservedOrders
        { get; }
    }
    internal class OrderReceivedEventArgs : EventArgs
    {
        public OrderReceivedEventArgs(Order ord)
        {
            OrderInfo = ord;
            Time = DateTime.Now;
        }
        public DateTime Time
        { get; }
        public Order OrderInfo
        { get; }
    }
    internal class LostConnectionEventArgs : EventArgs
    {
        public LostConnectionEventArgs(List<Order> orders)
        {
            Time = DateTime.Now;
            var temp = new Order[orders.Count];
            orders.CopyTo(temp);
            UnservedOrders = temp;
        }
        public DateTime Time
        { get; }
        public Order[] UnservedOrders
        { get; }
    }
    internal class ServedReceivedEventArgs : EventArgs
    {
        public ServedReceivedEventArgs(Order servedOrder)
        {
            Time = DateTime.Now;
            ServedOrder = servedOrder;
        }
        public Order ServedOrder
        { get; }
        public DateTime Time
        { get; }
    }
    internal class StatusUnknownException : Exception
    {
        public StatusUnknownException(string message) : base(message)
        { }
    }

    internal class ClientHandler
    {
        public event Action<ServedReceivedEventArgs, ClientHandler> ServedReceived;
        public event Action<StatusReceivedEventArgs, ClientHandler> StatusReceived;
        public event Action<OrderReceivedEventArgs, ClientHandler> OrderReceived;  
        public event Action<LostConnectionEventArgs, ClientHandler> ConnectionLost;

        /// <summary>
        /// Creates a ClientHandler that will use the given socket
        /// </summary>
        public ClientHandler(Socket sock)
        {
            var stream = new NetworkStream(sock);
            m_serlzr = new NetSerializer<Datagram>(new NetworkStream(sock), new object());
            m_alive = true;
            m_status = new State(Direction.Stop, -1);
            m_orders = new List<Order>();
        }
        /// <summary>
        /// Copy of the list of unserved orders
        /// </summary>
        public Order[] UnservedOrders
        {
            get
            {
                var temp = new Order[m_orders.Count];
                m_orders.CopyTo(temp);
                return temp;
            }
        }
        /// <summary>
        /// The elevators last known status. 
        /// Throws InvalidOperationException if not received yet or if EmergencyStop'ed
        /// </summary>
        public State Status
        {
            get
            {
                if (m_status.lastFloor == -1)
                    throw new StatusUnknownException("Status not available");
                return m_status;
            }
        }
        /// <summary>
        /// Receives data from the elevator and processes it asynchronously
        /// </summary>
        public void Run()
        {
            Task.Run(() => MonitorConnection());
            try
            {
                while (m_alive)
                {
                    Datagram dgram = m_serlzr.ExtractFromStream();
                    ProcessData(dgram);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Stop();
                ConnectionLost(new LostConnectionEventArgs(m_orders), this);
            }
        }
        /// <summary>
        /// Closes the tcp-connection with the elevator
        /// </summary>
        public void Stop()
        {
            try
            {
                m_status = new State(Direction.Stop, -1);
                m_alive = false;
                m_serlzr.TcpStream.Close();
            }
            catch { }
        }
        /// <summary>
        /// Send a received order to the elevator, so that it will be
        /// able to react appropriately (button lights etc)
        /// </summary>
        public void SendOrder(Order order)
        {
            SendData(Datagram.CreateOrder(order));
        }
        /// <summary>
        /// Send a served order to the elevator, so that it will be
        /// able to react appropriately (button lights etc)
        /// </summary>
        public void SendServed(Order served)
        {
            while (m_orders.Remove(served))
                ;
            SendData(Datagram.CreateServed(served));
        }
        /// <summary>
        /// Send an order that should be served by this elevator
        /// </summary>
        public void SendToServe(Order toServe)
        {
            m_orders.Add(toServe);
            SendData(Datagram.CreateToServe(toServe));
        }

        private void MonitorConnection()
        {
            while (m_alive)
            {
                SendData(Datagram.CreateDummy());
                Thread.Sleep(1000);
            }
        }
        private void SendData(Datagram data)
        {
            try
            {
                m_serlzr.WriteToStream(data);
            }
            catch(Exception e)
            {
                if (e is IOException || e is SocketException || e is ObjectDisposedException)
                {
                    Stop();
                    ConnectionLost(new LostConnectionEventArgs(m_orders), this);
                    Console.WriteLine(e.ToString());
                }
                else
                    throw;
            }
        }
        private void ProcessData(Datagram msg)
        {
            switch (msg.Type)
            {
                case Datagram.MsgType.Status:
                    State status = (State)msg.Content;
                    m_status = status;
                    StatusReceived(new StatusReceivedEventArgs((State)msg.Content, m_orders), this);
                    return;
                case Datagram.MsgType.Order:
                    OrderReceived(new OrderReceivedEventArgs((Order)msg.Content), this);
                    return;
                case Datagram.MsgType.Served:
                    ServedReceived(new ServedReceivedEventArgs((Order)msg.Content), this);
                    return;
                default:
                    return;
            }
        }

        NetSerializer<Datagram> m_serlzr;
        volatile bool m_alive;
        State m_status;
        List<Order> m_orders;
    }
}
