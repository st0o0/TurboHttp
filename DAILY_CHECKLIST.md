# Daily Work Checklist for RALPH

## 🌅 Morning Routine (15 min)

### 1. Review Current Sprint Status
- [ ] Check current phase and week
- [ ] Review yesterday's progress
- [ ] Identify blockers or issues
- [ ] Update task board/tracker

### 2. Pull Latest Changes
```bash
git fetch origin
git pull origin main
dotnet restore
```

### 3. Run Quick Smoke Tests
```bash
dotnet test --filter "Category=Smoke"
# Should complete in < 30 seconds
```

### 4. Review Today's Tasks
- [ ] Identify 2-3 main tasks for today
- [ ] Prioritize based on phase plan
- [ ] Estimate time for each task

---

## 💻 Development Workflow

### For Each New Feature/Component:

#### Step 1: Understand Requirements (20 min)
- [ ] Read relevant RFC section(s)
- [ ] Identify MUST vs SHOULD requirements
- [ ] Note edge cases and error conditions
- [ ] Review existing code patterns

#### Step 2: Design API (15 min)
- [ ] Sketch method signatures
- [ ] Consider Zero-Allocation patterns
- [ ] Plan Span<byte> usage
- [ ] Document expected behavior

#### Step 3: Write Tests FIRST (Test-Driven Development)
```csharp
// Example test structure:
[Theory]
[InlineData("GET /path HTTP/1.1\r\n\r\n", "GET", "/path", "HTTP/1.1")]
[InlineData("POST / HTTP/1.1\r\n\r\n", "POST", "/", "HTTP/1.1")]
public void RequestLine_ValidInput_ParsesCorrectly(
    string input, 
    string expectedMethod, 
    string expectedPath, 
    string expectedVersion)
{
    // Arrange
    var parser = new Http11Parser();
    var bytes = Encoding.ASCII.GetBytes(input);
    
    // Act
    var result = parser.TryParseRequest(bytes, out var request);
    
    // Assert
    Assert.True(result);
    Assert.Equal(expectedMethod, request.Method);
    Assert.Equal(expectedPath, request.Path);
    Assert.Equal(expectedVersion, request.Version);
}
```

#### Step 4: Write Implementation
- [ ] Start with simplest case
- [ ] Add error handling
- [ ] Optimize for Zero-Allocation
- [ ] Add XML documentation comments

#### Step 5: Refactor & Optimize
- [ ] Remove unnecessary allocations
- [ ] Use Span<byte> where possible
- [ ] Use ArrayPool for temporary buffers
- [ ] Benchmark hot paths

#### Step 6: Code Review (Self-Review)
- [ ] Check XML documentation
- [ ] Verify error handling
- [ ] Check for edge cases
- [ ] Run static analysis
- [ ] Verify test coverage

---

## ✅ Daily Checklist

### Before Each Commit:
- [ ] All tests pass (`dotnet test`)
- [ ] Code coverage > 90% for new code
- [ ] No compiler warnings
- [ ] XML documentation on public APIs
- [ ] Git commit message follows convention

### Commit Message Format:
```
[Component] Brief description

- Detailed change 1
- Detailed change 2

Refs: RFC 7230 §3.1.1
Tests: 8 new tests added
Coverage: 95% line, 90% branch
```

### Examples:
```
[HTTP1.1-Parser] Implement request-line parsing

- Add RequestLine struct
- Implement TryParseRequestLine method
- Handle whitespace and CRLF
- Add validation for method and version

Refs: RFC 7230 §3.1.1
Tests: 8 new tests added
Coverage: 95% line, 90% branch
```

---

## 🧪 Testing Best Practices

### Test Categories:

#### 1. Unit Tests (per-method)
```csharp
[Fact]
public void ParseMethod_ValidMethod_ReturnsTrue()
{
    // Test single method in isolation
}
```

#### 2. Integration Tests (end-to-end)
```csharp
[Fact]
public void EncodeAndDecode_Request_RoundTrip()
{
    // Test full request encode → decode cycle
}
```

#### 3. Property-Based Tests (fuzzing)
```csharp
[Property]
public Property ParseMethod_RandomValidInput_NeverThrows(
    NonEmptyString method)
{
    // Test with random valid inputs
}
```

#### 4. Conformance Tests (RFC validation)
```csharp
[Theory]
[MemberData(nameof(RFC7230_Section3_1_1_TestCases))]
public void RFC7230_Section3_1_1_Conformance(
    string testId, 
    string input, 
    bool shouldPass)
{
    // Test against RFC requirements
}
```

### Test Organization:
```
tests/
├── Unit/
│   ├── Http11/
│   │   ├── ParserTests.cs
│   │   ├── EncoderTests.cs
│   │   └── ChunkedEncodingTests.cs
│   └── Http2/
│       ├── FrameParserTests.cs
│       ├── HpackTests.cs
│       └── FlowControlTests.cs
├── Integration/
│   ├── Http11EndToEndTests.cs
│   └── Http2EndToEndTests.cs
├── Conformance/
│   ├── RFC7230Tests.cs
│   ├── RFC7540Tests.cs
│   └── RFC7541Tests.cs
└── Performance/
    └── Benchmarks.cs
```

---

## 🔍 Code Quality Checks

### Before Pushing Code:

#### 1. Run Full Test Suite
```bash
dotnet test --logger "console;verbosity=detailed"
```

#### 2. Check Code Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
# Open coverage report
reportgenerator -reports:coverage.opencover.xml -targetdir:coverage-report
```

#### 3. Run Static Analysis
```bash
# Enable all analyzers
dotnet build /p:RunAnalyzersDuringBuild=true /p:TreatWarningsAsErrors=true
```

#### 4. Run Benchmarks (for critical paths)
```bash
cd benchmarks
dotnet run -c Release --filter "*ParserBenchmark*"
```

#### 5. Memory Profiling (weekly)
```bash
# Use dotMemory or similar
dotnet run -c Release --profile
```

---

## 📊 Progress Tracking

### Daily Log Template:
```markdown
# Date: 2024-MM-DD
## Phase: X - [Phase Name]
## Week: X/14

### Today's Goals:
1. [ ] Task 1
2. [ ] Task 2
3. [ ] Task 3

### Completed:
- ✅ Implemented X
- ✅ Added Y tests
- ✅ Fixed bug Z

### In Progress:
- 🔄 Feature A (60% complete)
- 🔄 Refactoring B (30% complete)

### Blocked:
- ❌ Issue with C (waiting for decision on X)

### Tomorrow's Plan:
1. Complete feature A
2. Start on feature D
3. Write documentation for B

### Metrics:
- Tests added: 12
- Tests passing: 145/145 (100%)
- Code coverage: 92%
- Lines of code: +250, -30
```

---

## 🚨 When Things Go Wrong

### Debugging Checklist:

#### 1. Test Failure
- [ ] Read error message carefully
- [ ] Check test input data
- [ ] Verify expected vs actual
- [ ] Add console logging
- [ ] Use debugger breakpoints
- [ ] Isolate the failing code

#### 2. Performance Issue
- [ ] Run benchmark to quantify
- [ ] Use profiler (dotTrace, PerfView)
- [ ] Check for allocations
- [ ] Look for N+1 patterns
- [ ] Review algorithm complexity
- [ ] Compare with baseline

#### 3. Memory Leak
- [ ] Use memory profiler (dotMemory)
- [ ] Check for unclosed streams
- [ ] Verify ArrayPool returns
- [ ] Look for event subscriptions
- [ ] Check for circular references

#### 4. RFC Compliance Issue
- [ ] Re-read RFC section
- [ ] Check MUST vs SHOULD
- [ ] Compare with other implementations
- [ ] Ask for clarification
- [ ] Document decision if ambiguous

---

## 🎯 Weekly Review (Friday, 30 min)

### Week-End Checklist:
- [ ] Review week's progress against plan
- [ ] Update test coverage metrics
- [ ] Run full benchmark suite
- [ ] Update documentation
- [ ] Clean up TODO comments
- [ ] Plan next week's priorities
- [ ] Update project board

### Weekly Metrics:
```markdown
## Week X Summary

### Progress:
- Phase: X/6 (Y%)
- Tasks completed: Z
- Tests added: N
- Code coverage: X%
- Lines of code: +XXX, -YYY

### Key Achievements:
1. Completed A
2. Implemented B
3. Fixed critical bug C

### Challenges:
1. Issue with X (resolved/ongoing)
2. Performance bottleneck in Y (resolved/ongoing)

### Next Week Focus:
1. Priority task A
2. Priority task B
3. Priority task C

### Blockers:
- None / [describe blocker]
```

---

## 📚 Reference Quick Links

### RFCs:
- [RFC 7230 - HTTP/1.1 Message Syntax](https://tools.ietf.org/html/rfc7230)
- [RFC 7231 - HTTP/1.1 Semantics](https://tools.ietf.org/html/rfc7231)
- [RFC 7540 - HTTP/2](https://tools.ietf.org/html/rfc7540)
- [RFC 7541 - HPACK](https://tools.ietf.org/html/rfc7541)

### Tools:
- [h2spec](https://github.com/summerwind/h2spec) - HTTP/2 conformance testing
- [h2load](https://nghttp2.org/documentation/h2load.1.html) - HTTP/2 load testing
- [BenchmarkDotNet](https://benchmarkdotnet.org/) - .NET benchmarking
- [dotMemory](https://www.jetbrains.com/dotmemory/) - Memory profiling

### Documentation:
- [.NET Span<T>](https://docs.microsoft.com/en-us/dotnet/api/system.span-1)
- [ArrayPool<T>](https://docs.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
- [Memory<T> and Span<T> usage guidelines](https://docs.microsoft.com/en-us/dotnet/standard/memory-and-spans/)

---

## 💡 Pro Tips

### Performance:
1. **Always benchmark before optimizing** - Don't guess, measure!
2. **Use Span<byte> for parsing** - Avoid string allocations
3. **Pool temporary buffers** - Use ArrayPool<byte>
4. **Cache static data** - Static tables, regex patterns, etc.
5. **Avoid LINQ in hot paths** - Use for loops instead

### Testing:
1. **Test edge cases first** - Empty, null, max values
2. **Test error paths** - Don't just test happy path
3. **Use Theory over Fact** - Parameterize similar tests
4. **Name tests descriptively** - MethodName_Scenario_ExpectedResult
5. **Keep tests fast** - Unit tests should run in milliseconds

### Code Quality:
1. **Follow SOLID principles** - Especially Single Responsibility
2. **Keep methods small** - Aim for < 20 lines
3. **Use meaningful names** - Be verbose if needed
4. **Document complex logic** - Future you will thank you
5. **Refactor continuously** - Don't accumulate technical debt

### Git:
1. **Commit often** - Small, focused commits
2. **Write good messages** - Explain why, not what
3. **Keep main stable** - All tests must pass
4. **Use branches** - Feature branches for large changes
5. **Review your own PRs** - Check diff before submitting

---

## 🎉 Milestones Celebration

### When you complete:
- ✅ **10 tests** → Take a 5-minute break
- ✅ **50 tests** → Treat yourself to coffee/snack
- ✅ **100 tests** → End day early or take longer lunch
- ✅ **Complete a phase** → Update LinkedIn, tweet about it!
- ✅ **Complete project** → You're a legend! 🏆

---

**Remember: Quality over speed. It's better to do it right than to do it fast!**

**You got this, RALPH! 💪**
