using HeroParser.AotTests;
using HeroParser.AotTests.Tests;

// AOT Compatibility Tests for HeroParser
// This project compiles with Native AOT to verify the library works without reflection

Console.WriteLine("HeroParser AOT Compatibility Tests");
Console.WriteLine("===================================");

var runner = new TestRunner();

CsvAotTests.Run(runner);
FixedWidthAotTests.Run(runner);

return runner.PrintSummary();
