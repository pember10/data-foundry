# Assembly Binding Redirects - Data Foundry

## Overview

Assembly binding redirects are necessary when your extension uses packages that depend on different versions of the same assembly. Without these redirects, you'll see errors like:

```
Could not load file or assembly 'Azure.Core, Version=1.47.3.0, Culture=neutral, PublicKeyToken=...' 
or one of its dependencies. The system cannot find the file specified.
```

## Current Binding Redirects (app.config)

Our extension includes the following binding redirects:

| Assembly | Public Key Token | Old Versions | New Version |
|----------|------------------|--------------|-------------|
| Microsoft.Identity.Client | 0a613f4dd989e8ae | 0.0.0.0-4.78.0.0 | 4.78.0.0 |
| Azure.Core | 92742159e12e44c8 | 0.0.0.0-1.49.0.0 | 1.49.0.0 |
| System.Memory | cc7b13ffcd2ddd51 | 0.0.0.0-4.0.5.0 | 4.0.5.0 |
| System.Text.Json | cc7b13ffcd2ddd51 | 0.0.0.0-8.0.0.6 | 8.0.0.6 |
| System.Runtime.CompilerServices.Unsafe | b03f5f7f11d50a3a | 0.0.0.0-6.0.0.0 | 6.0.0.0 |

## Why Binding Redirects Are Needed

### Example Scenario

1. Your code references `Azure.Identity 1.16.0`
2. `Azure.Identity 1.16.0` depends on `Azure.Core 1.49.0`
3. But `Microsoft.Data.SqlClient` depends on `Azure.Core 1.47.3`
4. Without a binding redirect, .NET tries to load `Azure.Core 1.47.3` (doesn't exist) ? **Error**
5. With binding redirect, .NET redirects `1.47.3` ? `1.49.0` ? **Success**

## How to Add a Binding Redirect

### Step 1: Identify the Missing Assembly

When you see an error like:
```
Could not load file or assembly 'SomeAssembly, Version=X.Y.Z.0'
```

### Step 2: Find the Actual Version

```powershell
[System.Reflection.Assembly]::LoadFile("$PWD\bin\Debug\SomeAssembly.dll").GetName().Version
```

Output example:
```
Major  Minor  Build  Revision
-----  -----  -----  --------
2      1      0      0
```

### Step 3: Add Binding Redirect

In `app.config`:

```xml
<dependentAssembly>
  <assemblyIdentity name="SomeAssembly" publicKeyToken="abc123..." culture="neutral" />
  <bindingRedirect oldVersion="0.0.0.0-2.1.0.0" newVersion="2.1.0.0" />
</dependentAssembly>
```

### Step 4: Rebuild

```
Build > Rebuild Solution
```

The `app.config` gets transformed to `data-foundry.dll.config` in the output directory.

## Finding Public Key Tokens

### Method 1: Using PowerShell

```powershell
[System.Reflection.Assembly]::LoadFile("$PWD\bin\Debug\Azure.Core.dll").GetName().GetPublicKeyToken() | ForEach-Object { $_.ToString("x2") } | Join-String
```

Output: `92742159e12e44c8`

### Method 2: Using Visual Studio

1. Open **Developer Command Prompt**
2. Run: `sn -T "path\to\assembly.dll"`

### Method 3: From Error Message

The error message usually includes it:
```
PublicKeyToken=92742159e12e44c8
```

## Common Issues

### Issue 1: Binding Redirect Not Applied

**Symptom**: Still getting assembly load errors after adding redirect

**Solution**:
1. Verify `app.config` exists in project root
2. Rebuild (not just Build)
3. Check `bin\Debug\data-foundry.dll.config` exists
4. Restart Visual Studio experimental instance

### Issue 2: Wrong Version in Redirect

**Symptom**: Different assembly load error

**Solution**:
Use the ACTUAL assembly version from `bin\Debug`, not the NuGet package version:

```powershell
# Get the real version
[Reflection.Assembly]::LoadFile("$PWD\bin\Debug\Azure.Core.dll").GetName().Version
```

### Issue 3: Multiple Versions in Output

**Symptom**: Build warning about multiple versions

**Solution**:
Usually safe to redirect to the HIGHEST version:
```xml
<bindingRedirect oldVersion="0.0.0.0-9999.9999.9999.9999" newVersion="X.Y.Z.0" />
```

## VSIX-Specific Considerations

### Where Config Goes

- **Development**: `bin\Debug\data-foundry.dll.config`
- **Installed VSIX**: Extension install directory alongside `data-foundry.dll`

### Ensuring Config Is Included in VSIX

In `.csproj`:
```xml
<None Include="app.config">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
  <IncludeInVSIX>true</IncludeInVSIX>
</None>
```

(This should already be configured automatically by the build system)

## Testing Binding Redirects

### Quick Test in Immediate Window

```csharp
// This should NOT throw an error
var assembly = System.Reflection.Assembly.Load("Azure.Core, Version=1.47.3.0, Culture=neutral, PublicKeyToken=92742159e12e44c8");
assembly.GetName().Version.ToString() // Should show "1.49.0.0"
```

### Full Integration Test

1. Build extension
2. Install VSIX
3. Open Data Foundry tool window
4. Click "Detect Changes"
5. Should work without assembly load errors

## Automated Binding Redirect Generation

Visual Studio can auto-generate binding redirects:

1. Right-click project ? **Properties**
2. **Build** tab
3. Check **Auto-generate binding redirects**

?? **Note**: This works for regular projects but may not work perfectly for VSIX projects. Manual configuration is more reliable.

## Summary of Current Configuration

Our `app.config` prevents these runtime errors:

? **Microsoft.Identity.Client version conflicts** (4.76 vs 4.78)  
? **Azure.Core version conflicts** (1.47.3 vs 1.49.0)  
? **System.Memory version conflicts**  
? **System.Text.Json version conflicts**  
? **System.Runtime.CompilerServices.Unsafe conflicts**

All Azure authentication and SQL operations should work without assembly load errors!

## Maintenance

When updating NuGet packages:
1. Rebuild solution
2. Test in experimental instance
3. If you see new assembly load errors, repeat the process above
4. Add new binding redirects as needed

## References

- [Assembly Binding Redirection (Microsoft Docs)](https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/redirect-assembly-versions)
- [How to: Enable and Disable Automatic Binding Redirection](https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/how-to-enable-and-disable-automatic-binding-redirection)
