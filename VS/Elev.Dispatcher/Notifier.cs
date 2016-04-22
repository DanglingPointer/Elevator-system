/// This module is used by the dispatcher in order to maintain the process pair mechanisms
using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace Elev.Dispatcher
{
    public class ProcessDiedEventArgs : EventArgs
    {
        /// <summary/>
        /// <param name="primary">The dead process was primary</param>
        public ProcessDiedEventArgs(bool primary)
        {
            IsPrimary = primary;
            Time = DateTime.Now;
        }
        /// <summary>
        /// True if the process that has died, was primary
        /// </summary>
        public bool IsPrimary
        { get; }
        public DateTime Time
        { get; }
        public static implicit operator ProcessDiedEventArgs(bool primary)
        {
            return new ProcessDiedEventArgs(primary);
        }
    }
    /// <summary>
    /// Process pair notifier. Maintains a tcp connection with a pair process
    /// on the same computer and notifies user when the connection is lost. 
    /// </summary>
    public interface IProcessNotifier
    {
        /// <summary>
        /// Fired when connection with the pair process is lost
        /// </summary>
        event Action<ProcessDiedEventArgs> ProcessDied;
        /// <summary>
        /// Connects with the second proccess, and monitors the connection.
        /// Runs asynchronously. Stops and closes socket if the connection is lost
        /// </summary>
        void Start();
        /// <summary>
        /// Breaks connection with the pair process, nothrow
        /// </summary>
        void Stop();
    }
    /// <summary>
    /// Starts backup process and maintains connection with it. Fires
    /// an event when the connection is lost or unable to connect
    /// </summary>
    public class PrimaryProcessNotifier : IProcessNotifier
    {
        public event Action<ProcessDiedEventArgs> ProcessDied;

        public PrimaryProcessNotifier()
        {
            m_tcpServer = null;
            m_socket = null;
        }
        /// <summary>
        /// Starts backup-process. Stops when the connection fails.
        /// Should be started again when PairDied is triggered
        /// </summary>
        public void Start()
        {
            m_tcpServer = new TcpListener(IPAddress.Any, 0);
            Task.Run(() =>
            {
                m_tcpServer.Start();
                IPEndPoint ep = (IPEndPoint)m_tcpServer.LocalEndpoint;
                
                Process.Start("Dispatcher.exe", ep.Port.ToString());

                m_socket = m_tcpServer.AcceptSocket();
                try
                {
                    using (var str = new NetworkStream(m_socket))
                    {
                        var sr = new StreamReader(str);
                        var sw = new StreamWriter(str);
                        while (true)
                        {
                            while (str.DataAvailable)
                                sr.ReadLine();
                            Thread.Sleep(30); // b/c of CPU performance issues
                            sw.WriteLine("Primary is alive");
                        }
                    }
                }
                catch (IOException)
                {
                    Stop();
                    ProcessDied(false);
                }
            });
        }
        public void Stop()
        {
            try
            {
                m_socket.Close();
                m_tcpServer.Stop();
            }
            catch { }
        }
        TcpListener m_tcpServer;
        Socket m_socket;
    }
    /// <summary>
    /// Connects to the primary process which is assumed to be already running
    /// Fires an event when the connection is lost (or if unable to connect)
    /// </summary>
    public class BackupProcessNotifier : IProcessNotifier
    {
        public event Action<ProcessDiedEventArgs> ProcessDied;

        public BackupProcessNotifier(int port)
        {
            m_port = port;
        }
        /// <summary>
        /// Connects to primary. 
        /// </summary>
        public void Start()
        {
            Task.Run(() =>
            {
                try
                {
                    m_tcpClient = new TcpClient("localhost", m_port);
                    using (var str = m_tcpClient.GetStream())
                    {
                        var sr = new StreamReader(str);
                        var sw = new StreamWriter(str);
                        while (true)
                        {
                            sw.WriteLine("Backup is alive");
                            Thread.Sleep(30); // b/c of CPU performance issues
                            while (str.DataAvailable)
                                sr.ReadLine();
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is SocketException || e is IOException)
                    {
                        Stop();
                        ProcessDied(true);
                    }
                    else
                        throw;
                }
            });
        }
        public void Stop()
        {
            try
            {
                m_tcpClient.Close();
            }
            catch { }
        }
        TcpClient m_tcpClient;
        int m_port;
    }
}
