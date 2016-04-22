/// Main logic for the dispatcher 
using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Elev.Formats;

namespace Elev.Dispatcher
{
    public class DispatcherEventArgs : EventArgs
    {
        public DispatcherEventArgs(string info)
        {
            EventInfo = info;
            Time = DateTime.Now;
        }
        public string EventInfo
        { get; }
        public DateTime Time
        { get; }
        public string GetInfoString()
        {
            return string.Format("{0}: {1}", Time.ToLongTimeString(), EventInfo);
        }
        public static implicit operator DispatcherEventArgs(string info)
        {
            return new DispatcherEventArgs(info);
        }
    }
    public class ElevCountEventArgs : EventArgs
    {
        public ElevCountEventArgs(int elevCount)
        {
            ElevCount = elevCount;
            Time = DateTime.Now;
        }
        public int ElevCount
        { get; }
        public DateTime Time
        { get; }
        public string GetInfoString()
        {
            return string.Format("{0}: New number of elevators is {1}", Time.ToLongTimeString(), ElevCount);
        }
        public static implicit operator ElevCountEventArgs(int count)
        {   // For convenience
            return new ElevCountEventArgs(count);
        }
    }
    /// <summary>
    /// This class includes an array of ClientHandlers according to the
    /// number of elevators connected, and coordinates their activity, 
    /// including assigning each order to the right elevator
    /// </summary>
    public class OrderDispatcher
    {
        public event Action<DispatcherEventArgs> EventHappened;
        public event Action<int> ElevNumberChanged;
        /// <summary>
        /// Creates order dispatcher. Orders from log will be read in and served
        /// if 'recoverng' is true
        /// </summary>
        public OrderDispatcher(bool recovering)
        {
            m_serlzr = new LogWriter();
            m_server = null;
            m_elevators = new List<ClientHandler>();
            m_alive = false;
            if (!recovering)
            {
                FileInfo oldLog = new FileInfo("elevator_log.bin");
                if (oldLog.Exists)
                    oldLog.Delete();
                m_logOrders = null;
            }
            else
            {
                m_logOrders = m_serlzr.GetOrderData();
            }
        }
        /// <summary>
        /// Number of the elevators connected to the dispatcher
        /// </summary>
        public int ElevatorCount
        {
            get { return m_elevators.Count; }
        }
        /// <summary>
        /// Local IP-address of the dispatcher
        /// </summary>
        public IPEndPoint LocalAddress
        {
            get { return ((m_server == null) ? null : (IPEndPoint)m_server.LocalEndpoint); }
        }
        /// <summary>
        /// Listens on the local port 55555 for connections and creates new
        /// ClientHandler's appropriately
        /// </summary>
        public void Run()
        {
            m_alive = true;

            m_server = new TcpListener(IPAddress.Any, 55555);
            m_server.Start();

            // If there is a local elevator we ensure that it is started when all other elevators have died
            FileInfo elevExe = new FileInfo("Elevator.exe");
            if (elevExe.Exists)
            {
                Task.Run(() =>
                {   // Checking every 5 sec that at least one elevator is alive
                    while (m_alive)
                    {
                        Thread.Sleep(5000);    // Checks every 5 sec
                        if (m_elevators.Count == 0)
                        {
                            Process.Start("Elevator.exe");
                            EventHappened("Local elevator started");
                        }
                    }
                });
            }

            EventHappened("Dispatcher started");

            try
            {
                while (m_alive)
                {
                    Socket sock = m_server.AcceptSocket(); // blocks

                    var handler = new ClientHandler(sock);
                    handler.ConnectionLost += OnConnectionLost;
                    handler.OrderReceived += OnOrderReceived;
                    handler.ServedReceived += OnServedReceived;
                    handler.StatusReceived += OnStatusReceived;
                    m_elevators.Add(handler);
                    Task.Run(() => handler.Run());

                    if (m_logOrders != null) 
                    {
                        Task.Run(() =>
                        {
                            foreach (Order ord in m_logOrders)
                                OnOrderReceived(new OrderReceivedEventArgs(ord), null);
                        });
                        m_logOrders = null;
                    }

                    EventHappened("Elevator connected");
                    ElevNumberChanged(m_elevators.Count);
                }
            }
            catch
            {
                foreach (ClientHandler handler in m_elevators)
                {
                    handler.Stop();
                }
                Stop();
            }
        }
        /// <summary>
        /// Stop listening for new connections
        /// </summary>
        public void Stop()
        {
            try
            {
                if (m_alive)
                    EventHappened("Dispatcher stopped");
                m_alive = false;
                m_server.Stop();
            }
            catch { }
        }

        internal void OnStatusReceived(StatusReceivedEventArgs e, ClientHandler h)
        {
            if (m_alive)
            {
                EventHappened("Status update received");
                if (e.NewStatus.lastFloor == -1)
                {
                    foreach (Order order in e.UnservedOrders)
                        OnOrderReceived(new OrderReceivedEventArgs(order), h);
                }
            }
        }
        internal void OnServedReceived(ServedReceivedEventArgs e, ClientHandler h)
        {
            if (m_alive)
            {
                EventHappened("Served order received");
                foreach (ClientHandler elev in m_elevators)
                {
                    elev.SendServedAsync(e.ServedOrder);
                }
                EventHappened(string.Format("Floor {0} served, sent to all", e.ServedOrder.destFloor));

                List<Order> allUnserved = new List<Order>();
                foreach(ClientHandler elev in m_elevators)
                {
                    allUnserved.AddRange(elev.UnservedOrders);
                }
                m_serlzr.RewriteLog(allUnserved.ToArray());
            }
        }
        internal async void OnOrderReceived(OrderReceivedEventArgs e, ClientHandler h)
        {
            if (m_alive)
            {
                EventHappened("Order received");
                ClientHandler handler=null;
                foreach (ClientHandler elev in m_elevators)
                {
                    elev.SendOrderAsync(e.OrderInfo);
                }
                m_serlzr.WriteToLog(e.OrderInfo);
                while (m_alive)
                {
                    try
                    {
                        handler = DispatchOrder(e.OrderInfo);
                        await handler.SendToServeAsync(e.OrderInfo);
                        EventHappened("Order dispatched");
                        return;
                    }
                    catch (NullReferenceException)
                    {   // Wait 3 seconds and try again if DispatchOrder throws
                        EventHappened("Order delayed");
                        Thread.Sleep(new TimeSpan(0, 0, 3));
                    }
                }
            }
        }
        internal void OnConnectionLost(LostConnectionEventArgs e, ClientHandler h)
        {
            if (m_alive)
            {
                h.Stop();
                bool found = m_elevators.Remove(h);

                if (found)
                {
                    EventHappened("Elevator disconnected");
                    ElevNumberChanged(ElevatorCount);
                    OnStatusReceived(
                        new StatusReceivedEventArgs(new State(Direction.Stop, -1), new List<Order>(e.UnservedOrders)), h
                        );
                }
            }
        }
        /// <summary>
        /// Chooses the right elevator for the order. Throws NullReferenceException if
        /// no elevators available, but never returns null
        /// </summary>
        /// <param name="ord"> Order to be dispatched </param>
        /// <returns> The elevator that should receive this order </returns>
        private ClientHandler DispatchOrder(Order ord)
        {
            var suitable = new List<ClientHandler>();
            foreach (ClientHandler elev in m_elevators)
            {
                try
                {
                    if (elev.Status.lastFloor == ord.destFloor && elev.Status.dirn == Direction.Stop)
                    {   // Idle and on the same floor
                        return elev;
                    }
                    else if (elev.Status.dirn == ord.dirn && (
                        (ord.dirn == Direction.Up && ord.destFloor > elev.Status.lastFloor) ||
                        (ord.dirn == Direction.Down && ord.destFloor < elev.Status.lastFloor)
                        ))
                    {   // Travelling in the same direction
                        suitable.Add(elev);
                    }
                }
                catch (StatusUnknownException)
                { } // ignore elevator if its state is not available
            }
            if (suitable.Count == 0)
            {
                foreach (ClientHandler elev in m_elevators)
                {   // Find idle elevators
                    try
                    {
                        if (elev.Status.dirn == Direction.Stop)
                            suitable.Add(elev);
                    }
                    catch (StatusUnknownException)
                    { } // ignore elevator if its state is not available
                }
            }
            // If no idle elevators, we use a collection of all possible elevators
            ICollection<ClientHandler> chosenOnes = (suitable.Count==0) ? m_elevators : suitable;

            if (suitable.Count != 0)
            {
                ClientHandler nearest = null;
                State dummy;
                foreach (ClientHandler elev in chosenOnes)
                {   // Choosing any elevator that is not EmergencyStop'ed as the initial choice
                    try
                    {
                        dummy = elev.Status; // throws if the state is not available
                        nearest = elev;
                        break;
                    }
                    catch (StatusUnknownException)
                    { } // ignore elevator if its state is not available
                }

                int minDist = Math.Abs(nearest.Status.lastFloor - ord.destFloor);   // throws if nearest == null
                foreach (ClientHandler elev in chosenOnes)
                {   // Nearest elevator to the destination
                    try
                    {
                        int dist = Math.Abs(ord.destFloor - elev.Status.lastFloor);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = elev;
                        }
                    }
                    catch (StatusUnknownException)
                    { } // ignore elevator if its state is unknown
                }
                return nearest;
            }
            else
            {
                ClientHandler farthest = null;
                State dummy;
                foreach (ClientHandler elev in chosenOnes)
                {   // Choosing any elevator that is not EmergencyStop'ed as the initial choice
                    try
                    {
                        dummy = elev.Status; // throws if the state is not available
                        farthest = elev;
                        break;
                    }
                    catch (StatusUnknownException)
                    { } // ignore elevator if its state is not available
                }

                int maxDist = Math.Abs(farthest.Status.lastFloor - ord.destFloor);   // throws if farthest == null
                foreach (ClientHandler elev in chosenOnes)
                {   // Nearest elevator to the destination
                    try
                    {
                        int dist = Math.Abs(ord.destFloor - elev.Status.lastFloor);
                        if (dist > maxDist)
                        {
                            maxDist = dist;
                            farthest = elev;
                        }
                    }
                    catch (StatusUnknownException)
                    { } // ignore elevator if its state is unknown
                }
                return farthest;
            }
        }

        LogWriter m_serlzr;
        TcpListener m_server;
        volatile List<ClientHandler> m_elevators;
        volatile bool m_alive;
        volatile Order[] m_logOrders;
    }
}
