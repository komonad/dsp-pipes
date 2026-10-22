using System;
using System.IO;
using FluidPipes.Tests;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var result = CoreSuite.Run(true);
            Console.WriteLine(result);
            if (args.Length > 0)
                File.WriteAllText(args[0], result);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
