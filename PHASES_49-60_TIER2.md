# TIER 2 – HTTP Core Compliance Task List (Phases 49–60)

---

# Phase 49–50: Content-Encoding Handling

(**RFC 9110 §8.4**)

## 🎯 Objective

Correct processing of `Content-Encoding` according to HTTP Semantics.

## MUST Requirements

- [ ] Send `Accept-Encoding` if the client supports compression
- [ ] Support stacked encodings (decode in reverse order)
- [ ] Properly handle `identity`
- [ ] Unknown encodings:

    * Request → 415 (if sending unsupported encoding)
    * Response → fail or pass through safely
- [ ] Remove `Content-Encoding` after successful decompression
- [ ] Update `Content-Length` after decompression
- [ ] Support streaming decompression (avoid full buffering when possible)

## Edge Cases

- [ ] Multiple encodings (e.g., `gzip, br`)
- [ ] Empty bodies
- [ ] 204 and 304 MUST NOT contain a body
- [ ] HEAD responses MUST NOT include a body
- [ ] Do not confuse `Transfer-Encoding` with `Content-Encoding`

---

# Phase 51–52: Redirect Handling

(**RFC 9110 §15.4**)

## 🎯 Objective

Semantically correct redirect behavior.

## MUST Requirements

- [ ] Support: 301, 302, 303, 307, 308
- [ ] 303 → always switch to GET
- [ ] 307/308 → preserve method and body
- [ ] 301/302 → historical GET rewrite handled intentionally
- [ ] Resolve relative `Location` headers correctly
- [ ] Enforce `MaxRedirects`
- [ ] Detect redirect loops

## Security Requirements

- [ ] Do NOT forward `Authorization` header across origins
- [ ] Optionally block HTTPS → HTTP downgrade
- [ ] Re-evaluate cookies for each new redirect URI

---

# Phase 53–54: Cookie Management

(**RFC 6265**)

## 🎯 Objective

Full RFC 6265 compliance.

## MUST Requirements

- [ ] Implement domain matching per RFC 6265 §5.1.3
- [ ] Distinguish host-only vs domain cookies
- [ ] Implement path matching correctly
- [ ] Correctly interpret `Expires` and `Max-Age`
- [ ] Send `Secure` cookies only over HTTPS
- [ ] Respect `HttpOnly`
- [ ] Correctly process multiple `Set-Cookie` headers

## SHOULD

- [ ] Support `SameSite`
- [ ] Implement public suffix protection

## MUST NOT

- [ ] Use naive `EndsWith()` domain matching
- [ ] Store cookies without domain/path scoping

---

# Phase 55–56: Connection Management

(**RFC 9112 §9 – HTTP/1.1**)

## 🎯 Objective

Correct persistent connection behavior.

## MUST Requirements

- [ ] Persistent connections enabled by default
- [ ] Respect `Connection: close`
- [ ] Correctly interpret `Keep-Alive`
- [ ] Do NOT reuse connection when:

    * Response body not fully consumed
    * Protocol errors occurred
    * Connection explicitly closed
- [ ] Enforce per-host connection limits

## HTTP/2 / HTTP/3 Considerations

- [ ] Support multiplexing behavior
- [ ] Do not apply HTTP/1.1 pooling logic to HTTP/2 streams

---

# Phase 57: Logging (Spec-Neutral but Safe)

## MUST NOT

- [ ] Log sensitive headers (Authorization, Cookie)
- [ ] Log full bodies by default
- [ ] Alter request/response semantics

---

# Phase 58: Timeout & Retry Policies

(**RFC 9110 §9.2 – Idempotency**)

## 🎯 Objective

Semantically safe retries.

## MUST Requirements

 -[ ] Automatically retry only idempotent methods:

    * GET
    * HEAD
    * PUT
    * DELETE
    * OPTIONS

- [ ] Do NOT automatically retry POST

- [ ] Retry only on:

    * Network failures
    * 408
    * 503 (+ optionally respect Retry-After)

- [ ] Respect `Retry-After` header

## MUST NOT

- [ ] Retry partial streamed bodies without rewind support
- [ ] Blindly resend non-idempotent requests

---

# Phase 59: Cross-Feature Integrity Validation

Ensure correct interaction between features:

- [ ] Redirect + Cookies → correct domain re-evaluation
- [ ] Redirect + Authorization → strip on cross-origin
- [ ] Decompression + Caching → entity integrity preserved
- [ ] Pooling + Timeout → no leaked connections
- [ ] Retry + Streaming → only retry rewindable bodies
- [ ] HEAD → never expose body even if decompressed

---

# Phase 60: Final HTTPWG Core Validation Gate

## RFC 9110 Validation

- [ ] All methods handled correctly
- [ ] All status codes interpreted correctly
- [ ] Headers treated case-insensitively
- [ ] Multiple header combination rules respected
- [ ] Message body rules fully implemented
- [ ] Proper handling of 1xx responses
- [ ] 204/304 without body
- [ ] HEAD without body

## RFC 9112 (HTTP/1.1)

- [ ] Correct chunked decoding
- [ ] Chunk extensions safely ignored (if unsupported)
- [ ] Trailer fields handled or discarded safely
- [ ] Content-Length conflicts handled securely

## RFC 9111 (if caching implemented)

-[ ] Correct Cache-Control parsing
-[ ] Respect `no-store`
-[ ] Respect `must-revalidate`
-[ ] Implement `Vary` handling

---

# 🚨 Definition of Done – HTTP Core Compliant

Your client is **HTTP Core compliant** when:

-[ ] No behavior contradicts RFC 9110 semantics
-[ ] Redirect handling is secure
-[ ] Cookie matching is RFC-correct
-[ ] No incorrect body handling
-[ ] No keep-alive protocol violations
-[ ] Retry logic respects idempotency rules

---