using System;
using System.Linq;
using System.Reflection;

#pragma warning disable OPENAI002
var type = typeof(OpenAI.Realtime.RealtimeServerUpdateResponseOutputItemAdded);
Console.WriteLine($"=== {type.Name} ===");
foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
Console.WriteLine();

Console.WriteLine("=== RealtimeServerUpdate (base) ===");
var baseType = typeof(OpenAI.Realtime.RealtimeServerUpdate);
foreach (var prop in baseType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
Console.WriteLine();

Console.WriteLine("=== RealtimeSessionClient methods ===");
var sessionType = typeof(OpenAI.Realtime.RealtimeSessionClient);
foreach (var method in sessionType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
    Console.WriteLine($"  {method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
