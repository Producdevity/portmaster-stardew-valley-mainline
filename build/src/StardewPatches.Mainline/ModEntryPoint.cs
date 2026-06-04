using System;

[AttributeUsage(AttributeTargets.Class)]
public class ModEntryPointAttribute : Attribute
{
}

[ModEntryPoint]
public static class StardewPatches
{
    public static void Main()
    {
        Console.Out.WriteLine("StardewPatches initialized");
    }
}
