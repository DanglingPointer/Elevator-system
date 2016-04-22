/// Module that handles the elevator car's movements
using System;
using System.Threading;
using Elev.Formats;
using Elev.Orders;

namespace Elev.Movement
{
    public class FloorEventArgs : EventArgs
    {
        public FloorEventArgs(int floor)
        {
            Floor = floor;
        }
        public int Floor { get; }
        public static implicit operator FloorEventArgs(int floor)
        {
            return new FloorEventArgs(floor);
        }
    }
    public class EmergencyStopEventArgs : EventArgs
    {
        public EmergencyStopEventArgs(int lastFloor, int curFloor, Direction lastDirn)
        {
            CurrentFloorSignal = curFloor;
            LastDirection = lastDirn;
        }
        /// <summary> Current floor, -1 if between floors </summary>
        public int CurrentFloorSignal { get; }
        /// <summary> Last floor passed by the elevator </summary>
        public int LastFloor { get; }
        /// <summary> Last registered direction of movement before stop </summary>
        public Direction LastDirection { get; }
    }
    internal class ElevatorStoppedException : Exception
    {
        public ElevatorStoppedException() { }
    }
    public class MotionController
    {
        public event Action<Order>                      ArrivedAtFloor;     // fired synchronously
        public event Action<FloorEventArgs>             PassedByFloor;      // fired asynchronously
        public event Action<EmergencyStopEventArgs>     EmergencyStopped;   // fired synchronously
        /// <summary>
        /// Initializes and moves down to nearest floor
        /// </summary>
        public MotionController()
        {
            m_car = Elevator.GetCar();
            m_car.MotorDirection = Direction.Down;
            while (m_car.FloorSensorSignal == -1)
                ;
            m_car.MotorDirection = Direction.Stop;
            m_lastFloor = m_car.FloorSensorSignal;
            m_stopped = false;
            m_lastDirn = Direction.Stop;

            PassedByFloor += (e) => m_lastFloor = e.Floor;
        }
        /// <summary>
        /// Serves the next order retrieved from the manager
        /// </summary>
        public void MoveToFloor(OrderManager manager)
        {
            m_stopped = false;
            Order dest = manager.NextOrder;
            if (dest.destFloor > LastFloor)
            {
                ChangeDirection(Direction.Up);
            }
            else if (dest.destFloor < LastFloor)
            {
                ChangeDirection(Direction.Down);
            }
            else
            {
                if (m_lastDirn == Direction.Up)
                    ChangeDirection(Direction.Down);
                else // LastDirection == Direction.Down (also after initialization)
                    ChangeDirection(Direction.Up);
            }
            int tmpFloor, prevFloor = -1;
            try
            {
                Console.WriteLine("Moving to floor...");
                while ((tmpFloor = m_car.FloorSensorSignal) != dest.destFloor)
                {
                    if (tmpFloor != -1 && tmpFloor != prevFloor)
                    {
                        prevFloor = tmpFloor;
                        var receivers = PassedByFloor.GetInvocationList();
                        foreach (Action<FloorEventArgs> receiver in receivers)
                            receiver.BeginInvoke(tmpFloor, null, null);
                    }
                    CheckStopped();
                    if (manager.OrdersAvailable)
                        dest = manager.NextOrder;
                }
                ChangeDirection(Direction.Stop);
                var recvs = PassedByFloor.GetInvocationList();
                foreach (Action<FloorEventArgs> receiver in recvs)
                    receiver.BeginInvoke(tmpFloor, null, null);

                m_car.DoorOpen();
                ArrivedAtFloor(dest);
                Thread.Sleep(2000);
                m_car.DoorClose();
            }
            catch (ElevatorStoppedException)
            {
                m_car.MotorDirection = Direction.Stop;
                EmergencyStopped(new EmergencyStopEventArgs(LastFloor, m_car.FloorSensorSignal, m_lastDirn));
            }
        }
        /// <summary>
        /// Triggers emergency stop
        /// </summary>
        public void EmergencyStop()
        {
            m_stopped = true;
        }
        /// <summary>
        /// Last floor passed by the elevator
        /// </summary>
        public int LastFloor
        {
            get
            {
                if (m_car.FloorSensorSignal != -1)
                    m_lastFloor = m_car.FloorSensorSignal;
                return m_lastFloor;
            }
        }
        /// <summary>
        /// Current direction of movement or Direction.Stop if not moving
        /// </summary>
        public Direction CurrentDirection
        {
            get { return m_car.MotorDirection; }
        }
        /// <summary>
        /// Direction of last motion, never Stop unless just started
        /// </summary>
        public Direction LastDirection
        {
            get { return m_lastDirn; }
        }
        /// <summary>
        /// Throws ElevatorStoppedException if stopped, returns false otherwise
        /// </summary>
        public bool CheckStopped()
        {
            if (m_stopped == true)
                throw new ElevatorStoppedException();
            return m_stopped;
        }
        /// <summary>
        /// Changes motor direction and updates LastDirection
        /// </summary>
        private void ChangeDirection(Direction dirn)
        {
            m_car.MotorDirection = dirn;
            if (dirn != Direction.Stop)
                m_lastDirn = dirn;
            else
            {
                if (LastFloor == Elevator.FloorCount - 1)
                    m_lastDirn = Direction.Down;
                else if (LastFloor == 0)
                    m_lastDirn = Direction.Up;
            }
        }
        Direction       m_lastDirn;
        volatile int    m_lastFloor;
        volatile bool   m_stopped;
        ICar            m_car;
    }
}