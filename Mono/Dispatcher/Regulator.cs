/// Main class for the Dispatcher
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Elev.Dispatcher;

namespace Dispatcher
{
    class Regulator
    {
        /// <summary>
        /// Determines mode and starts process-pair mechanisms
        /// </summary>
        /// <param name="args"> Command line arguments, indicate mode </param>
        /// <param name="form"> The actual window </param>
        public Regulator(string[] args, Form1 form)
        {
            m_started = false;
            m_form = form;
            if(args.Length == 1)
            {   // backup cmd parameters: <tcpport>
                m_disp = null;
                m_proc = new BackupProcessNotifier(Convert.ToInt32(args[0]));
                m_proc.ProcessDied += ((e) => 
                                       m_form.AddHistoryMsg(e.Time.ToLongTimeString() + ": Primary stopped working, turning primary")
                                       );
                m_proc.ProcessDied += TurnPrimary;
                m_form.InternSetMode(false);
            }
            else
            {   // No args: primary, default port 55555
                m_disp = new OrderDispatcher(false);
                m_disp.ElevNumberChanged += m_form.SetElevCount;
                m_disp.EventHappened += ((e) => m_form.AddHistoryMsg(e.GetInfoString()));

                m_proc = new PrimaryProcessNotifier();
                m_proc.ProcessDied += ((e) => m_form.AddHistoryMsg(e.Time.ToLongTimeString() + 
                                                                   ": Backup stopped working, restarting backup"));
                m_proc.ProcessDied += ((e) => m_proc.Start());
            }
            m_proc.Start();
        }
        /// <summary>
        /// Dispatcher object. Indicates whether we are primary or backup: is null for backup
        /// </summary>
        public OrderDispatcher Dispatcher
        {
            get { return m_disp; }
        }
        public void StartProgram()
        {
            if (m_disp != null && !m_started)
            {
                m_started = true;
                Task.Run(() => m_disp.Run());

                var host = Dns.GetHostEntry(Dns.GetHostName());
                string localIp=null;
                foreach (IPAddress ip in host.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        localIp = ip.ToString();

                Thread.Sleep(500);  // Wait to ensure that udp server is set up
                m_form.SetAddress(localIp, m_disp.LocalAddress.Port);
            }
        }
        public void StopProgram()
        {
            if (m_disp != null && m_started)
            {
                m_started = false;
                m_disp.Stop();
            }
        }
        private void TurnPrimary(ProcessDiedEventArgs evArg)
        {
            m_form.SetMode(true);

            m_disp = new OrderDispatcher(true);
            m_disp.ElevNumberChanged += m_form.SetElevCount;
            m_disp.EventHappened += ((e) => m_form.AddHistoryMsg(e.GetInfoString()));


            m_proc = new PrimaryProcessNotifier();
            m_proc.ProcessDied += ((e) => m_form.AddHistoryMsg(e.Time.ToLongTimeString() + ": Backup stopped working, restarting backup"));
            m_proc.ProcessDied += ((e) => m_proc.Start());

            StartProgram();
            m_proc.Start();
        }
        IProcessNotifier m_proc;
        OrderDispatcher m_disp;
        Form1 m_form;
        volatile bool m_started;
    }
}
