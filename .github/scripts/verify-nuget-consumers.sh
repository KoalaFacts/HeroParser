#!/usr/bin/env bash
# Release gate: consume the actual local-feed .nupkg instead of ProjectReference builds.
set -euo pipefail

if [[ ! -f Directory.Build.props ]]; then
  echo "Run from the HeroParser repository root." >&2
  exit 1
fi
version=$(sed -nE 's/^[[:space:]]*<Version>([^<]+)<\/Version>.*/\1/p' Directory.Build.props | head -n 1)
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
  echo "Unexpected release version: $version" >&2
  exit 1
fi

feed="$PWD/.artifacts/release-candidate"
mkdir -p "$feed"
for project in src/HeroParser/HeroParser.csproj src/HeroParser.Console/HeroParser.Console.csproj src/HeroParser.Cli/HeroParser.Cli.csproj; do
  dotnet pack "$project" --configuration Release --no-build --output "$feed" \
    -p:Version="$version" -p:PackageVersion="$version" \
    -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
done

for id in HeroParser HeroParser.Console HeroParser.Cli; do
  for ext in nupkg snupkg; do
    test -s "$feed/$id.$version.$ext" || { echo "Missing $id.$version.$ext" >&2; exit 1; }
  done
done
for tfm in net8.0 net9.0 net10.0; do
  unzip -Z1 "$feed/HeroParser.$version.nupkg" | grep -Fx "lib/$tfm/HeroParser.dll" >/dev/null
  unzip -Z1 "$feed/HeroParser.Console.$version.nupkg" | grep -Fx "lib/$tfm/HeroParser.Console.dll" >/dev/null
done
unzip -Z1 "$feed/HeroParser.$version.nupkg" |
  grep -Fx "analyzers/dotnet/cs/HeroParser.Generators.dll" >/dev/null

git rev-parse HEAD > "$feed/SOURCE_SHA"
sha256sum "$feed"/*.nupkg "$feed"/*.snupkg > "$feed/SHA256SUMS"
cat "$feed/SOURCE_SHA"
cat "$feed/SHA256SUMS"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/packages"
export HEROPARSER_EXPECTED_SOURCE_SHA="$(cat "$feed/SOURCE_SHA")"
cat > "$work/NuGet.Config" <<CFG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate">
      <package pattern="HeroParser" />
      <package pattern="HeroParser.*" />
    </packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
CFG

for tfm in net8.0 net9.0 net10.0; do
  target="$work/$tfm"
  dotnet new console --name NuGetConsumer --framework "$tfm" --no-restore --output "$target"
  dotnet add "$target/NuGetConsumer.csproj" package HeroParser --version "$version" --no-restore
  cat > "$target/Program.cs" <<'CS'
using System.Text.Json.Serialization;
using HeroParser;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Reading.Records;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths.Records;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;

var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Csv).Assembly)!.InformationalVersion;
if (!info.EndsWith("+" + Environment.GetEnvironmentVariable("HEROPARSER_EXPECTED_SOURCE_SHA"), StringComparison.Ordinal))
    throw new InvalidOperationException("Consumed package source differs: " + info);
Console.WriteLine("Consumed core identity: " + info);

string csvText = """
Name,Age
"Alice",30
Bob,25
""";
var csv = Csv.DeserializeRecords<SmokePerson>(csvText).ToList();
if (csv.Count != 2 || csv[0].Name != "Alice" || csv[0].Age != 30 ||
    csv[1].Name != "Bob" || csv[1].Age != 25)
    throw new InvalidOperationException("CSV package-consumer fields differ.");

string fixedText = "Alice".PadRight(20) + "30".PadLeft(10) +
                   "Bob".PadRight(20) + "25".PadLeft(10);
var fixedRows = FixedWidth.DeserializeRecords<SmokeFixedPerson>(
    fixedText, new FixedWidthReadOptions { RecordLength = 30 }).ToList();
if (fixedRows.Count != 2 || fixedRows[0].Name != "Alice" || fixedRows[0].Age != 30 ||
    fixedRows[1].Name != "Bob" || fixedRows[1].Age != 25)
    throw new InvalidOperationException("FixedWidth package-consumer fields differ.");

// NativeAOT package consumer: OnError and custom converters use a reflection fallback.
// The generated binder must preserve setters or fields silently become defaults.
int errorCalls = 0;
string invalidFixedText = "Alice".PadRight(20) + "30".PadLeft(10) + "\n" +
                          "Bob".PadRight(20) + "INVALID".PadLeft(10);
var withErrorHandler = FixedWidth.Read<SmokeFixedPerson>()
    .OnError((_, _) => { errorCalls++; return FixedWidthDeserializeErrorAction.SkipRecord; })
    .FromText(invalidFixedText).ToList();
if (withErrorHandler.Count != 1 || withErrorHandler[0].Name != "Alice" ||
    withErrorHandler[0].Age != 30 || errorCalls != 1)
    throw new InvalidOperationException("Generated FixedWidth OnError package-consumer fields/callback differ.");

int converterCalls = 0;
var withConverter = FixedWidth.Read<SmokeFixedPerson>()
    .RegisterConverter<int>((ReadOnlySpan<char> value, System.Globalization.CultureInfo culture, string? format, out int result) =>
    {
        converterCalls++;
        result = int.Parse(value, culture) + 100;
        return true;
    })
    .FromText("Alice".PadRight(20) + "30".PadLeft(10)).ToList();
if (withConverter.Count != 1 || withConverter[0].Name != "Alice" ||
    withConverter[0].Age != 130 || converterCalls != 1)
    throw new InvalidOperationException("Generated FixedWidth converter package-consumer fields/callback differ.");

// The CRLF split exactly across writes must not trigger the JSONL size limit.
var pipe = new Pipe();
try
{
    await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("{}\r"));
    await using var lineIterator = Jsonl.ReadLinesAsync(
        pipe.Reader, new JsonlReadOptions { MaxLineSizeBytes = 2 }).GetAsyncEnumerator();
    Task<bool> firstLine = lineIterator.MoveNextAsync().AsTask();
    await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("\n"));
    await pipe.Writer.CompleteAsync();
    if (!await firstLine || lineIterator.Current.ToString() != "{}" ||
        lineIterator.Current.LineNumber != 1 || await lineIterator.MoveNextAsync())
        throw new InvalidOperationException("Split JSONL CRLF package-consumer boundary differs.");
}
finally
{
    await pipe.Reader.CompleteAsync();
    await pipe.Writer.CompleteAsync();
}

string jsonlText = """
{"Name":"Alice","Age":30}
{"Name":"Bob","Age":25}
""";
var jsonl = Jsonl.DeserializeRecords(jsonlText, SmokeJsonContext.Default.SmokePerson).ToList();
if (jsonl.Count != 2 || jsonl[0].Name != "Alice" || jsonl[0].Age != 30 ||
    jsonl[1].Name != "Bob" || jsonl[1].Age != 25)
    throw new InvalidOperationException("JSONL package-consumer fields differ.");

Console.WriteLine("NuGet PackageReference CSV/FixedWidth/JSONL consumer OK");

[GenerateBinder]
public sealed class SmokePerson
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[GenerateBinder]
public sealed class SmokeFixedPerson
{
    [PositionalMap(Start = 0, Length = 20)]
    public string Name { get; set; } = "";
    [PositionalMap(Start = 20, Length = 10, Alignment = FieldAlignment.Right)]
    public int Age { get; set; }
}

[JsonSerializable(typeof(SmokePerson))]
internal partial class SmokeJsonContext : JsonSerializerContext
{
}
CS
  dotnet restore "$target/NuGetConsumer.csproj" --configfile "$work/NuGet.Config"
  dotnet run --project "$target/NuGetConsumer.csproj" --configuration Release --no-restore
  if [[ "$tfm" == "net10.0" ]]; then
    dotnet restore "$target/NuGetConsumer.csproj" --configfile "$work/NuGet.Config" -r linux-x64 -p:PublishAot=true
    dotnet publish "$target/NuGetConsumer.csproj" --configuration Release --framework net10.0 \
      --runtime linux-x64 --no-restore -p:PublishAot=true -p:TreatWarningsAsErrors=true \
      --output "$target/native"
    "$target/native/NuGetConsumer"
  fi
done

dotnet tool install HeroParser.Cli --version "$version" --tool-path "$work/tool" \
  --configfile "$work/NuGet.Config"
"$work/tool/heroparser" --help > /dev/null
echo "All local-feed NuGet package consumers passed for $version"
