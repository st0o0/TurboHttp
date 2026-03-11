# Iteration 09 — TASK-028: Http11Engine Cookie Integration Tests

## Task
**ID:** TASK-028
**Title:** Http11Engine Cookie Integration Tests

## Commands & Outcomes

1. **Read** `KestrelFixture.cs` L489-586 — reviewed all cookie routes (set, set-secure, set-httponly, set-samesite, set-expires, set-domain, set-path, echo, set-multiple, delete, set-and-redirect)
2. **Read** existing Http11 integration tests (01-04) — studied patterns: TestKit base, KestrelFixture, SendAsync helper, pipeline materialisation
3. **Read** `CookieJar.cs` — confirmed API: `ProcessResponse(uri, response)`, `AddCookiesToRequest(uri, ref request)`, `Count`, `Clear()`
4. **Write** `src/TurboHttp.IntegrationTests/Http11/05_Http11CookieTests.cs` — 12 tests covering all acceptance criteria
5. **Run** `dotnet test --filter "FullyQualifiedName~Http11CookieTests"` — **12/12 passed** in 1.03s

## Acceptance Criteria

- [x] File created: `src/TurboHttp.IntegrationTests/Http11/05_Http11CookieTests.cs`
- [x] 12 tests: store+send, accumulate, Path, Domain, Secure, HttpOnly, Max-Age=0, expired, multiple Set-Cookie, sorting, redirects, SameSite
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11CookieTests"`

## Deviations
None.
