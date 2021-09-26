﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Console;

if (args is { Length: 0 } || args[0] is not string path)
{
    WriteLine("Must specify a repo root directory as input");
    return;
}

// yaml top-matter

string topMatter =
@"# generated by dependadotnet
# https://github.com/dotnet/core/tree/main/samples/dependadotnet
version: 2
updates:";

WriteLine(topMatter);

/* Generate the following pattern for each project file:

  Note: Wednesday was chosen for quick response to .NET patch Tuesday updates

- package-ecosystem: ""nuget""
  directory: ""/"" #projectfilename
  schedule:
      interval: ""weekly""
      day: ""wednesday""
  open-pull-requests-limit: 5
*/

string packagesJsonUrl = "https://raw.githubusercontent.com/dotnet/core/b5ca8283def279b20eced6c0b14c4634659cd6eb/samples/dependadotnet/package-ignore.json";
Dictionary<string, string[]> packageIgnore = await GetPackagesInfo(packagesJsonUrl);
string validPackageReference = @"PackageReference.*Version=""[0-9]";
string packageReference = @"PackageReference Include=""";
string targetFrameworkStart = "<TargetFramework>";
string targetFrameworkEnd = "</TargetFramework>";
string dotnetDir = $"{Path.AltDirectorySeparatorChar}.dotnet";

foreach (string directory in Directory.EnumerateDirectories(path,"*.*",SearchOption.AllDirectories))
{
    if (directory.EndsWith(dotnetDir))
    {
        continue;
    }

    foreach (string file in Directory.EnumerateFiles(directory))
    {
        if (!IsProject(file))
        {
            continue;
        }

        string filename = Path.GetFileName(file);
        string? parentDir = Path.GetDirectoryName(file);
        string relativeDir = parentDir?.Substring(path.Length).Replace(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar) ?? Path.AltDirectorySeparatorChar.ToString();
        string? targetFramework = null;
        bool match = false;
        List<PackageIgnoreMapping> mappings = new();
        foreach (string content in File.ReadLines(file))
        {
            if (targetFramework is null && TryGetTargetFramework(content, out targetFramework))
            {
            }

            if (Regex.IsMatch(content, validPackageReference))
            {
                match = true;

                if (TryGetPackageName(content, out string? packageName) &&
                    packageIgnore.TryGetValue($"{packageName}_{targetFramework}", out string[]? ignore))
                {
                    mappings.Add(new(packageName,ignore));
                }

                break;
            }
        }

        if (!match)
        {
            continue;
        }

        WriteLine( 
$@"  - package-ecosystem: ""nuget""
    directory: ""{relativeDir}"" #{filename}
    schedule:
      interval: ""weekly""
      day: ""wednesday""
    open-pull-requests-limit: 5");

        if (mappings.Count == 0)
        {
            continue;
        }

        /* Format:
    ignore:
     - dependency-name: "Microsoft.AspNetCore.Mvc.NewtonsoftJson"
       versions: ["5.*"]        
        */

        WriteLine("    ignore:");

        foreach(PackageIgnoreMapping mapping in mappings)
        {
            WriteLine( 
$@"     - dependency-name: ""{mapping.PackageName}""
       versions: {PrintArrayAsYaml(mapping.Ignore)}");
        }
    }
}

bool IsProject(string filename) => Path.GetExtension(filename) switch
{
    ".csproj" or ".fsproj" or ".vbproj" => true,
    _ => false
};

bool TryGetTargetFramework(string content, [NotNullWhen(true)] out string? targetFramework)
{
    targetFramework = null;
    int start = content.IndexOf(targetFrameworkStart);

    if (start == -1)
    {
        return false;
    }

    int end = content.IndexOf(targetFrameworkEnd);

    if (end == -1 ||
        end < start)
    {
        return false;
    }

    int startOfTFM = start + targetFrameworkStart.Length;
    targetFramework = content.Substring(startOfTFM, end - startOfTFM);

    return targetFramework.StartsWith("net");
}

bool TryGetPackageName(string content, [NotNullWhen(true)] out string? packageName)
{
    packageName = null;
    int start = content.IndexOf(packageReference);

    if (start < 0)
    {
        return false;
    }

    int startOfPackageName = start + packageReference.Length;
    int endOfPackageName = content.AsSpan(startOfPackageName).IndexOf('"');

    if (endOfPackageName == 0)
    {
        return false;
    }

    packageName = content.Substring(startOfPackageName, endOfPackageName);
    return true;
}

async Task<Dictionary<string, string[]>> GetPackagesInfo(string url)
{
    HttpClient client = new();
    PackageInfoSet? packages = await client.GetFromJsonAsync<PackageInfoSet>(packagesJsonUrl);

    if (packages is null)
    {
        throw new IOException("Could not download packages information");
    }
    
    Dictionary<string, string[]> packageIgnore = new();

    foreach (PackageInfo package in packages.Packages)
    {
        foreach(PackageTargetFrameworkIgnoreMapping mapping in package.Mapping)
        {
            string key = $"{package.Name}_{mapping.TargetFramework}";
            packageIgnore.Add(key, mapping.Ignore);
        }
    }

    return packageIgnore;
}

string PrintArrayAsYaml(string[] array)
{
    StringBuilder buffer = new(); 
    buffer.Append("[");
    for (int i = 0; i < array.Length; i++)
    {
        buffer.Append($@"""{array[i]}""");

        if (i + 1 < array.Length)
        {
            buffer.Append(", ");
        }
    }
    buffer.Append("]");

    return buffer.ToString();
}

record PackageInfoSet(PackageInfo[] Packages);
record PackageInfo(string Name, PackageTargetFrameworkIgnoreMapping[] Mapping);
record PackageTargetFrameworkIgnoreMapping(string TargetFramework, string[] Ignore);
record PackageIgnoreMapping(string PackageName, string[] Ignore);
