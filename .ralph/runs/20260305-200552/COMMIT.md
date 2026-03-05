Phase 70 Step 3: RFC-Based Folder Structure — 14 test files reorganized, 2514 tests pass

- Create RFC9113/ subdirectory with 9 moved files: connection preface, frame parsing,
  stream state machine, settings, flow control, headers validation, error mapping,
  GOAWAY/RST_STREAM, and continuation frame tests.

- Create RFC7541/ subdirectory with 5 moved files: static table, dynamic table,
  Huffman decoder, header block decoding, and table size / header list size tests.

- No namespace changes, no logic changes, no .csproj changes. SDK-style project
  auto-includes .cs files in subdirectories.

- Update IMPLEMENTATION_PLAN.md: mark Step 3 as ✅ with moved file mapping.

Zero regressions: 2514 tests pass (2083 unit + 407 integration + 24 stream).

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
