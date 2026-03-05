Phase 70 Step 4: Per-Test RFC Traceability — 424 comments, 2514 tests pass

- Add `/// RFC X §Y.Z — <brief requirement>` doc comment above every `[Fact]` and
  `[Theory]` attribute in the 14 RFC-organized test files (424 total test methods).

- RFC9113/ files (9 files, 216 tests): sections RFC 9113 §3.4, §5.1, §6.10 and
  RFC 7540 §4.1, §4.2, §5.2, §5.4, §6.5, §6.8.

- RFC7541/ files (5 files, 208 tests): sections RFC 7541 Appendix A, §4, §5.1, §5.2,
  §6, §6.3, §7.1 and RFC 7540 §6.5.2.

- For files with RFC-prefixed DisplayNames (e.g. RFC9113-3.4-CP-001:), section parsed
  directly from the prefix. For short-code files (FC-, HV-, EM-, ST-, etc.), section
  mapped from per-file context table.

- Update IMPLEMENTATION_PLAN.md: mark Step 4 as ✅.

Zero regressions: 2514 tests pass (2083 unit + 407 integration + 24 stream).

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
