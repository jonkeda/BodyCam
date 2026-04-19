using System;
using System.Reflection;
using OpenAI.Realtime;
var type = typeof(RealtimeSessionClientOptions);
Console.WriteLine($"=== {type.Name} ===");
foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
Console.WriteLine();

// Check if WebSocket property has a setter
var wsProp = typeof(RealtimeSessionClient).GetProperty("WebSocket", BindingFlags.Public | BindingFlags.Instance);
Console.WriteLine($"WebSocket: CanRead={wsProp?.CanRead}, CanWrite={wsProp?.CanWrite}");

// Check base class
Console.WriteLine($"RealtimeSessionClient base: {typeof(RealtimeSessionClient).BaseType?.Name}");
