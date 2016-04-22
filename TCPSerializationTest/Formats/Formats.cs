﻿/// This module is used by both dispatcher and elevators. It is important
/// that both programs use the same version of the same assembly, otherwise 
/// serialization will not work. This file is to be compiled into a DLL.
/// Both the dispatcher (master) and the elevator (slave) program use a copy 
/// of this DLL file. 
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Formatters;
using System.Threading;
using System.Net.Sockets;
using System.Xml.Serialization;
using System.Runtime.Serialization;
namespace Elev.Formats
{
    public enum Direction
    {
        Down = -1,
        Stop = 0,
        Up = 1
    }
    /// <summary>
    /// Class representing an elevator's state
    /// </summary>
    public struct State
    {
        /// <summary/>
        /// <param name="dirn">Current direction of motion</param>
        /// <param name="lastFloor">Last floor passed by the elevator. -1 if EmergencyStop'ed</param>
        public State(Direction dirn, int lastFloor)
        {
            this.dirn = dirn;
            this.lastFloor = lastFloor;
        }
        /// <summary>
        /// Current direction of motion
        /// </summary>
        public Direction dirn;
        /// <summary>
        /// Last floor passed by the elevator. -1 if EmergencyStop'ed
        /// </summary>
        public int lastFloor;
    }
    /// <summary>
    /// Class containing information about an internal or external order
    /// </summary>
    [Serializable]
    public struct Order : IEquatable<Order>
    {
        /// <summary/>
        /// <param name="dirn"> Where the client wants to move </param>
        /// <param name="destFloor"> From where the order is received </param>
        public Order(Direction dirn, int destFloor)
        {
            this.dirn = dirn;
            this.destFloor = destFloor;
        }
        /// <summary>
        /// Where the client wants to move
        /// </summary>
        public Direction dirn;
        /// <summary>
        /// From where the order is received
        /// </summary>
        public int destFloor;
        public bool Equals(Order rhs)
        {
            return dirn == rhs.dirn && destFloor == rhs.destFloor;
        }
        public override bool Equals(object obj)
        {
            return Equals((Order)obj);
        }
    }
    /// <summary>
    /// Protocol of the data exchange between the Dispatcher and Elevators
    /// </summary>
    [XmlInclude(typeof(Order))]
    [XmlInclude(typeof(State))]
    public class Datagram
    {
        public enum MsgType : byte
        {
            /// <summary> Content is an object of type State </summary>
            Status,
            /// <summary> Content is an object of type Order </summary>
            Order,
            /// <summary> Content is Order (floor that has been served) </summary>
            Served,
            /// <summary> Content is order </summary>
            ToServe
        }
        /// <summary>
        /// Needed for the XML-serializer
        /// </summary>
        private Datagram()
        {
            Type = MsgType.Order;
            Content = null;
        }
        private Datagram(MsgType type, object content)
        {
            Type = type;
            Content = content;
        }
        /// <summary>
        /// Type of the message to be sent
        /// </summary>
        public MsgType Type
        { get; set; }
        /// <summary>
        /// Message content
        /// </summary>
        public object Content
        { get; set; }
        /// <summary>
        /// Order to be sent from an elevator when registered, and from
        /// the dispatcher to notify all elevators
        /// </summary>
        public static Datagram CreateOrder(Order o)
        {
            return new Datagram(MsgType.Order, o);
        }
        /// <summary>
        /// Status update to be sent from an elevator to the dispatcher
        /// </summary>
        public static Datagram CreateStatus(State s)
        {
            return new Datagram(MsgType.Status, s);
        }
        /// <summary>
        /// Order served message to be sent from either side in order
        /// to synchronize floor button lights and order queues
        /// </summary>
        /// <param name="order">Floor that has now been served </param>
        public static Datagram CreateServed(Order order)
        {
            return new Datagram(MsgType.Served, order);
        }
        /// <summary>
        /// Sent from dispatcher to elevator when an order is assigned to this elevator
        /// </summary>
        /// <param name="order">Order to be served by an elevator</param>
        public static Datagram CreateToServe(Order order)
        {
            return new Datagram(MsgType.ToServe, order);
        }
    }
    /// <summary>
    /// Converts appropriate datatypes to messages to be sent via TCP,
    /// and maintain a log file.
    /// </summary>
    public class DataSerializer
    {
        /// <summary>
        /// Creates DataSerializer that will read/write log to a binary file
        /// with a specified name
        /// </summary>
        public DataSerializer(NetworkStream tcpstream, string logPath)
        {
            m_logPath = logPath + ".bin";
            m_bin = new BinaryFormatter();
            m_bin.AssemblyFormat = FormatterAssemblyStyle.Simple;

            if (tcpstream != null)
            {
                m_tcpstream = tcpstream;
                m_tcpreader = new StreamReader(tcpstream);
                m_tcpwriter = new StreamWriter(tcpstream);
                m_tcpwriter.AutoFlush = true;

                m_memstream = new MemoryStream();
                m_memreader = new StreamReader(m_memstream);
                m_memwriter = new StreamWriter(m_memstream);
                m_memwriter.AutoFlush = true;

                m_xml = new XmlSerializer(typeof(Datagram));
            }
        }
        /// <summary>
        /// Creates DataSerializer that will read/write log to a binary file
        /// with the default name
        /// </summary>
        public DataSerializer(NetworkStream stream) : this(stream, "elevator_log")
        { }
        public NetworkStream TcpStream
        {
            get { return m_tcpstream; }
        }
        /// <summary>
        /// Sends a message to the tcp stream
        /// </summary>
        /// <param name="obj">message to be sent</param>
        public void WriteToStream(Datagram obj)
        {
            m_memstream.SetLength(0);
            m_xml.Serialize(m_memstream, obj);
            m_memwriter.Write('\0');
            m_memstream.Position = 0;

            string serializedObj = m_memreader.ReadToEnd();
            m_tcpwriter.Write(serializedObj);
        }
        /// <summary>
        /// Extracts one message from the stream
        /// </summary>
        public Datagram ExtractFromStream()
        {
            string serializedObj = ReadXmlObject();
            m_memstream.SetLength(0);
            m_memwriter.Write(serializedObj);
            m_memstream.Position = 0;
            return m_xml.Deserialize(m_memstream) as Datagram;
        }
        /// <summary> 
        /// Extracts first xml-object from the tcp stream
        /// </summary>
        private string ReadXmlObject()
        {
            string temp = "";
            int symbol;
            do
            {
                symbol = m_tcpreader.Read();
                if (symbol != 0)
                    temp += (char)symbol;
            } while (symbol != 0);
            return temp;
        }
        /// <summary>
        /// Writes one or more Order to the log file
        /// </summary>
        /// <param name="dataArr"> Data to be written into the log file </param>
        public void WriteToLog(params Order[] dataArr)
        {
            while (true)
            {
                try
                {
                    using (FileStream file = File.Open(m_logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        foreach (Order data in dataArr)
                            m_bin.Serialize(file, data);
                    }
                    return;
                }
                catch (IOException)
                {   // Wait and try again if someone else is writing to the log
                    Thread.Sleep(30);
                }
            }
        }
        /// <summary>
        /// Erases and rewrites the whole log file
        /// </summary>
        public void RewriteLog(Order[] orders)
        {
            while (true)
            {
                try
                {
                    using (FileStream file = File.Open(m_logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        foreach (Order data in orders)
                            m_bin.Serialize(file, data);
                    }
                    return;
                }
                catch (IOException)
                {   // Wait and try again if someone else is writing to the log
                    Thread.Sleep(30);
                }
            }
        }
        /// <summary>
        /// Read the whole log to an array of Orders
        /// </summary>
        /// <returns> Data read from the log, or an array of zero length
        /// if the log does not exist </returns>
        public Order[] GetOrderData()
        {
            try
            {
                List<Order> alldata = new List<Order>();
                using (FileStream file = File.Open(m_logPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (file.Position < file.Length)
                        alldata.Add((Order)m_bin.Deserialize(file));
                }
                return alldata.ToArray();
            }
            catch (FileNotFoundException)
            {
                return new Order[0];
            }
        }
        BinaryFormatter m_bin;
        string m_logPath;

        XmlSerializer m_xml;

        NetworkStream m_tcpstream;
        StreamReader m_tcpreader;
        StreamWriter m_tcpwriter;

        MemoryStream m_memstream;
        StreamReader m_memreader;
        StreamWriter m_memwriter;
    }
}
