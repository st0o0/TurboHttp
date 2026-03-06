Phase 3: Mark 01_ConnectionPrefaceTests as migrated (already clean)

01_ConnectionPrefaceTests.cs had 0 Http2Decoder references — the
migration was completed in a prior commit. Update IMPLEMENTATION_PLAN.md
to check off Phase 3. All 23 ConnectionPreface tests pass; RFC9113 suite
failure count unchanged from baseline (43 pre-existing failures).
