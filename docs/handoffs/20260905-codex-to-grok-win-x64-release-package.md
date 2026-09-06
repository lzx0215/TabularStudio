# Issue #25: Codex → Grok / Project Owner — Windows release candidate

Execution date: 2026-09-06 (Asia/Shanghai). Filename follows the requested 20260905 handoff name.

## Status and source

- Issue: https://github.com/lzx0215/TabularStudio/issues/25
- Branch: `task/25-win-x64-self-contained-release`
- Final QA baseline: `95b6e86eb920cc3038cb796b7b14e13c8505404a`
- Latest main after `git switch main` / `git pull --ff-only origin main`: exactly that baseline; clean working tree. No post-QA product changes.
- Actual app-code commit: `95b6e86eb920cc3038cb796b7b14e13c8505404a`.
- Package configuration commit: `03abdb8d36e725d56a86f3409d70e8b2e4e5b006`. The artifact was built with these exact configuration file contents before committing; no product code changed.
- Issue #23 is closed/completed; its live comment reports MVP Final QA PASS, build 0 warnings / 0 errors and 203 passed / 0 failed / 0 skipped at the baseline above.
- Build Package Status: **PASS** — verified win-x64 self-contained single-file EXE and ZIP.
- Clean-machine Verification Status: **NOT YET VERIFIED** — 尚未在无 .NET 环境机器验证.
- Overall release acceptance: **PENDING**, primarily disconnected-operation and clean-machine checks. Draft PR; do not merge, tag, publish a GitHub Release, upload assets, or close the milestone in this task.

## Configuration and reproduction

| Item | Verified value |
| --- | --- |
| SDK | 10.0.400 (global.json requests 10.0.100 / latestFeature) |
| Target | .NET 10 WPF, Windows x64 |
| Configuration | Release |
| RuntimeIdentifier | win-x64 |
| SelfContained | true |
| PublishSingleFile | true |
| IncludeNativeLibrariesForSelfExtract | true |
| PublishTrimmed | false |
| ReadyToRun / NativeAOT | Not enabled |
| Version / InformationalVersion | 0.1.0 |
| AssemblyVersion / FileVersion | 0.1.0.0 |
| ProductVersion read from EXE | 0.1.0 |
| Included frameworks in generated runtimeconfig | Microsoft.NETCore.App 10.0.11; Microsoft.WindowsDesktop.App 10.0.11 |

Exact successful command, run from repository root:

```powershell
dotnet publish src/TabularStudio.App/TabularStudio.App.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=win-x64-self-contained
```

The tracked profile provides all version, output and bundling settings. No csproj changes. Existing ignore rules already cover artifacts/publish/bin/obj; the sole .gitignore change permits this exact non-secret pubxml file. App AssemblyName produces the requested TabularStudio.exe without changing source namespaces or UI. Native libraries are bundled and extracted on launch; a native WPF module was observed under `D:\Temp\lyl\.net\TabularStudio\Y4gsBydTRkJC\PresentationNative_cor3.dll`.

The first publish exposed a referenced Core PDB despite CopyOutputSymbolsToPublishDirectory=false. The profile now also removes PDB items after ComputeFilesToPublish. After removing the identified stale publish PDB and publishing again, the full output tree contains exactly one file. Build PDBs remain outside the package. No required DLL/config/content was deleted to simulate single-file.

Reproduction and acceptance checklist: [release-win-x64.md](../release-win-x64.md).

## Artifacts

Publish directory: `D:\aiproject\TabularStudio\artifacts\publish\win-x64\`

| Artifact | Bytes | SHA256 |
| --- | ---: | --- |
| TabularStudio.exe | 149976794 | 629EE06BB19ECB6ECC343DAD51726C5F64D4D4F2BF9B55AC804C2B5675C1BC69 |
| TabularStudio-v0.1.0-win-x64.zip | 63685695 | 8DC2767300B3DA0256A5D6E806F2E9FC5932605F16D2A5B90A8E3FAB957423FE |

ZIP path: `D:\aiproject\TabularStudio\artifacts\TabularStudio-v0.1.0-win-x64.zip`.

ZIP inspected using ZipFile.OpenRead: exactly one root entry, `TabularStudio.exe`, uncompressed length 149976794 bytes, compressed entry length 63685563 bytes. No source, tests, Git files, PDB, synthetic workbook, configuration secrets, handoff or internal docs. Only the published EXE was passed to Compress-Archive. Artifacts and all temporary GUI helpers/synthetic data are ignored and not committed or uploaded.

Single-file structure: **PASS**. Self-contained configuration/runtime metadata: **PASS**. Neither proves execution on a machine without .NET.

## Validation evidence

| Check | Status | Evidence and limits |
| --- | --- | --- |
| Release build | PASS | dotnet build TabularStudio.sln -c Release: 0 warnings / 0 errors |
| Release tests | PASS | dotnet test TabularStudio.sln -c Release --no-build: 203 passed / 0 failed / 0 skipped |
| Publish | PASS | Successful exit, no reported warning/error |
| Published EXE startup | PASS | Started actual publish EXE, PID 7252; nonzero window handle, responding, title TabularStudio - 本地离线表格工具; default format page observed through Windows UI Automation |
| Startup close | PASS | CloseMainWindow succeeded, process exited with code 0 |
| Independent-directory test | PASS (startup only) | ZIP extracted to fresh D:\TabularStudioReleaseTest\v0.1.0-20260906-012624; identical EXE hash; launched with that directory as working directory, PID 5028, responding window; normal exit 0 |
| Format EXE Smoke Test | PASS (basic path) | Through actual GUI file dialog selected synthetic master.xlsx; Sheet/Preview loaded Alpha/Beta; Start generated master_格式统一.xlsx (5939 bytes). Output XML: Alpha trimmed; 001/002 preserved |
| Matching EXE Smoke Test | PASS (basic path) | Master was actual format result; Reference was a separate synthetic reference.xlsx; selected ID (A列) on both condition selectors and Department (B列) return field; Start generated master_格式统一_匹配结果.xlsx (6046 bytes) |
| Matching result | PASS | Actual output XML contains Synthetic-A / Synthetic-B in the returned Department column and 匹配成功 for both rows; UI reported 数据匹配完成 |
| Cross-feature Smoke Test | PASS | Clicked 作为主表送入数据匹配 ->; matching page loaded the format result as Master. Reference/conditions/return field were subsequently selected and execution was explicitly invoked |
| Existing Output | PASS (cancel and explicit overwrite) | Re-enabled Ready state by changing return-field selection. Cancel preserved output SHA256. Explicit overwrite completed successfully. Save-as branch NOT RUN |
| Open generated file | PASS | App button opened master_格式统一_匹配结果.xlsx - WPS Office; only that synthetic workbook window was requested to close |
| Open containing folder | PASS | App button opened Explorer at file:///D:/aiproject/TabularStudio/artifacts/release-evidence/synthetic; verified using Shell.Application and closed the test folder window |
| Input safety in this smoke | PASS | Original synthetic master.xlsx and reference.xlsx hashes unchanged after processing and explicit result overwrite |
| App cleanup after functional smoke | PASS | Requested normal close, WaitForExit returned true, zero remaining TabularStudio processes. Exit code was not captured in this last closure; the two startup-only closures separately returned 0 |
| Offline source inspection | PASS (limited static check) | No matches for HttpClient, HTTP URLs, Telemetry, AutoUpdate, WebRequest or Socket in src C# files. No online capability added |
| Disconnected EXE startup/format/matching | NOT RUN | Development workstation was not disconnected; static inspection and unchanged product code are not disconnected runtime evidence |
| No-.NET Windows x64 clean machine | NOT YET VERIFIED | Only development machine available; independent directory does not replace this test |
| git diff --check | PASS | Checked configuration and final documentation diff |

GUI operations used Windows UI Automation plus the native Open File dialog filename control. Helpers remained in ignored artifacts/release-evidence; no automation framework or test dependency was added to the repository. Early UI Automation option selection hit unsupported provider patterns; selecting the actual SelectionItem provider succeeded. Only observed successful operations are marked PASS. Synthetic input consists of invented IDs 001/002, Alpha/Beta and Synthetic-A/Synthetic-B; no real business or patient data was used.

## Scope and handoff

- Contract changes = none.
- Product behavior changes = none.
- Core, FormatStandardization, DataMatching, UI source/layout, requirements, Approved Contract and Processing Baseline unchanged.
- Tracked changes are limited to publish profile, its exact ignore exception, release documentation and this handoff.
- Git tag = not created.
- GitHub Release = not created.
- Release asset upload = not performed.
- Merge = not performed.

Grok: review Issue #25 scope and the distinction between build success and package acceptance. Project Owner: use the ZIP/hash above on Windows x64 without .NET SDK/Runtime, disconnect Internet in that test environment, verify launch, format, matching and cross-feature flow, plus Existing Output save-as. Record machine/OS, runtime absence, exact ZIP hash and results. Independent startup passed, but full workflow was exercised from the publish directory, not repeated from the independent directory.

Remaining verification gates: disconnected runtime smoke and true no-.NET clean-machine smoke; Existing Output save-as branch is also pending. No observed single-file compatibility failure requires a folder fallback. Do not claim full release acceptance or proceed to a formal release until the pending evidence is reviewed.
