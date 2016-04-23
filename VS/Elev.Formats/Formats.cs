/// This module is used by both dispatcher and elevators. It is important
/// that both programs use the same version of the same assembly, otherwise 
/// serialization will not work. This file is to be compiled into a DLL.
/// Both the dispatcher (master) and the elevator (slave) program use a copy 
/// of this DLL file. 
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Formatters;
using System.Threading;
using System.Net.Sockets;
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
    /// Struct representing an elevator's state
    /// </summary>
    [DataContract]
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
        [DataMember]
        public readonly Direction dirn;
        /// <summary>
        /// Last floor passed by the elevator. -1 if EmergencyStop'ed
        /// </summary>
        [DataMember]
        public readonly int lastFloor;
    }
    /// <summary>
    /// Struct containing information about an internal or external order
    /// </summary>
    [Serializable]
    [DataContract]
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
        [DataMember]
        public readonly Direction dirn;
        /// <summary>
        /// From where the order is received
        /// </summary>
        [DataMember]
        public readonly int destFloor;
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
    [KnownType(typeof(Order))]
    [KnownType(typeof(State))]
    [DataContract]
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
            ToServe,
            /// <summary> Checks connection, content is null </summary>
            Dummy
        }
        private Datagram(MsgType type, object content)
        {
            Type = type;
            Content = content;
        }
        /// <summary>
        /// Type of the message to be sent
        /// </summary>
        [DataMember]
        public MsgType Type
        { get; private set; }
        /// <summary>
        /// Message content
        /// </summary>
        [DataMember]
        public object Content
        { get; private set; }
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
        /// <param name="order">Floor that has now been served</param>
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
        /// <summary>
        /// Sent from dispatcher to elevator to check the connection. Necessary
        /// because Mono implementation of NetworkStream.Read() sucks
        /// </summary>
        /// <returns></returns>
        public static Datagram CreateDummy()
        {
            return new Datagram(MsgType.Dummy, null);
        }
    }
    /// <summary>
    /// Wrapper around NetworkStream that converts a specified datatype 
    /// to datagrams and sends via TCP. 
    /// </summary>
    /// <typeparam name="T"> Type to be serialized. Must be marked 
    /// with DataContractAttribute </typeparam>
    public class NetSerializer<T>
    {
        /// <summary>
        /// Creates NetSerializer that will read/write to the specified tcp stream
        /// </summary>
        public NetSerializer(NetworkStream tcpstream, object mutex)
        {
            m_mutex = mutex;
            m_xml = new DataContractSerializer(typeof(T));

            m_tcpstream = tcpstream;
            m_tcpreader = new StreamReader(m_tcpstream);
            m_tcpwriter = new StreamWriter(tcpstream);
            m_tcpwriter.AutoFlush = true;
        }
        /// <summary>
        /// The tcp stream we are writing to
        /// </summary>
        public NetworkStream TcpStream
        {
            get { return m_tcpstream; }
        }
        /// <summary>
        /// Sends an object to the tcp stream
        /// </summary>
        /// <param name="obj"> Object to be sent </param>
        public void WriteToStream(T obj)
        {
            SerializationException ex = null;
            lock (m_mutex)
            {
                for (int i = 0; i < 5; ++i) // max 5 attempts to serialize an object
                {
                    try
                    {
                        m_xml.WriteObject(m_tcpstream, obj);
                        m_tcpwriter.Write('\0');
                        return;
                    }
                    catch (SerializationException e) { ex = e; }
                    Console.WriteLine("OOPS! Serialization");
                }
            }
            Console.WriteLine("Failed to serialize object");
            throw ex;
        }
        /// <summary>
        /// Extracts one object from the stream, blocks when no objects available or
        /// throws IOException if ReadTimeOut is set
        /// </summary>
        public T ExtractFromStream()
        {
            SerializationException ex = null;
            string serializedObj = ReadXmlObject();
            for (int i = 0; i < 5; ++i) // max 5 attempts to deserialize
            {
                try
                {
                    using (var memstream = new MemoryStream(Encoding.UTF8.GetBytes(serializedObj)))
                    {
                        return (T)m_xml.ReadObject(memstream);
                    }
                }
                catch (SerializationException e) { ex = e; }
                Console.WriteLine("OOPS! Deserialization");
            }
            Console.WriteLine("Failed to deserialize: " + serializedObj);
            throw ex;
        }
        /// <summary> 
        /// Extracts first xml-object from the tcp stream, blocks
        /// </summary>
        private string ReadXmlObject()
        {
            string temp = "";
            int symbol;
            while ((symbol = m_tcpreader.Read()) != 0)
            {
                temp += (char)symbol;
            }
            return temp;
        }
        
        object m_mutex;
        DataContractSerializer m_xml;

        NetworkStream   m_tcpstream;
        StreamWriter    m_tcpwriter;
        StreamReader    m_tcpreader;
    }
    /// <summary>
    /// Maintains a binary log file
    /// </summary>
    public class LogWriter
    {
        /// <summary>
        /// Creates a log writer that writes to a file with the given name
        /// </summary>
        /// <param name="fileName">File name without extension</param>
        public LogWriter(string fileName)
        {
            m_logPath = fileName + ".bin";
            m_bin = new BinaryFormatter();
            m_bin.AssemblyFormat = FormatterAssemblyStyle.Simple;
        }
        /// <summary>
        /// Creates a log writer that writes to a file with the default name
        /// </summary>
        public LogWriter() : this("elevator_log")
        { }
        /// <summary>
        /// Appends one or more Order objects to the log file. Creates new log if 
        /// the log file does not exist
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
                using (FileStream file = File.Open(m_logPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    List<Order> alldata = new List<Order>();
                    while (file.Position < file.Length)
                        alldata.Add((Order)m_bin.Deserialize(file));
                    return alldata.ToArray();
                }
            }
            catch (FileNotFoundException)
            {
                return new Order[0];
            }
        }

        BinaryFormatter m_bin;
        string m_logPath;
    }
}
