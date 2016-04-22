using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Elev.Dispatcher;

namespace Dispatcher
{
    public partial class Form1 : Form
    {
        public Form1(string[] args)
        {
            InitializeComponent();
            m_cmdArgs = args;
        }
        /// <summary>
        /// Displays a new message below all previous in the list box
        /// </summary>
        public void AddHistoryMsg(string msg)
        {
            listBox.Invoke(new GuiAccessDelegate<string>(InternAddHistoryMsg), msg);
        }
        /// <summary>
        /// Changes the number of elevators displayed
        /// </summary>
        public void SetElevCount(int count)
        {
            countLabel.Invoke(new GuiAccessDelegate<int>(InternSetElevCount), count);
        }
        /// <summary>
        /// Changes the displayed mode (primary/backup)
        /// </summary>
        public void SetMode(bool primary)
        {
            modeLabel.Invoke(new GuiAccessDelegate<bool>(InternSetMode), primary);
        }
        /// <summary>
        /// Sets the displayed ip-address of the server (dispatcher)
        /// </summary>
        public void SetAddress(string ipAddr, int port)
        {
            ipAddrLabel.Invoke(new GuiAccessDelegate<string>(InternSetIpv4), ipAddr);
            ipAddrLabel.Invoke(new GuiAccessDelegate<int>(InternSetPort), port);
        }

        internal void InternAddHistoryMsg(string msg)
        {
            listBox.Items.Add(msg);
            int visibleItems = listBox.ClientSize.Height / listBox.ItemHeight;
            listBox.TopIndex = Math.Max(listBox.Items.Count - visibleItems + 1, 0);
        }
        private void InternSetElevCount(int count)
        {
            countLabel.Text = string.Format("Number of elevators: {0}", count);
        }
        internal void InternSetMode(bool primary)
        {
            modeLabel.Text = (primary) ? "Mode: Primary" : "Mode: Backup";
        }
        private void InternSetIpv4(string ipAddr)
        {
            ipAddrLabel.Text = "Dispatcher IP-address: " + ipAddr;
        }
        private void InternSetPort(int port)
        {
            portLabel.Text = string.Format("Dispatcher port: {0}", port);
        }
        private void stopButton_Click(object sender, EventArgs e)
        {
            m_reg.StopProgram();
            if (m_reg.Dispatcher == null)
                MessageBox.Show("Error: backup mode does not support any button commands", "Error Message");
        }
        private void startButton_Click(object sender, EventArgs e)
        {
            m_reg.StartProgram();
            if (m_reg.Dispatcher == null)
                MessageBox.Show("Error: backup mode does not support any button commands", "Error Message");
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            m_reg = new Regulator(m_cmdArgs, this);
        }
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            m_reg.StopProgram();
        }
        delegate void GuiAccessDelegate<T>(T arg);

        Regulator m_reg;
        string[] m_cmdArgs;
    }
}
