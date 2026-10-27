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
    dotnet restore "$target/NuGetConsumer.csproj" --configfile "$work/NuGet.Config" -r linux-x64
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
