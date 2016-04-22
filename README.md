# Elevator system

This system controls and coordinates actions of any number of elevators. Number of floors is hardcoded, but can be easily changed. The program consists of two parts: Dispatcher and Elevator. 
* Elevator uses Comedilib in order to interact with hardware. Since Comedilib only exists for Unix, the program is supposed to be run on Unix as well. If another type of hardware or library controlling it has to be used, only one file needs to be changed (`LibElev.cs`).
* Dispatcher maintains a tcp-connection with every elevator and chooses which elevator will serve each order. As it does not interact with the hardware directly it can be run on any OS supporting .NET. Tests have been performed on Linux and Windows. 

Below some UML diagrams describing the architecture are present. Some modules are to be compiled into .dll files.

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
