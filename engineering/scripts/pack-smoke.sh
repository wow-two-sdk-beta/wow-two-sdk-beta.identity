#!/usr/bin/env bash
# Restores the packed nupkgs in a throwaway consumer, as a product would take them, and compiles a composition root
# that wires both forms behind the contract plus the test kit. A private packages folder keeps the local versions
# out of the machine's NuGet cache.
#
#   pack-smoke.sh <artifact-dir> <version>
set -euo pipefail

artifacts=$(cd "${1:?usage: pack-smoke.sh <artifact-dir> <version>}" && pwd)
version=${2:?pass the packed version}

consumer=$(mktemp -d)
trap 'rm -rf "$consumer"' EXIT

cat > "$consumer/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > "$consumer/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WoW2.Sdk.Beta.Be.Identity.Module" Version="$version" />
    <PackageReference Include="WoW2.Sdk.Beta.Be.Identity.Client" Version="$version" />
    <PackageReference Include="WoW2.Sdk.Beta.Be.Identity.Testing" Version="$version" />
  </ItemGroup>
</Project>
XML

cat > "$consumer/Program.cs" <<CS
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Client;
using WoW.Two.Sdk.Beta.Be.Identity.Module;
using WoW.Two.Sdk.Beta.Be.Identity.Testing.Greetings;

var formOne = new ServiceCollection();
formOne.AddIdentity().Configure(options => options.ConnectionString = "Data Source=:memory:");

var formThree = new ServiceCollection();
formThree.AddIdentityClient(options => options.BaseAddress = new Uri("https://service.example.test/"));

Console.WriteLine(\$"pack smoke: {nameof(GreetingsBrokerConformance)} and both forms resolve from the packed family");
CS

# No HTTP cache: a core version published minutes ago must resolve, not a cached index that predates it.
dotnet restore "$consumer/Consumer.csproj" --packages "$consumer/packages" --no-http-cache -v quiet
dotnet build "$consumer/Consumer.csproj" --no-restore -nologo -v quiet
dotnet "$consumer/bin/Debug/net10.0/Consumer.dll"
