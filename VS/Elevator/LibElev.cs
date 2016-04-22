// Object-oriented driver controlling elevator and panel
// Methods are imported from the native shared library libelev.so
// Remember to copy 'libelev.so' to the output directory (e.g. bin/Release/)
// or to one of the directories specified by LD_LIBRARY_PATH
using System.ComponentModel;
using System.Runtime.InteropServices;
using Elev.Formats;
namespace Elev
{
// =============USER API========================================================================

    public enum Button
    {
        Up = 0,
        Down = 1,
        Command = 2
    }
    /// <summary> 
    /// Access to button lights. The interface is necessary to enforce singleton
    /// </summary>
    public interface ILampController
    {
        /// <summary> Set to 'true' in order to turn on a button light on a given floor </summary>
        /// <param name="button">Which type of lamp to set. Can be Button.Up,
        /// Button.Down or Button.Command (button "inside" the elevator).</param>
        /// <param name="floor">Floor of lamp to set. Must be 0-FloorCount.</param>
        bool this[Button button, int floor]
        { get; set; }
        /// <summary> Set to 'true' in order to turn on a button light on a given floor </summary>
        /// <param name="button">Which type of lamp to set. Must be 0-ButtonCount </param>
        /// <param name="floor">Floor of lamp to set. Must be 0-FloorCount </param>
        bool this[int button, int floor]
        { get; set; }
    }
    /// <summary> 
    /// Access to buttons. The interface is necessary to enforce singleton
    /// </summary>
    public interface IButtonMonitor
    {
        /// <summary> Checks if a specified button is pressed. </summary>
        /// <param name="button">Which button type to check. Can be Button.Up,
        /// Button.Down or Button.Command (button "inside" the elevator).</param>
        /// <param name="floor">Which floor to check button. Must be 0-3.</param>
        /// <returns> 'False' if button is not pushed. 'True' if button is pushed.</returns>
        bool this[Button button, int floor]
        { get; }
        /// <summary> Checks if a specified button is pressed. Throws
        /// InvalidEnumArgumentException if invalid button number passed. </summary>
        /// <param name="button">Which button type to check. Must be 0-ButtonCount </param>
        /// <param name="floor">Which floor to check button. Must be 0-3.</param>
        /// <returns> 'False' if button is not pushed. 'True' if button is pushed.</returns>
        bool this[int button, int floor]
        { get; }
    }
    /// <summary> Elevator car controller </summary>
    public interface ICar
    {
        /// <summary>
        /// Sets new motor direction. Can be up, down, or stop.
        /// </summary>
        Direction MotorDirection
        { get; set; }
        /// <summary>
        /// Returns -1 if elevator is not on a floor. 0-3 if elevator is on floor. 
        /// 0 is ground floor, 3 is top floor.
        /// </summary>
        int FloorSensorSignal
        { get; }
        /// <summary>
        /// Returns 'true' if obstruction is enabled
        /// </summary>
        bool ObstructionSignal
        { get; }
        /// <summary>
        /// Turns on door open lamp
        /// </summary>
        void DoorOpen();
        /// <summary>
        /// Turns off door open lamp
        /// </summary>
        void DoorClose();
    }
    /// <summary> Panel controller </summary>
    public interface IPanel
    {
        /// <summary>
        /// Object controlling button lamps
        /// </summary>
        ILampController ButtonLamp
        { get; }
        /// <summary>
        /// Object registering button signals
        /// </summary>
        IButtonMonitor ButtonSignal
        { get; }
        /// <summary>
        /// Which floor lamp to turn on. Other floor lamps are turned off.
        /// </summary>
        int FloorIndicator
        { get; set; }
        /// <summary>
        /// Returns 'true' if stop button is pushed
        /// </summary>
        bool StopSignal
        { get; }
        /// <summary>
        /// Turn on the stop lamp
        /// </summary>
        void StopLampOn();
        /// <summary>
        /// Turn off the stop lamp
        /// </summary>
        void StopLampOff();
    }
    /// <summary>
    /// Gives access to elevator car and panel through static methods. Non-static methods
    /// will never be called directly by user (only through interfaces).
    /// </summary>
    public class Elevator : ICar, IPanel
    {
        public const int FloorCount     = 4;
        public const int ButtonCount    = 3;
        /// <summary>
        /// Returns elevator car object
        /// </summary>
        public static ICar GetCar()
        {
            lock (s_mutex)
            {
                if (s_inst == null)
                    s_inst = new Elevator();
            }
            return s_inst;
        }
        /// <summary>
        /// Returns elevator panel control object
        /// </summary>
        public static IPanel GetPanel()
        {
            lock (s_mutex)
            {
                if (s_inst == null)
                    s_inst = new Elevator();
            }
            return s_inst;
        }
        
// ============USER API END=====================================================================

        private Elevator()
        {
            Init();
            m_buttonLamp = new LampController(ButtonCount, FloorCount);
            m_buttonSignal = new ButtonMonitor();
            MotorDirection = Direction.Stop;
            FloorIndicator = 0;
        }
        public Direction MotorDirection
        {
            get { return m_dirn; }
            set
            {
                SetMotorDirection((int)value);
                m_dirn = value;
            }
        }
        public ILampController ButtonLamp
        {
            get { return m_buttonLamp; }
        }
        public int FloorIndicator
        {
            get { return m_floorLamp; }
            set
            {
                SetFloorIndicator(value);
                m_floorLamp = value;
            }
        }
        public void DoorOpen()
        {
            SetDoorOpenLamp(1);
        }
        public void DoorClose()
        {
            SetDoorOpenLamp(0);
        }
        public void StopLampOn()
        {
            SetStopLamp(1);
        }
        public void StopLampOff()
        {
            SetStopLamp(0);
        }
        public IButtonMonitor ButtonSignal
        {
            get { return m_buttonSignal; }
        }
        public int FloorSensorSignal
        {
            get { return GetFloorSensorSignal(); }
        }
        public bool StopSignal
        {
            get { return (GetStopSignal() == 1) ? true : false; }
        }
        public bool ObstructionSignal
        {
            get { return (GetObstructionSignal() == 1) ? true : false; }
        }

        LampController      m_buttonLamp;
        ButtonMonitor       m_buttonSignal;
        volatile Direction  m_dirn;
        volatile int        m_floorLamp;
        static Elevator     s_inst = null;
        static object       s_mutex = new object();

        [DllImport("libelev.so", EntryPoint = "elev_init")]
        private static extern void Init();
        [DllImport("libelev.so", EntryPoint = "elev_set_motor_direction")]
        private static extern void SetMotorDirection(int dirn);
        [DllImport("libelev.so", EntryPoint = "elev_set_floor_indicator")]
        private static extern void SetFloorIndicator(int floor);
        [DllImport("libelev.so", EntryPoint = "elev_set_door_open_lamp")]
        private static extern void SetDoorOpenLamp(int value);
        [DllImport("libelev.so", EntryPoint = "elev_set_stop_lamp")]
        private static extern void SetStopLamp(int value);
        [DllImport("libelev.so", EntryPoint = "elev_get_floor_sensor_signal")]
        private static extern int GetFloorSensorSignal();
        [DllImport("libelev.so", EntryPoint = "elev_get_stop_signal")]
        private static extern int GetStopSignal();
        [DllImport("libelev.so", EntryPoint = "elev_get_obstruction_signal")]
        private static extern int GetObstructionSignal();
    }
    internal class LampController : ILampController
    {
        /// <summary>
        /// Turns off all button lamps
        /// </summary>
        public LampController(int buttonCount, int floorCount)
        {
            m_lampStates = new bool[buttonCount, floorCount];   // all false by default
            for (int floor = 0; floor < floorCount; ++floor)
                for (int button = 0; button < buttonCount; ++button)
                    SetButtonLamp(button, floor, 0);
        }
        public bool this[int button, int floor]
        {
            get { return m_lampStates[button, floor]; }
            set
            {
                SetButtonLamp(button, floor, (value == true) ? 1 : 0);
                m_lampStates[button, floor] = value;
            }
        }
        public bool this[Button button, int floor]
        {
            get { return this[(int)button, floor]; }
            set { this[(int)button, floor] = value; }
        }

        volatile bool[,] m_lampStates;

        [DllImport("libelev.so", EntryPoint = "elev_set_button_lamp")]
        private static extern void SetButtonLamp(int button, int floor, int value);
    }
    internal class ButtonMonitor : IButtonMonitor
    {
        public ButtonMonitor()
        { }
        public bool this[Button button, int floor]
        {
            get { return (GetButtonSignal((int)button, floor) == 1) ? true : false; }
        }
        public bool this[int button, int floor]
        {
            get
            {
                if (button < 0 || button > 2) throw new InvalidEnumArgumentException();
                return (GetButtonSignal(button, floor) == 1) ? true : false;
            }
        }
        [DllImport("libelev.so", EntryPoint = "elev_get_button_signal")]
        private static extern int GetButtonSignal(int button, int floor);
    }
}