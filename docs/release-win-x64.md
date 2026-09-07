# Windows x64 MVP v0.1.0 package (Issue #25)

This procedure builds a local release candidate. It does not create a tag, GitHub Release, installer, or upload an asset. Package acceptance remains separate from build success.

This procedure's artifact acceptance is Project Owner Gate 3 in `docs/development-process.md`: Release / Artifact Acceptance. Acceptance must lock the exact filename + size + SHA256. Existing verification rules in this document are not relaxed by the process simplification.

## Baseline gate

Start with a clean checkout of latest main. Compare it with Final QA commit `95b6e86eb920cc3038cb796b7b14e13c8505404a`. If newer changes affect product code, UI, Core, contracts, or processing rules, stop for Project Owner review and renewed QA. Work on `task/25-win-x64-self-contained-release`.

## Build and publish

From the repository root, with the .NET 10 SDK selected by global.json:

```powershell
dotnet build TabularStudio.sln -c Release
dotnet test TabularStudio.sln -c Release --no-build
git diff --check
dotnet publish src/TabularStudio.App/TabularStudio.App.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=win-x64-self-contained
```

Stop if a command fails. Expected: build 0 warnings / 0 errors; tests 203 passed / 0 failed / 0 skipped.

The tracked profile sets win-x64, Release, SelfContained=true, PublishSingleFile=true, PublishTrimmed=false, Version/InformationalVersion=0.1.0 and AssemblyVersion/FileVersion=0.1.0.0. AssemblyName sets the requested executable filename to TabularStudio.exe; namespaces and UI are unchanged. Source revision suffixes are disabled for the requested exact informational version. PDB files remain available in build output but are excluded from publish output. ReadyToRun, NativeAOT, trimming and custom bundlers are not enabled.

IncludeNativeLibrariesForSelfExtract=true includes native runtime/WPF libraries in the bundle. At runtime .NET extracts native components into its extraction cache; single-file describes distribution, not zero runtime extraction. See [Microsoft single-file documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

Output: `artifacts/publish/win-x64/`. Use a fresh or previously inspected empty output directory for each candidate; never package stale output. The existing .gitignore covers artifacts, publish, bin, obj and PDB files; only this non-secret publish profile is excepted from the generic pubxml rule.

## Artifact verification

Verify the complete publish directory contains only TabularStudio.exe before claiming single-file PASS. If runtime DLL/config/content files remain, record them; do not remove required files to force a single-file result. If compatibility fails, report evidence and obtain Project Owner's choice before adopting folder publication.

Check FileVersion 0.1.0.0 and ProductVersion 0.1.0 using `(Get-Item <exe>).VersionInfo`. Record exact size and SHA256 using Get-FileHash -Algorithm SHA256.

Package only the verified publish executable as `artifacts/TabularStudio-v0.1.0-win-x64.zip`, with TabularStudio.exe at ZIP root. Inspect archive entries, extract to an independent empty directory, and compare extracted EXE hash with the original. Never include source, tests, .git, PDB, configuration secrets, workbooks, internal docs or handoff files.

## Published EXE acceptance checklist

Use only newly generated synthetic .xlsx files, outside the publish directory and ZIP. Launch the actual published EXE, then repeat from a copied independent directory with its working directory outside the repository.

- Startup: visible responsive window, default format standardization page.
- Format: browse synthetic workbook, select Sheet/HeaderRow, inspect Preview, execute and inspect *_格式统一.xlsx; verify input hash unchanged.
- Matching: select synthetic Master/Reference, Sheet/HeaderRow, condition and Return Fields; execute and inspect *_匹配结果.xlsx; verify both input hashes unchanged.
- Handoff: send the format result as Master to matching; confirm no automatic execution, then select Reference/conditions/fields and execute.
- Existing Output: test cancel, save-as and explicit overwrite of output, never input.
- Open generated file and open containing folder through the app.
- Close the app normally and check no tested process remains.
- Repeat startup, format and matching with Internet disconnected on a test machine. Do not disconnect the development workstation remotely without coordination.
- Repeat on Windows x64 with no .NET SDK/Runtime installed. Record Windows version, absence of .NET, artifact hash and actual results.

Configuration checks and startup on a development machine do not prove no-.NET clean-machine compatibility. Code inspection does not prove disconnected operation. Mark unavailable GUI, offline or clean-machine checks NOT RUN / NOT YET VERIFIED, and give the Project Owner the exact candidate hash to test. Do not report overall package acceptance PASS while required smoke checks remain pending.
