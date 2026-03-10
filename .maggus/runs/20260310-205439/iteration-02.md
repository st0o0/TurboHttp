# Iteration 02 — TASK-H2C-04: Forbidden Header Stripping End-to-End

**Task:** TASK-H2C-04 — Forbidden Header Stripping End-to-End
**File created:** `src/TurboHttp.StreamTests/Http20/Http20ForbiddenHeaderRfcTests.cs`

## Commands Run

1. `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~ForbiddenHeader"` → **5/5 passed**

## Acceptance Criteria

- [x] `H2FH-001`: `connection` header → not present in wire format
- [x] `H2FH-002`: `transfer-encoding` header → not present in wire format
- [x] `H2FH-003`: `upgrade` header → not present in wire format
- [x] `H2FH-004`: `keep-alive` header → not present in wire format
- [x] `H2FH-005`: Custom header (`x-custom`) → present in wire format

## Implementation Notes

- Tests follow same pattern as `Http20PseudoHeaderRfcTests`: run requests through `StreamIdAllocatorStage → Request2FrameStage`, decode HPACK headers, assert forbidden headers are absent.
- The encoder's `IsForbidden()` method in `Http2RequestEncoder.cs` (line 443) already strips connection-specific headers per RFC 9113 §8.2.2.
- No deviations or skips.
