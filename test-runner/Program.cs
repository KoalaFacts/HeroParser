using System;
using HeroParser;

class ManualTests
{
    static int passed = 0;
    static int failed = 0;

    static void Main()
    {
        Console.WriteLine("Running HeroParser Manual Tests...\n");

        // Test 1: Simple CSV
        Test("SimpleCsv_ParsesCorrectly", () =>
        {
            var csv = "a,b,c\n1,2,3\n4,5,6";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(3, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b", "c" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2", "3" }, rows[1], "Row 1");
            AssertArrayEqual(new[] { "4", "5", "6" }, rows[2], "Row 2");
        });

        // Test 2: Empty fields
        Test("EmptyFields_ParsesCorrectly", () =>
        {
            var csv = "a,,c\n,b,\n,,";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(3, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "", "c" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "", "b", "" }, rows[1], "Row 1");
            AssertArrayEqual(new[] { "", "", "" }, rows[2], "Row 2");
        });

        // Test 3: Single column
        Test("SingleColumn_ParsesCorrectly", () =>
        {
            var csv = "a\nb\nc";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(3, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "b" }, rows[1], "Row 1");
            AssertArrayEqual(new[] { "c" }, rows[2], "Row 2");
        });

        // Test 4: Different delimiter
        Test("TabDelimiter_ParsesCorrectly", () =>
        {
            var csv = "a\tb\tc\n1\t2\t3";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan(), '\t'))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b", "c" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2", "3" }, rows[1], "Row 1");
        });

        // Test 5: Trailing newline
        Test("TrailingNewline_ParsesCorrectly", () =>
        {
            var csv = "a,b\n1,2\n";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2" }, rows[1], "Row 1");
        });

        // Test 6: CRLF line endings
        Test("CRLFLineEndings_ParsesCorrectly", () =>
        {
            var csv = "a,b\r\n1,2\r\n";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2" }, rows[1], "Row 1");
        });

        // Test 7: Many columns
        Test("ManyColumns_ParsesCorrectly", () =>
        {
            var csv = "a,b,c,d,e,f,g,h,i,j\n1,2,3,4,5,6,7,8,9,10";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertEqual(10, rows[0].Length, "Row 0 column count");
            AssertEqual(10, rows[1].Length, "Row 1 column count");
        });

        // Test 8: Column access by index
        Test("ColumnAccess_ByIndex", () =>
        {
            var csv = "a,b,c\n1,2,3";
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual("a", row[0].ToString(), "Column 0");
            AssertEqual("b", row[1].ToString(), "Column 1");
            AssertEqual("c", row[2].ToString(), "Column 2");
        });

        // Test 9: Column count
        Test("ColumnCount_IsCorrect", () =>
        {
            var csv = "a,b,c\n1,2,3";
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual(3, row.Count, "Column count");
        });

        // Test 10: Empty CSV
        Test("EmptyCsv_ReturnsNoRows", () =>
        {
            var csv = "";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(0, rows.Count, "Row count should be 0");
        });

        // Test 11: Large row (tests SIMD parsing - 64+ chars for AVX-512)
        Test("LargeRow_ParsesCorrectly", () =>
        {
            var csv = "aaaaa,bbbbb,ccccc,ddddd,eeeee,fffff,ggggg,hhhhh,iiiii,jjjjj,kkkkk,lllll\n11111,22222,33333,44444,55555,66666,77777,88888,99999,00000,aaaaa,bbbbb";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertEqual(12, rows[0].Length, "Row 0 column count");
            AssertEqual("aaaaa", rows[0][0], "Row 0 Col 0");
            AssertEqual("lllll", rows[0][11], "Row 0 Col 11");
        });

        // Test 12: Very long line (tests buffer allocation)
        Test("VeryLongLine_ParsesCorrectly", () =>
        {
            var fields = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                fields.Add($"field{i}");
            }
            var csv = string.Join(",", fields);
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual(100, row.Count, "Column count");
            AssertEqual("field0", row[0].ToString(), "First field");
            AssertEqual("field99", row[99].ToString(), "Last field");
        });

        // Test 13: Consecutive delimiters
        Test("ConsecutiveDelimiters_ParsesCorrectly", () =>
        {
            var csv = "a,,,d\n,,,";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "", "", "d" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "", "", "", "" }, rows[1], "Row 1");
        });

        // Test 14: Only delimiter
        Test("OnlyDelimiter_ParsesCorrectly", () =>
        {
            var csv = ",";
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual(2, row.Count, "Column count");
            AssertEqual("", row[0].ToString(), "Col 0");
            AssertEqual("", row[1].ToString(), "Col 1");
        });

        // Test 15: Multiple empty rows
        Test("MultipleEmptyRows_SkipsCorrectly", () =>
        {
            var csv = "a,b\n\n\n1,2\n\n";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            // Empty lines should be skipped
            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2" }, rows[1], "Row 1");
        });

        // Test 16: Mixed line endings
        Test("MixedLineEndings_ParsesCorrectly", () =>
        {
            var csv = "a,b\r\nc,d\ne,f\r\ng,h";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan()))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(4, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "c", "d" }, rows[1], "Row 1");
            AssertArrayEqual(new[] { "e", "f" }, rows[2], "Row 2");
            AssertArrayEqual(new[] { "g", "h" }, rows[3], "Row 3");
        });

        // Test 17: Pipe delimiter
        Test("PipeDelimiter_ParsesCorrectly", () =>
        {
            var csv = "a|b|c\n1|2|3";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan(), '|'))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b", "c" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2", "3" }, rows[1], "Row 1");
        });

        // Test 18: Semicolon delimiter
        Test("SemicolonDelimiter_ParsesCorrectly", () =>
        {
            var csv = "a;b;c\n1;2;3";
            var rows = new List<string[]>();

            foreach (var row in Csv.Parse(csv.AsSpan(), ';'))
            {
                rows.Add(row.ToStringArray());
            }

            AssertEqual(2, rows.Count, "Row count");
            AssertArrayEqual(new[] { "a", "b", "c" }, rows[0], "Row 0");
            AssertArrayEqual(new[] { "1", "2", "3" }, rows[1], "Row 1");
        });

        // Test 19: SIMD boundary testing (exactly 32 chars - AVX2 boundary)
        Test("Exactly32Chars_ParsesCorrectly", () =>
        {
            var csv = "12345678901234567890123456789,ab"; // 30 chars + 1 comma + 2 chars = 33 total
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual(2, row.Count, "Column count");
            AssertEqual("12345678901234567890123456789", row[0].ToString(), "Col 0");
            AssertEqual("ab", row[1].ToString(), "Col 1");
        });

        // Test 20: SIMD boundary testing (exactly 64 chars - AVX-512 boundary)
        Test("Exactly64Chars_ParsesCorrectly", () =>
        {
            var csv = "1234567890123456789012345678901234567890123456789012345678901,abc"; // 61 + 1 + 3 = 65
            var reader = Csv.Parse(csv.AsSpan());
            reader.MoveNext();
            var row = reader.Current;

            AssertEqual(2, row.Count, "Column count");
            AssertEqual("1234567890123456789012345678901234567890123456789012345678901", row[0].ToString(), "Col 0");
            AssertEqual("abc", row[1].ToString(), "Col 1");
        });

        Console.WriteLine($"\n=== Results ===");
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {passed + failed}");

        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    static void Test(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine($"✓ {name}");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"✗ {name}");
            Console.WriteLine($"  Error: {ex.Message}");
        }
    }

    static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{message}: Expected {expected}, got {actual}");
        }
    }

    static void AssertArrayEqual<T>(T[] expected, T[] actual, string message)
    {
        if (expected.Length != actual.Length)
        {
            throw new Exception($"{message}: Length mismatch. Expected {expected.Length}, got {actual.Length}");
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
            {
                throw new Exception($"{message}[{i}]: Expected {expected[i]}, got {actual[i]}");
            }
        }
    }
}
