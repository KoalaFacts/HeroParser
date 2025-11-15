# HeroParser Code Audit Report

**Date:** 2025-11-15
**Auditor:** Claude
**Status:** 🔴 **CRITICAL ISSUES FOUND**

---

## 🔴 CRITICAL ISSUES (Must Fix Before Testing)

### 1. **Memory Safety Violation in CsvReader.ParseRow() - CRITICAL**
**File:** `src/HeroParser/CsvReader.cs:99-106`
**Severity:** 🔴 **CRITICAL** - Use-after-free bug

**Problem:**
```csharp
Span<int> starts = stackalloc int[estimatedColumns * 2];
Span<int> lengths = stackalloc int[estimatedColumns * 2];
int actualCount = _parser.ParseColumns(line, _delimiter, starts, lengths);
return new CsvRow(line, starts.Slice(0, actualCount), lengths.Slice(0, actualCount));
```

Stack-allocated memory (`stackalloc`) is only valid within the method scope. Once `ParseRow()` returns, the stack space can be reused. However, `CsvRow` stores `ReadOnlySpan<int>` references to this memory, which become dangling pointers.

**Impact:**
- Memory corruption
- Undefined behavior
- Random crashes or incorrect data

**Fix:**
Always use ArrayPool, never stackalloc for data that escapes the method:
```csharp
// Remove stackalloc path entirely
var startsArray = ArrayPool<int>.Shared.Rent(Math.Max(estimatedColumns * 2, 16));
var lengthsArray = ArrayPool<int>.Shared.Rent(Math.Max(estimatedColumns * 2, 16));
// ... rest of pooled logic
```

---

### 2. **Wrong Delimiter in EstimateColumnCount() - BUG**
**File:** `src/HeroParser/CsvReader.cs:134`
**Severity:** 🔴 **HIGH** - Incorrect behavior

**Problem:**
```csharp
if (sample[i] == ',') delimiterCount++; // Assume comma for estimation
```

Hardcoded comma (`,`) instead of using `_delimiter`. This gives wrong estimates for tab-delimited, semicolon-delimited, or pipe-delimited files.

**Impact:**
- Inefficient memory allocation for non-comma files
- Potential buffer overruns if estimate is too small

**Fix:**
```csharp
if (sample[i] == _delimiter) delimiterCount++;
```

---

### 3. **Stack Overflow Risk from Recursive MoveNext() - HIGH**
**File:** `src/HeroParser/CsvReader.cs:80`
**Severity:** 🟡 **MEDIUM-HIGH**

**Problem:**
```csharp
if (line.IsEmpty)
{
    return MoveNext(); // Recurse to next non-empty line
}
```

Recursive call can cause stack overflow if CSV has thousands of consecutive empty lines.

**Impact:**
- Stack overflow exception
- Crash on malformed CSVs with many empty lines

**Fix:**
Use iteration instead of recursion:
```csharp
while (true)
{
    if (_position >= _csv.Length)
        return false;

    var lineEnd = FindLineEnd(remaining, out int lineEndLength);
    // ... get line ...

    if (!line.IsEmpty)
    {
        _currentRow = ParseRow(line);
        return true;
    }

    // Skip empty line and continue loop
    _position += lineEnd + lineEndLength;
}
```

---

## 🟡 HIGH PRIORITY ISSUES

### 4. **Missing Using Statements**
**Files:** Multiple SIMD parsers
**Severity:** 🟡 **HIGH** - Compilation failure

**Problem:**
`BitOperations.TrailingZeroCount()` is used but `using System.Numerics;` is missing.

**Affected Files:**
- `src/HeroParser/Simd/Avx512Parser.cs:66`
- `src/HeroParser/Simd/Avx2Parser.cs:64`
- `src/HeroParser/Simd/NeonParser.cs:145`

**Fix:**
Add to all SIMD parser files:
```csharp
using System.Numerics;
```

---

### 5. **Delimiter Cast to Byte Limits to ASCII**
**Files:** All SIMD parsers
**Severity:** 🟡 **MEDIUM** - Unicode limitation

**Problem:**
```csharp
var delimiterVec = Vector256.Create((byte)delimiter);
```

Casting delimiter to `byte` only works for ASCII characters (0-127). Unicode delimiters (> 127) will be truncated.

**Impact:**
- Cannot use Unicode delimiters (rare but possible)
- Silent incorrect behavior

**Fix:**
Document this limitation or add validation:
```csharp
if (delimiter > 127)
    throw new ArgumentException("SIMD parsers only support ASCII delimiters", nameof(delimiter));
```

---

### 6. **Saturating Conversion Can Cause False Matches**
**Files:** All SIMD parsers
**Severity:** 🟡 **MEDIUM** - Unicode edge case

**Problem:**
```csharp
var bytes0 = Avx512BW.ConvertToVector256ByteWithSaturation(vec0);
```

UTF-16 characters > 255 are saturated to 255. If delimiter is an ASCII char and CSV contains Unicode chars that saturate to the same value, false matches occur.

**Example:**
- Delimiter: ASCII 255 (ÿ)
- CSV contains: Unicode char 0x01FF (saturates to 255)
- SIMD sees match, but shouldn't

**Impact:**
- Rare edge case with specific Unicode content
- Incorrect parsing

**Fix:**
Document limitation or add ASCII validation:
```csharp
// Only process ASCII CSVs with SIMD
if (line.Any(c => c > 127))
    return ScalarParser.Instance.ParseColumns(...);
```

---

## 🟢 MEDIUM PRIORITY ISSUES

### 7. **Unsafe MemoryMarshal Usage in NeonParser**
**File:** `src/HeroParser/Simd/NeonParser.cs:119`
**Severity:** 🟢 **MEDIUM**

**Problem:**
```csharp
var span = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<Vector128<byte>, byte>(ref comparison), 16);
```

Taking `ref` of value parameter `comparison`. This creates a ref to a local stack copy.

**Impact:**
- Potential undefined behavior
- May work by accident but is technically unsafe

**Fix:**
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static unsafe ulong ExtractMask(Vector128<byte> comparison)
{
    ulong mask = 0;
    byte* ptr = (byte*)&comparison;
    for (int i = 0; i < 16; i++)
    {
        if (ptr[i] != 0)
            mask |= 1UL << i;
    }
    return mask;
}
```

---

### 8. **Missing Dispose Calls for CsvRow**
**File:** `src/HeroParser/CsvReader.cs`
**Severity:** 🟢 **MEDIUM** - Memory leak

**Problem:**
`CsvRow` has a `Dispose()` method to return pooled arrays, but it's never called.

**Impact:**
- ArrayPool arrays not returned
- Memory leak over time

**Fix:**
Either:
1. **Automatic disposal** (requires .NET 9+ ref struct Dispose support)
2. **Manual call in MoveNext()**:
```csharp
public bool MoveNext()
{
    _currentRow.Dispose(); // Return old row's arrays before creating new one

    // ... rest of logic
}
```

---

### 9. **Permute Order May Be Incorrect in Avx2Parser**
**File:** `src/HeroParser/Simd/Avx2Parser.cs:47`
**Severity:** 🟢 **MEDIUM** - Needs verification

**Problem:**
```csharp
var permuted = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte();
```

Permute pattern `0b11_01_10_00` means: select lanes 0, 2, 1, 3. Is this the correct order after `PackUnsignedSaturate`?

**Impact:**
- Delimiters might be detected at wrong positions
- SIMD parser won't match scalar

**Fix:**
Verify permutation is correct or test thoroughly with validation tests.

---

## 🔵 LOW PRIORITY ISSUES

### 10. **No Bounds Checking in CsvRow Indexer**
**File:** `src/HeroParser/CsvRow.cs:49-60`
**Severity:** 🔵 **LOW** - By design

**Problem:**
```csharp
// Bounds check removed for maximum performance
// User must ensure valid index
```

**Impact:**
- Array out of bounds exception if user passes invalid index
- Less safe API

**Note:** This is intentional for performance. Document clearly in API docs.

---

### 11. **CsvFileReader Allocates String**
**File:** `src/HeroParser/CsvFileReader.cs`
**Severity:** 🔵 **LOW** - Design limitation

**Problem:**
```csharp
var chars = Encoding.UTF8.GetString(utf8Bytes); // Allocates
```

Memory-mapped file advantage is lost by allocating the entire string.

**Impact:**
- Not truly zero-copy
- Defeats purpose of memory mapping

**Fix:**
Parse UTF-8 bytes directly (future enhancement).

---

### 12. **ParallelCsvReader Allocates Results**
**File:** `src/HeroParser/ParallelCsvReader.cs:34-47`
**Severity:** 🔵 **LOW** - By design

**Problem:**
```csharp
public string[][] ParseAll() // Allocates
```

**Impact:**
- Defeats zero-allocation design
- Acceptable trade-off for multi-threading

**Note:** Documented limitation.

---

## ✅ WHAT'S WORKING WELL

1. **Hardware detection and dispatch** - Clean factory pattern
2. **Ref struct design** - Proper zero-allocation approach
3. **Test coverage** - Comprehensive test suite (200+ cases)
4. **SIMD bitmask strategy** - Correct approach for high performance
5. **ArrayPool usage** - Proper memory pooling (when used)
6. **API simplicity** - Clean, minimal surface area

---

## 📋 SUMMARY

### Issues by Severity

| Severity | Count | Description |
|----------|-------|-------------|
| 🔴 **CRITICAL** | 3 | Must fix - memory safety, correctness bugs |
| 🟡 **HIGH** | 3 | Should fix - compilation, edge cases |
| 🟢 **MEDIUM** | 3 | Nice to fix - safety, performance |
| 🔵 **LOW** | 3 | Optional - design limitations |

### Must Fix Before Testing

**Priority 1 (Blocking):**
1. ✅ Fix stackalloc memory safety (#1)
2. ✅ Fix delimiter estimation (#2)
3. ✅ Add missing using statements (#4)
4. ✅ Fix recursive MoveNext (#3)

**Priority 2 (Should Fix):**
5. ✅ Add delimiter validation or document ASCII limitation (#5, #6)
6. ✅ Fix NeonParser ExtractMask (#7)
7. ✅ Call CsvRow.Dispose() properly (#8)

**Priority 3 (Can Test First):**
8. ⏳ Verify Avx2 permutation (#9)
9. ⏳ Document bounds checking behavior (#10)

---

## 🎯 RECOMMENDED ACTION PLAN

### Step 1: Fix Critical Issues (Required)
- Fix stackalloc bug → use ArrayPool always
- Fix delimiter estimation → use _delimiter
- Add `using System.Numerics;` to all SIMD parsers
- Fix recursive MoveNext → use iteration

### Step 2: Add Validations (Recommended)
- Validate delimiter is ASCII (< 128)
- Add disposal logic for CsvRow
- Fix NeonParser ExtractMask

### Step 3: Test
- Run test suite
- Verify SIMD validation tests pass
- Check for memory leaks

### Step 4: Benchmark
- Once tests pass, run benchmarks
- Profile if performance < 30 GB/s

---

**Status:** 🔴 **DO NOT TEST YET** - Critical bugs must be fixed first!

Let me know when you want me to implement the fixes.
