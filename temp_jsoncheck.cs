using System;
using System.IO;
using System.Text.Json;
class P {
  static void Main() {
    var path = @"D:\eyesmanagement\pc\bin\Debug\net6.0-windows\workflow-settings.json";
    try {
      using var doc = JsonDocument.Parse(File.ReadAllText(path));
      Console.WriteLine("PARSE_OK");
    } catch (Exception ex) {
      Console.WriteLine("PARSE_FAIL");
      Console.WriteLine(ex.GetType().FullName);
      Console.WriteLine(ex.Message);
    }
  }
}
