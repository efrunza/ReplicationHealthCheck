using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;

public static class LogHelpers
{
    private static int counter;
    private static readonly object locker = new object();

    public static void ResetCounter()
    {
        lock (locker)
        {
            counter = 0;

            //Console.WriteLine("Counter value is: " + counter.ToString());
        }
    }

    public static void IncrementCounter()
    {
        lock (locker)
        {
            counter++;

            //Console.WriteLine("Counter value is: " + counter.ToString());
        }
    }

    public static int GetCounter()
    {
        lock (locker)
        {
            //Console.WriteLine("Counter value is: " + counter.ToString());

            return counter;

        }
    }
}