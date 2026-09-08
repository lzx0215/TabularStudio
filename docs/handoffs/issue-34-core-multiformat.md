# Issue #34 Core multi-format handoff

Source: GitHub Issue #34; branch `issue-34-core-multiformat`. Exact reviewed commit is recorded in the PR, not self-referenced in this file.

## Delivered

Core inspection, preview, standardization and matching support XLSX, real XLS and UTF-8 CSV. Outputs retain the input/master format. All nine master/reference combinations are exercised. Input protection, existing output confirmation and existing XLSX algorithms remain in place. No batch, WPF or optional-rule default changes.

The Owner's 2026-09-08 instruction delegates dependency/security remediation within unchanged requirements. ADR records NPOI 2.7.4, CsvHelper 33.1.0 and explicit security version constraints. NSax is absent from the resolved graph. License snapshots and attribution guidance are in `../decisions/issue34-licenses/`.

## Verification performed

- SDK 10.0.400; solution restore PASS; build PASS (0 warnings, 0 errors).
- Solution automated regression: 225 passed, 0 failed, 0 skipped.
- `dotnet list TabularStudio.sln package --vulnerable --include-transitive --no-restore`: no known vulnerable packages reported by the configured source.
- Independent public-Core harness: `dotnet run --project tools/Issue34.FunctionalQA/Issue34.FunctionalQA.csproj --no-restore`.
- FT34-01 through FT34-16: PASS in that harness, including all nine format combinations, actual output reopen, input SHA256 checks, sheet boundaries, conflicts, formula and layout preservation. This is Core functional QA, not WPF/UI functional QA.
- All fixtures are runtime-generated synthetic/non-sensitive data and cleaned after execution.
- VC34-01: final technical decision and delegated authorization recorded in ADR. VC34-02: processing rules, contracts and architecture synchronized. VC34-03/04: actual build/test results above.

## Contract and receiving role

WorksheetSource.WorksheetName, PreviewTable.WorksheetName and ProcessedWorksheetName are nullable for CSV. CSV inspection returns no worksheets. Excel still requires a valid sheet. No new error enum or request option. CSV same-file/different-sheet semantics do not exist.

UI reviewer task supplied design APPROVE with no UI blocker; actual diff review is requested against the pushed SHA. This is a reviewer-role response, not a claim of independent Antigravity model identity. The UI owner can integrate these contracts in its separately scoped work; no UI implementation is included here.

## Remaining boundaries

- Network physically disconnected runtime: NOT VERIFIED. Local execution without restore is not a disconnected-network test.
- Clean-machine deployment and distributable license packaging: NOT VERIFIED; no release artifact created.
- Arbitrary legacy BIFF, macros and embedded objects: NOT VERIFIED; synthetic formula/style/sheet coverage is not a universal preservation guarantee.
- CSV encoding/parser/record decisions and memory model limits are documented in processing-rules section 11; unsupported encodings are not advertised as supported.
- Next: actual diff/scope review and Owner Gate 2 for the exact PR SHA. Do not merge without approval. Post-merge main verification and applicable release QA remain required.
