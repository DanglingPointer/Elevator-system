# Elevator system

![Gui screenshot](https://github.com/DanglingPointer/Elevator-system/blob/master/Dispatcher_gui.png)

This system controls and coordinates actions of any number of elevators. Number of floors is hardcoded, but can be easily changed. The program consists of two parts: Dispatcher and Elevator. 
* Elevator uses Comedilib in order to interact with hardware. Since Comedilib only exists for Unix, the program is supposed to be run on Unix as well. If another type of hardware or library controlling it has to be used, only one file needs to be changed (`LibElev.cs`).
* Dispatcher maintains a tcp-connection with every elevator and chooses which elevator will serve each order. It also continuously tracks the number of elevators and reassignes orders dynamically in case of elevator failure. As it does not interact with the hardware directly it can be run on any OS supporting .NET. Tests have been performed on Linux and Windows. 

In order to start the dispatcher one have to type `mono Dispatcher.exe` in the terminal window. Once dispatcher is started, the elevators can be started by typing for example `mono Elevator.exe 129.241.187.158 55555` where the first argument is the IPv4 address of the dispatcher. If both files are started on the same computer, no command line arguments need to be passed to elevator program. 

Below are some UML diagrams describing the architecture. Notice that some changes were made in order to comply with requirements and system specifications, rendering some of the diagrams outdated.

#### Elev.Formats.dll
Common module used by both Elevator and Dispatcher. It defines common data types and methods of (de-)serialization. 
![Formats diagram](https://github.com/DanglingPointer/Elevator-system/blob/master/Formats_class_diagram.jpg)

#### Elevator.exe
The program controlling hardware and maintaining connection with Dispatcher. Makes use of ELev.Formats.dll and the native library libelev.so.
![Elevator diagram](https://github.com/DanglingPointer/Elevator-system/blob/master/Elevator_class_diagram.jpg)

#### Elev.Dispatcher.dll
Core logic for the Dispatcher. Implements process-pair pattern and makes use of Elev.Formats.dll.
![ElevDispatcher diagram](https://github.com/DanglingPointer/Elevator-system/blob/master/ElevDispatcher_class_diagram.jpg)

#### Dispatcher.exe
Integration of Elev.Dispatcher.dll into a simple GUI created using WinForms.
![Dispatcher diagram](https://github.com/DanglingPointer/Elevator-system/blob/master/Dispatcher_class_diagram.jpg)
