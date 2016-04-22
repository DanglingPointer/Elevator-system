/// The overall controller of an elevator
using System;
using System.Threading;
using System.Threading.Tasks;
using Elev.Formats;
using Elev.Connection;
using Elev.Movement;
using Elev.Orders;

namespace Elev
{
    public class Controller
    {
        /// <summary>
        /// Initilizes controller and elevator modules. 
        /// First command line argument is ipv4 address of the server, second argument is its port number
        /// </summary>
        /// <param name="cmdArgs">Command line arguments</param>
        public Controller(string[] cmdArgs)
        {
            m_alive = false;

            m_panel = Elevator.GetPanel();
            if (cmdArgs.Length == 2)
                m_client = new WebClient(cmdArgs[0], Convert.ToInt32(cmdArgs[1]));
            else
                m_client = new WebClient("127.0.0.1", 55555);
            
            m_mover = new MotionController();
            m_retriever = new OrderRetriever();
            m_manager = new OrderManager(m_client, m_retriever, m_mover);
        }
        /// <summary>
        /// Runs the elevator
        /// </summary>
        public void Run()
        {
            m_alive = true;
            Task.Run(() => m_client.Run());
            Task.Run(() => m_retriever.Run());
            
            Thread.Sleep(1000); // wait for the client to initialize
            m_client.SendStatus(new State(Direction.Stop, m_mover.LastFloor)); // send initial status
            Console.WriteLine("Controller started");
            bool stopStatusSent = false;
            while (m_alive || m_manager.OrdersAvailable)
            {
                if (m_manager.OrdersAvailable)
                {
                    m_mover.MoveToFloor(m_manager);
                    stopStatusSent = false;
                }
                else if (!stopStatusSent)
                {
                    m_client.SendStatus(new State(Direction.Stop, m_mover.LastFloor));
                    stopStatusSent = true;
                }
            }
        }
        IPanel m_panel;
        WebClient m_client;
        OrderRetriever m_retriever;
        OrderManager m_manager;
        MotionController m_mover;
        volatile bool m_alive;
    }
}