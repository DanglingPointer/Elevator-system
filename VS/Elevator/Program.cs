/// Main program for elevator
using System;
namespace Elev
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Length != 2 && args.Length != 0)
                    throw new ArgumentException("Invalid combination of command line arguments is passed");
                Controller c = new Controller(args);
                c.Run();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.ReadKey();
        }
    }
}
