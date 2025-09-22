using System;
using System.IO;
using System.Text;

namespace HeroParser.BenchmarkTests;

public static class DatasetGenerator
{
    private static readonly Random _random = new(42); // Fixed seed for reproducible datasets

    private static readonly string[] _firstNames =
    {
        "Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Helen",
        "Ian", "Julia", "Kevin", "Laura", "Michael", "Nancy", "Oliver", "Patricia",
        "Quinn", "Rachel", "Steve", "Tina", "Ulrich", "Victoria", "William", "Xenia",
        "Yuri", "Zoe"
    };

    private static readonly string[] _lastNames =
    {
        "Anderson", "Brown", "Clark", "Davis", "Evans", "Fisher", "Garcia", "Harris",
        "Johnson", "Kim", "Lee", "Miller", "Nelson", "O'Connor", "Parker", "Quinn",
        "Rodriguez", "Smith", "Taylor", "Upton", "Vasquez", "Wilson", "Xu", "Young", "Zhang"
    };

    private static readonly string[] _companies =
    {
        "TechCorp", "DataSystems", "CloudComputing Inc", "AI Solutions", "BlockchainTech",
        "CyberSecurity Ltd", "QuantumSoft", "BioTech Industries", "Green Energy Co",
        "Smart Manufacturing", "Digital Healthcare", "FinTech Innovations"
    };

    public static void GenerateAllDatasets()
    {
        var dataDir = Path.Combine("BenchmarkData");
        Directory.CreateDirectory(dataDir);

        // Constitution: "small files (1KB), medium files (1MB), large files (1GB)"
        GenerateSimpleDataset(Path.Combine(dataDir, "simple_1kb.csv"), 1024);
        GenerateSimpleDataset(Path.Combine(dataDir, "simple_1mb.csv"), 1024 * 1024);
        GenerateSimpleDataset(Path.Combine(dataDir, "simple_1gb.csv"), 1024 * 1024 * 1024);

        Console.WriteLine("Constitution-compliant datasets generated: 1KB, 1MB, 1GB");
    }

    public static void GenerateSimpleDataset(string filePath, long targetSizeBytes)
    {
        const string header = "Id,FirstName,LastName,Email,Company,Salary,JoinDate,IsActive\n";
        var estimatedRowSize = 80; // Approximate bytes per row
        var estimatedRows = (int)((targetSizeBytes - header.Length) / estimatedRowSize);

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        writer.Write(header);

        for (int i = 1; i <= estimatedRows; i++)
        {
            var firstName = _firstNames[_random.Next(_firstNames.Length)];
            var lastName = _lastNames[_random.Next(_lastNames.Length)];
            var email = $"{firstName.ToLower()}.{lastName.ToLower()}@email.com";
            var company = _companies[_random.Next(_companies.Length)];
            var salary = _random.Next(30000, 150000);
            var joinDate = DateTime.Now.AddDays(-_random.Next(1, 3650)).ToString("yyyy-MM-dd");
            var isActive = _random.Next(0, 2) == 1 ? "true" : "false";

            writer.WriteLine($"{i},{firstName},{lastName},{email},{company},{salary},{joinDate},{isActive}");
        }
    }

}