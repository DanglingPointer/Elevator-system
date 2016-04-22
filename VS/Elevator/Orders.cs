/// Mechanisms registering orders for an elevator 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elev.Formats;
using Elev.Connection;
using Elev.Movement;

namespace Elev.Orders
{
    public class ExternOrderEventArgs : EventArgs
    {
        public ExternOrderEventArgs(int destFloor, Direction dirn)
        {
            OrderInfo = new Order(dirn, destFloor);
        }
        public Order OrderInfo
        { get; }
    }
    public class InternOrderEventArgs : EventArgs
    {
        public InternOrderEventArgs(int destFloor)
        {
            OrderInfo = new Order(Direction.Stop, destFloor);
        }
        public Order OrderInfo
        { get; }
    }
    /// <summary>
    /// A class that checks all buttons and fires appropriate events asynchronously
    /// </summary>
    public class OrderRetriever
    {
        public event Action<ExternOrderEventArgs> ExternOrderReceived;
        public event Action<InternOrderEventArgs> InternOrderReceived;
        public event Action StopTriggered;
        public OrderRetriever()
        {
            m_alive = false;
            m_panel = Elevator.GetPanel();
            m_prevState = new bool[Elevator.ButtonCount, Elevator.FloorCount];
        }
        /// <summary>
        /// Starts checking buttons. Executes asynchronously
        /// </summary>
        public void Start()
        {
            m_alive = true;
            Task.Run(() =>
            {
                Console.WriteLine("Order retriever started");
                while (m_alive)
                {
                    bool stopState = m_panel.StopSignal;
                    if (stopState != m_prevStopState)
                    {
                        if (stopState == true)
                        {
                            var receivers = StopTriggered.GetInvocationList();
                            foreach (Action receiver in receivers)
                                receiver.BeginInvoke(null, null);
                        }
                        m_prevStopState = stopState;
                    }

                    for (m_floor = 0; m_floor < Elevator.FloorCount; ++m_floor)
                        for (m_button = 0; m_button < Elevator.ButtonCount; ++m_button)
                        {
                            bool curState = m_panel.ButtonSignal[m_button, m_floor];
                            if (curState != m_prevState[m_button, m_floor])
                            {
                                if (curState == true && m_panel.ButtonLamp[m_button, m_floor] == false)
                                {
                                    if (m_button == (int)Button.Command)
                                    {
                                        var receivers = InternOrderReceived.GetInvocationList();
                                        foreach(Action<InternOrderEventArgs> receiver in receivers)
                                        {
                                            receiver.BeginInvoke(
                                                new InternOrderEventArgs(m_floor), null, null
                                                );
                                        }
                                    }
                                    else
                                    {
                                        var receivers = ExternOrderReceived.GetInvocationList();
                                        foreach(Action<ExternOrderEventArgs> receiver in receivers)
                                        {
                                            receiver.BeginInvoke(new ExternOrderEventArgs(m_floor,
                                                (m_button == (int)Button.Up) ? Direction.Up : Direction.Down),
                                                null, null
                                                );
                                        }
                                    }
                                }
                                m_prevState[m_button, m_floor] = curState;
                            }
                        }
                }
            });
        }
        /// <summary>
        /// Stops checking buttons
        /// </summary>
        public void Stop()
        {
            m_alive = false;
        }

        volatile int m_floor;
        volatile int m_button;
        volatile bool m_alive;
        IPanel  m_panel;
        bool[,] m_prevState;
        bool m_prevStopState;
    }

    /// <summary>
    /// A class that connects the different modules with each other using even subscription,
    /// and also includes mechanisms for finding the next order to serve
    /// </summary>
    public class OrderManager
    {
        /// <summary>
        /// Arranges event subscriptions, but starts neither of the polling mechanisms
        /// </summary>
        public OrderManager(WebClient client, OrderRetriever retriever, MotionController mover)
        {
            m_mover = mover;
            m_client = client;
            m_externalOrders = new List<Order>();
            m_internalOrders = new List<Order>();
            m_nextOrder = null;
            m_panel = Elevator.GetPanel();
            m_serlzr = new LogWriter("elev_log");

            m_internalOrders.AddRange(m_serlzr.GetOrderData());
            foreach (Order ord in m_internalOrders)
                m_panel.ButtonLamp[Button.Command, ord.destFloor] = true;
            UpdateNextOrder();

            client.ConnectionLost += Client_ConnectionLost;
            client.OrderReceived += Client_OrderReceived;
            client.ToServeReceived += Client_ToServeReceived;
            client.ServedReceived += Client_ServedReceived;

            mover.PassedByFloor += Mover_PassedByFloor;
            mover.EmergencyStopped += Mover_EmergencyStopped;
            mover.ArrivedAtFloor += Mover_ArrivedAtFloor;

            retriever.ExternOrderReceived += Retriever_ExternOrderReceived;
            retriever.InternOrderReceived += Retriever_InternOrderReceived;
            retriever.StopTriggered += Retriever_StopTriggered;

            Console.WriteLine("Order manager initilized successfully");
        }
        /// <summary>
        /// Is true if the order queue is not empty
        /// </summary>
        public bool OrdersAvailable
        {
            get { return m_nextOrder != null; }
        }
        /// <summary>
        /// Next order to be served
        /// </summary>
        public Order NextOrder
        {
            get
            {
                if (m_nextOrder == null)
                    throw new InvalidOperationException("No orders available");
                return (Order)m_nextOrder;
            }
        }

        private void Mover_ArrivedAtFloor(Order ord)
        {
            if (ord.dirn != Direction.Stop)
            {
                m_client.SendServed(ord);
                while (m_externalOrders.Remove(ord))
                    ;
                m_panel.ButtonLamp[ord.dirn == Direction.Up ? Button.Up : Button.Down, ord.destFloor] = false;
            }
            if (m_externalOrders.Remove(new Order(m_mover.LastDirection, ord.destFloor)))
            {
                while (m_externalOrders.Remove(new Order(m_mover.LastDirection, ord.destFloor)))
                    ;
                m_client.SendServed(new Order(m_mover.LastDirection, ord.destFloor));
                m_panel.ButtonLamp[m_mover.LastDirection == Direction.Up ? Button.Up :Button.Down, ord.destFloor] = false;
            }
            while (m_internalOrders.Remove(new Order(Direction.Stop, ord.destFloor)))
                ;
            m_panel.ButtonLamp[Button.Command, ord.destFloor] = false;
            UpdateNextOrder();
            m_serlzr.RewriteLog(m_internalOrders.ToArray());
        }
        private void Mover_EmergencyStopped(EmergencyStopEventArgs e)
        {
            m_client.SendStatus(new State(Direction.Stop, -1));
        }
        private void Mover_PassedByFloor(FloorEventArgs e)
        {
            m_panel.FloorIndicator = e.Floor;
            m_client.SendStatus(new State(m_mover.LastDirection, e.Floor));
        }

        private void Retriever_StopTriggered()
        {
            m_panel.StopLampOn();
            m_mover.EmergencyStop();
            m_internalOrders.Clear();
            m_externalOrders.Clear();
            UpdateNextOrder();
            for (int floor = 0; floor < Elevator.FloorCount; ++floor)
                m_panel.ButtonLamp[Button.Command, floor] = false;
            Thread.Sleep(1000);
            m_panel.StopLampOff();
        }
        private void Retriever_InternOrderReceived(InternOrderEventArgs e)
        {
            m_panel.ButtonLamp[Button.Command, e.OrderInfo.destFloor] = true;
            m_internalOrders.Add(e.OrderInfo);
            UpdateNextOrder();
            m_serlzr.WriteToLog(e.OrderInfo);
        }
        private void Retriever_ExternOrderReceived(ExternOrderEventArgs e)
        {
            try
            {
                m_client.SendOrder(e.OrderInfo);
            }
            catch (InvalidOperationException) { }
        }

        private void Client_ServedReceived(DataReceivedEventArgs e)
        {
            Order order = e.OrderInfo;
            m_panel.ButtonLamp[order.dirn == Direction.Up ? Button.Up : Button.Down, order.destFloor] = false;
            while (m_externalOrders.Remove(order))
                ;
            UpdateNextOrder();
        }
        private void Client_ToServeReceived(DataReceivedEventArgs e)
        {
            try
            {
                m_mover.CheckStopped();
                m_externalOrders.Add(e.OrderInfo);
                UpdateNextOrder();
                Console.WriteLine("Next order updated");
            }
            catch (ElevatorStoppedException) { }
        }
        private void Client_OrderReceived(DataReceivedEventArgs e)
        {
            Order order = e.OrderInfo;
            m_panel.ButtonLamp[order.dirn == Direction.Up ? Button.Up : Button.Down, order.destFloor] = true;
        }
        private void Client_ConnectionLost()
        {
            for (int floor = 0; floor < Elevator.FloorCount; ++floor)
                for (int button = 0; button < Elevator.ButtonCount - 1; ++button)
                    if (m_panel.ButtonLamp[button, floor] == true)
                        m_externalOrders.Add(new Order(button == 0 ? Direction.Up : Direction.Down, floor));
        }

        /// <summary>
        /// Updates next order to be served, should be called when new orders are received
        /// or old orders are served
        /// </summary>
        private void UpdateNextOrder()
        {
            if(m_externalOrders.Count==0 && m_internalOrders.Count== 0)
            {
                m_nextOrder = null;
                return;
            }
            try
            {
                int lastFloor = m_mover.LastFloor;
                Direction lastDirn = m_mover.LastDirection;

                var queue = new List<Order>(m_externalOrders);
                queue.AddRange(m_internalOrders);
                Console.WriteLine("Total number of orders: {0}", queue.Count);

                // All internal orders or orders in the same direction are suitable
                var suitables = new List<Order>();
                foreach (Order ord in queue)
                {
                    if (ord.dirn == Direction.Stop || (
                        ord.dirn == lastDirn &&
                        ((ord.dirn == Direction.Up && ord.destFloor > lastFloor) || (ord.dirn == Direction.Down && ord.destFloor < lastFloor))
                        ))
                        suitables.Add(ord);
                }
                // If no orders are suitable we look at all orders
                ICollection<Order> chosenOnes = suitables.Count == 0 ? queue : suitables;

                if (suitables.Count != 0)
                {
                    // Finding the closest one from the selection of orders
                    Order closest = chosenOnes.First();
                    int closestDist = Math.Abs(closest.destFloor - lastFloor);
                    foreach (Order ord in chosenOnes)
                    {
                        int dist = Math.Abs(ord.destFloor - lastFloor);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = ord;
                        }
                    }
                    Console.WriteLine("Next order chosen");
                    m_nextOrder = closest;
                }
                else
                {
                    // Finding the farthest one from the selection of orders
                    Order farthest = chosenOnes.First();
                    int farthestDist = Math.Abs(farthest.destFloor - lastFloor);
                    foreach (Order ord in chosenOnes)
                    {
                        int dist = Math.Abs(ord.destFloor - lastFloor);
                        if (dist > farthestDist)
                        {
                            farthestDist = dist;
                            farthest = ord;
                        }
                    }
                    Console.WriteLine("Next order chosen");
                    m_nextOrder = farthest;
                }
            }
            catch(InvalidOperationException)
            {
                m_nextOrder = null;
            }
        }

        volatile List<Order> m_internalOrders;
        volatile List<Order> m_externalOrders;
        volatile object m_nextOrder;
        MotionController m_mover;
        WebClient m_client;
        IPanel m_panel;
        LogWriter m_serlzr;
    }
}
