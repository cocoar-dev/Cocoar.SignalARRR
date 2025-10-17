# SignalARRR Modernization Roadmap

**Last Updated:** 2025-10-16  
**Status:** Planning Phase  
**Project:** SignalARRR - Enhanced SignalR Library

---

## Table of Contents
- [Project Overview](#project-overview)
- [Current State Analysis](#current-state-analysis)
- [Architecture & Components](#architecture--components)
- [Modernization Strategy](#modernization-strategy)
- [Implementation Phases](#implementation-phases)
- [Progress Tracking](#progress-tracking)
- [Dependencies & Considerations](#dependencies--considerations)

---

## Project Overview

### What is SignalARRR?

SignalARRR is an enhanced wrapper around Microsoft's SignalR library that adds several advanced features to make real-time communication more flexible, maintainable, and feature-rich.

### Core Features

1. **Method Organization via Classes**
   - Split hub methods into multiple classes by inheriting from `ServerMethods<T>`
   - Better code organization and separation of concerns
   - Avoid monolithic hub classes

2. **Granular Authorization**
   - Apply `[Authorize]` attributes on individual methods or entire classes
   - Per-method authentication validation
   - Automatic reference token validation on each call

3. **Bidirectional RPC (Request/Response)**
   - Server can invoke methods on clients and receive responses
   - Not just fire-and-forget messages
   - True request/response patterns from server to client

4. **Observable Streams**
   - Use `IObservable<T>` as return types
   - Support for `IAsyncEnumerable<T>` and `ChannelReader<T>`
   - Reactive Extensions integration

5. **Type-Safe Client Proxies**
   - Generate strongly-typed proxies from interfaces
   - Eliminate magic strings for method names
   - Compile-time safety

6. **Multi-Platform Support**
   - Server library (.NET 8.0)
   - .NET Client (.NET 8.0 + .NET Framework 4.7.2)
   - TypeScript/JavaScript client

---

## Current State Analysis

### Project Structure

```
SignalARRR/
├── source/
│   ├── SignalARRR.Common (13 C# files)          - Shared types, messages, attributes
│   ├── SignalARRR.Server (33 C# files)          - Server-side hub implementation
│   ├── SignalARRR.Client (14 C# files)          - .NET client library
│   ├── SignalARRR.ProxyGenerator (4 C# files)   - Dynamic proxy generation
│   └── SignalARRR.Typescript/                   - TypeScript/JavaScript client
├── tests/
│   ├── SignalARRR.Tests                         - Unit tests (xUnit)
│   ├── TestServer                               - Sample ASP.NET Core server
│   ├── TestClient                               - .NET client test app
│   ├── TestClient_FullFramework                 - .NET Framework 4.7.2 client
│   ├── TestShared                               - Shared test models
│   └── SignalARRR.Tests.SharedModels
├── build/                                       - NUKE build automation
└── .github/workflows/                           - CI/CD pipelines
```

### Technology Stack

**Current Stack:**
- .NET 8.0 / .NET Framework 4.7.2
- ASP.NET Core SignalR
- Newtonsoft.Json for serialization
- ImpromptuInterface for dynamic proxies
- doob.Reflectensions (custom reflection library) ⚠️ *Needs update*
- System.Reactive.Linq for observables
- xUnit for testing
- NUKE for build automation
- TypeScript 4.5.5 with tslint (deprecated)

**Key Dependencies:**
```xml
<PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.0" />
<PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
<PackageVersion Include="ImpromptuInterface" Version="8.0.4" />
<PackageVersion Include="doob.Reflectensions" Version="6.2.1-beta0013" /> ⚠️
<PackageVersion Include="doob.Reflectensions.CommonExtensions" Version="6.4.2" />
<PackageVersion Include="doob.Reflectensions.Json" Version="6.4.2" />
<PackageVersion Include="System.Reactive.Linq" Version="6.0.0" />
```

### Strengths ✅

- ✅ Well-structured, modular architecture
- ✅ Comprehensive feature set solving real problems
- ✅ Multi-platform support (Server, .NET Client, TypeScript)
- ✅ Test coverage exists
- ✅ Build automation with NUKE
- ✅ GitHub Actions CI/CD pipelines
- ✅ Clear separation of concerns
- ✅ Type-safe client proxies

### Issues & Technical Debt ⚠️

#### 1. Outdated Dependencies
- ⚠️ doob.Reflectensions library is outdated (built by same author)
- ⚠️ TypeScript tooling is old (v4.5.5)
- ⚠️ tslint is deprecated (should use eslint)
- ⚠️ Some NuGet packages may have newer versions

#### 2. Legacy Patterns
- 🔄 Uses Newtonsoft.Json instead of System.Text.Json
- 🔄 ImpromptuInterface for dynamic proxies (consider alternatives)
- 🔄 Runtime reflection instead of source generators

#### 3. Documentation
- 📝 README is minimal
- 📝 No API documentation
- 📝 No usage examples or tutorials
- 📝 No architecture diagrams

#### 4. Code Quality
- 🧹 Some commented-out code blocks
- 🧹 Inconsistent naming (HARRR vs SignalARRR)
- 🧹 Could use more inline documentation

#### 5. Testing
- 🧪 Test coverage could be expanded
- 🧪 No visible integration tests
- 🧪 No performance/load tests

#### 6. Missing Modern Features
- ❌ No health checks
- ❌ No metrics/telemetry (OpenTelemetry)
- ❌ No circuit breakers or resilience patterns
- ❌ Limited structured logging

#### 7. DevOps & Deployment
- 🐳 No containerization (Dockerfile)
- ☸️ No Kubernetes manifests
- 📦 Publishing only to private feed
- 🔒 No automated security scanning

---

## Architecture & Components

### Core Flow: Client → Server Method Invocation

```
┌─────────┐                                  ┌─────────┐
│ Client  │                                  │ Server  │
└────┬────┘                                  └────┬────┘
     │                                            │
     │ 1. Create ClientRequestMessage             │
     │    (method, args, authorization)           │
     ├───────────────────────────────────────────>│
     │                                            │
     │                                       2. MessageHandler
     │                                          .InvokeAsync()
     │                                            │
     │                                       3. Authorization
     │                                          Check (per-method)
     │                                            │
     │                                       4. Resolve method from
     │                                          MethodsCollection
     │                                            │
     │                                       5. Create instance
     │                                          (DI container)
     │                                            │
     │                                       6. Inject properties
     │                                          (Context, Clients, etc)
     │                                            │
     │                                       7. Execute method
     │                                            │
     │ 8. Response (result or stream)             │
     │<───────────────────────────────────────────┤
     │                                            │
```

### Server-to-Client RPC Flow

```
┌─────────┐                                  ┌─────────┐
│ Server  │                                  │ Client  │
└────┬────┘                                  └────┬────┘
     │                                            │
     │ 1. GetTypedMethods<T>()                    │
     │    creates dynamic proxy                   │
     │                                            │
     │ 2. Proxy call →                            │
     │    ServerRequestMessage                    │
     ├───────────────────────────────────────────>│
     │                                            │
     │                                       3. Execute registered
     │                                          OnServerRequest handler
     │                                            │
     │ 4. ClientResponseMessage                   │
     │<───────────────────────────────────────────┤
     │                                            │
     │ 5. Complete Task<T>                        │
     │                                            │
```

### Authentication & Authorization Flow

```
1. Client connects with initial token
2. ClientContext stores User + UserValidUntil timestamp
3. On method invocation:
   ├─ If UserValidUntil < Now → Challenge client for fresh token
   │  ├─ Server sends ChallengeAuthentication message
   │  ├─ Client responds with new token
   │  └─ Server validates and updates ClientContext
   └─ Else → Use cached principal
4. Evaluate authorization policy for specific method
5. Execute method if authorized
```

---

## Modernization Strategy

### Guiding Principles

1. **Maintain Backward Compatibility** where possible
2. **Incremental Migration** - small, testable changes
3. **Documentation First** - document before/during changes
4. **Test Coverage** - expand tests before refactoring
5. **Community Ready** - prepare for open-source consumption

### Decision: doob.Reflectensions Dependency

**Status:** 🔴 **CRITICAL DECISION NEEDED**

The `doob.Reflectensions` library is used extensively throughout SignalARRR for:
- Type reflection and method invocation
- JSON conversions
- Property injection
- Type extensions

**Options:**

**Option A: Update Reflectensions First (Recommended)** ⭐
- ✅ Ensures stable foundation
- ✅ Can modernize both projects with same patterns
- ✅ Reduces risk of compatibility issues
- ❌ Delays SignalARRR modernization
- **Timeline:** 1-2 weeks

**Option B: Update Reflectensions in Parallel**
- ✅ Faster overall progress
- ✅ Can test changes across both projects
- ❌ Higher complexity managing two projects
- ❌ Risk of breaking changes affecting both
- **Timeline:** 2-3 weeks (overlapping)

**Option C: Replace with Built-in .NET Reflection**
- ✅ Reduces external dependencies
- ✅ More maintainable long-term
- ❌ Significant refactoring required
- ❌ May lose some convenience features
- **Timeline:** 3-4 weeks

**Option D: Keep Current Version**
- ✅ No immediate work required
- ✅ Focus on other modernizations
- ❌ Technical debt remains
- ❌ May have compatibility issues with newer .NET

**Recommended Approach:** Option A - Update Reflectensions first, then modernize SignalARRR using the updated library.

---

## Implementation Phases

### 🔴 Phase 0: Foundation & Planning (Current)

**Goal:** Establish baseline, update dependencies, create documentation

#### Tasks:
- [x] Repository analysis complete
- [ ] Create this modernization roadmap
- [ ] Update doob.Reflectensions library
  - [ ] Upgrade to latest .NET
  - [ ] Fix any breaking changes
  - [ ] Update NuGet packages
  - [ ] Publish updated version
- [ ] Update SignalARRR to use new Reflectensions version
- [ ] Create ARCHITECTURE.md document
- [ ] Create CONTRIBUTING.md
- [ ] Update README.md with better examples

**Estimated Duration:** 2 weeks  
**Status:** 🟡 In Progress

---

### 🟡 Phase 1: Critical Updates

**Goal:** Update dependencies, fix security issues, improve stability

#### 1.1 Dependency Updates
- [ ] Update all NuGet packages to latest stable versions
  - [ ] Microsoft.AspNetCore.* packages
  - [ ] SignalR packages
  - [ ] System.Reactive.Linq
  - [ ] xUnit and test packages
  - [ ] NUKE build tools
- [ ] Update TypeScript to latest LTS version
- [ ] Migrate from tslint to eslint
- [ ] Update npm dependencies
- [ ] Update GitHub Actions workflow versions

#### 1.2 Security & Stability
- [ ] Run security audit on all packages
- [ ] Add Dependabot configuration
- [ ] Enable GitHub CodeQL scanning
- [ ] Add security policy (SECURITY.md)
- [ ] Review and fix any security warnings

#### 1.3 Build & CI/CD
- [ ] Verify NUKE build works with updates
- [ ] Update GitHub Actions to use latest runners
- [ ] Add code coverage reports
- [ ] Add automated changelog generation

**Estimated Duration:** 1 week  
**Status:** ⚪ Not Started

---

### 🔵 Phase 2: Code Modernization

**Goal:** Modernize code patterns, improve performance, reduce technical debt

#### 2.1 Serialization Strategy
- [ ] Evaluate System.Text.Json compatibility
- [ ] Create abstraction layer for serialization
- [ ] Add System.Text.Json support (alongside Newtonsoft.Json)
- [ ] Performance benchmarks
- [ ] Migration guide for users

#### 2.2 Proxy Generation
- [ ] Evaluate alternatives to ImpromptuInterface:
  - [ ] Castle.Core DynamicProxy
  - [ ] DispatchProxy (built-in)
  - [ ] Source Generators (compile-time)
- [ ] Proof of concept implementation
- [ ] Performance comparison
- [ ] Choose and implement replacement

#### 2.3 Code Quality
- [ ] Remove commented-out code
- [ ] Standardize naming conventions
- [ ] Add XML documentation comments
- [ ] Add code analyzers (Roslynator, StyleCop)
- [ ] Run and fix analyzer warnings
- [ ] Add EditorConfig for consistent formatting

#### 2.4 Error Handling
- [ ] Create custom exception hierarchy
- [ ] Improve error messages
- [ ] Add better exception documentation
- [ ] Add global exception handling

**Estimated Duration:** 2-3 weeks  
**Status:** ⚪ Not Started

---

### 🟢 Phase 3: Feature Enhancements

**Goal:** Add modern observability, resilience, and developer experience features

#### 3.1 Observability
- [ ] Add OpenTelemetry support
  - [ ] Tracing for method invocations
  - [ ] Metrics for connection counts, message rates
  - [ ] Logs correlation
- [ ] Add health check endpoints
- [ ] Structured logging improvements
- [ ] Add diagnostic event source
- [ ] Performance counters

#### 3.2 Resilience
- [ ] Integrate Polly for retry policies
- [ ] Add circuit breaker support
- [ ] Timeout configurations
- [ ] Backpressure handling
- [ ] Connection recovery improvements

#### 3.3 Developer Experience
- [ ] Add source generator for proxy creation (optional)
- [ ] Improve IntelliSense documentation
- [ ] Add code snippets
- [ ] Create Visual Studio extension (optional)
- [ ] Better error messages with suggestions

#### 3.4 Configuration
- [ ] Options validation
- [ ] Configuration binding improvements
- [ ] Environment-specific settings
- [ ] Hot reload support where applicable

**Estimated Duration:** 3-4 weeks  
**Status:** ⚪ Not Started

---

### 🟣 Phase 4: Documentation & Examples

**Goal:** Create comprehensive documentation and real-world examples

#### 4.1 Core Documentation
- [ ] Complete API documentation (XML comments)
- [ ] Generate API reference with DocFX
- [ ] Architecture diagrams (C4 model)
- [ ] Sequence diagrams for key flows
- [ ] Performance characteristics document

#### 4.2 Guides & Tutorials
- [ ] Getting started guide
- [ ] Migration guide from standard SignalR
- [ ] Authentication & authorization guide
- [ ] Streaming data guide
- [ ] Server-to-client RPC guide
- [ ] TypeScript client guide
- [ ] Advanced scenarios guide

#### 4.3 Sample Applications
- [ ] Basic chat application
- [ ] Real-time dashboard
- [ ] Collaborative editing demo
- [ ] Authenticated API example
- [ ] Streaming data example
- [ ] Microservices communication example

#### 4.4 Video Content (Optional)
- [ ] Introduction video
- [ ] Tutorial series
- [ ] Architecture overview

**Estimated Duration:** 2-3 weeks  
**Status:** ⚪ Not Started

---

### 🟠 Phase 5: Testing & Quality

**Goal:** Comprehensive test coverage and quality assurance

#### 5.1 Unit Tests
- [ ] Expand unit test coverage (target: >80%)
- [ ] Test all error paths
- [ ] Mock/stub improvements
- [ ] Parameterized tests for edge cases

#### 5.2 Integration Tests
- [ ] End-to-end tests with real server/client
- [ ] Authentication flow tests
- [ ] Streaming tests
- [ ] Reconnection tests
- [ ] Multi-client scenarios

#### 5.3 Performance Tests
- [ ] Load testing framework
- [ ] Benchmark common scenarios
- [ ] Memory profiling
- [ ] Connection scaling tests
- [ ] Latency measurements
- [ ] Performance regression tests

#### 5.4 Compatibility Tests
- [ ] Test .NET 8.0 compatibility
- [ ] Test .NET Framework 4.7.2 compatibility
- [ ] Browser compatibility (TypeScript client)
- [ ] SignalR protocol versions

**Estimated Duration:** 2 weeks  
**Status:** ⚪ Not Started

---

### 🔴 Phase 6: Deployment & Distribution

**Goal:** Make the library production-ready and publicly available

#### 6.1 Packaging
- [ ] NuGet package metadata improvements
- [ ] Package icons and README
- [ ] Release notes automation
- [ ] Symbol packages for debugging
- [ ] npm package improvements
- [ ] TypeScript type definitions

#### 6.2 Distribution
- [ ] Publish to nuget.org (public)
- [ ] Publish to npm registry (public)
- [ ] GitHub Releases
- [ ] GitHub Packages integration
- [ ] Create installation guides

#### 6.3 Containerization
- [ ] Create Dockerfile for test server
- [ ] Docker Compose for development
- [ ] Kubernetes manifests
- [ ] Helm chart
- [ ] Container security scanning

#### 6.4 Infrastructure
- [ ] Example Azure deployment
- [ ] Example AWS deployment
- [ ] Terraform/Bicep templates
- [ ] Infrastructure documentation

**Estimated Duration:** 1-2 weeks  
**Status:** ⚪ Not Started

---

### 🟢 Phase 7: Community & Maintenance

**Goal:** Build community, establish maintenance practices

#### 7.1 Community Building
- [ ] License selection (MIT already set)
- [ ] Code of Conduct
- [ ] Contribution guidelines (CONTRIBUTING.md)
- [ ] Issue templates
- [ ] PR templates
- [ ] GitHub Discussions setup
- [ ] FAQ document

#### 7.2 Maintenance Process
- [ ] Versioning strategy (SemVer)
- [ ] Release process documentation
- [ ] Change management process
- [ ] Breaking change policy
- [ ] Deprecation policy
- [ ] Support policy

#### 7.3 Outreach
- [ ] Blog post announcement
- [ ] Reddit/Twitter posts
- [ ] .NET community sharing
- [ ] Conference talk proposals (optional)
- [ ] Newsletter features

**Estimated Duration:** Ongoing  
**Status:** ⚪ Not Started

---

## Progress Tracking

### Overall Progress

| Phase | Status | Progress | Start Date | End Date | Notes |
|-------|--------|----------|------------|----------|-------|
| Phase 0: Foundation | 🟡 In Progress | 20% | 2025-10-16 | - | Roadmap created |
| Phase 1: Critical Updates | ⚪ Not Started | 0% | - | - | Pending Reflectensions |
| Phase 2: Code Modernization | ⚪ Not Started | 0% | - | - | - |
| Phase 3: Feature Enhancements | ⚪ Not Started | 0% | - | - | - |
| Phase 4: Documentation | ⚪ Not Started | 0% | - | - | - |
| Phase 5: Testing & Quality | ⚪ Not Started | 0% | - | - | - |
| Phase 6: Deployment | ⚪ Not Started | 0% | - | - | - |
| Phase 7: Community | ⚪ Not Started | 0% | - | - | - |

**Legend:**
- 🔴 Critical / High Priority
- 🟡 In Progress
- 🟢 Completed
- 🔵 Medium Priority
- 🟣 Low Priority
- ⚪ Not Started

---

## Dependencies & Considerations

### Critical Path Dependencies

```
1. doob.Reflectensions Update (BLOCKER)
   ↓
2. Update SignalARRR to use new Reflectensions
   ↓
3. Dependency Updates (Phase 1)
   ↓
4. Code Modernization (Phase 2)
   ↓
5. Feature Enhancements (Phase 3)
   ↓
6. Testing (Phase 5) + Documentation (Phase 4) - Parallel
   ↓
7. Deployment (Phase 6)
   ↓
8. Community (Phase 7) - Ongoing
```

### Resource Requirements

- **Developer Time:** 12-16 weeks (full-time equivalent)
- **Infrastructure:** 
  - GitHub Actions minutes
  - NuGet.org account (free)
  - npm registry account (free)
  - Azure/AWS for examples (optional, can use free tier)

### Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking changes in Reflectensions | High | Medium | Comprehensive testing, version pinning |
| Community adoption low | Medium | Medium | Good documentation, marketing outreach |
| Performance regressions | High | Low | Benchmark suite, performance gates |
| Security vulnerabilities | High | Low | Regular audits, Dependabot, CodeQL |
| Maintenance burden | Medium | Medium | Good architecture, community contributions |

---

## Next Steps & Decision Points

### Immediate Actions (This Week)

1. ✅ **COMPLETED:** Create this modernization roadmap
2. 🔴 **DECISION NEEDED:** Choose doob.Reflectensions update strategy
   - Recommended: Option A (Update Reflectensions first)
   - Needs confirmation and timeline
3. ⚪ **TODO:** Create ARCHITECTURE.md document
4. ⚪ **TODO:** Update README.md with current state disclaimer

### Decision Log

| Date | Decision | Rationale | Decided By |
|------|----------|-----------|------------|
| 2025-10-16 | Create modernization roadmap | Needed clear plan before starting work | Team |
| - | TBD: Reflectensions strategy | Pending evaluation | Pending |
| - | TBD: Serialization approach | Pending research | Pending |
| - | TBD: Proxy generation alternative | Pending POC | Pending |

---

## Notes & Questions

### Open Questions

1. **Q:** Should we maintain .NET Framework 4.7.2 support?
   - **A:** TBD - Check user base, Microsoft support timeline (ends 2027)

2. **Q:** Should System.Text.Json completely replace Newtonsoft.Json or coexist?
   - **A:** TBD - Consider: breaking change, user preferences, performance

3. **Q:** What's the long-term vision? Who is the target audience?
   - **A:** TBD - Enterprise apps? Open source community? Both?

4. **Q:** Should we consider renaming the project for clarity?
   - **A:** TBD - "HARRR" is fun but might be confusing

5. **Q:** What's the policy on breaking changes?
   - **A:** TBD - Major version bump? Deprecation period?

### Resources & Links

- **Current Repository:** https://github.com/doob-at/SignalARRR
- **NuGet Packages:** https://f.feedz.io/doob/dev/nuget/index.json (private)
- **SignalR Documentation:** https://learn.microsoft.com/en-us/aspnet/core/signalr/
- **Related Projects:**
  - doob.Reflectensions (internal)
  - Microsoft.AspNetCore.SignalR
  - ImpromptuInterface

---

## Appendix

### Glossary

- **HARRR:** Hub base class in SignalARRR (playful name, "pirate" theme)
- **ServerMethods:** Base class for organizing hub methods into separate classes
- **ClientContext:** Enhanced context object with client information, authentication state
- **Proxy Generator:** Component that creates type-safe proxies from interfaces
- **Bidirectional RPC:** Server can call client methods and receive responses

### Code Statistics (Current)

- **Total C# Files:** ~84 files
- **Test Files:** ~46 files
- **Lines of Code:** ~TBD (to be measured)
- **Test Coverage:** ~TBD (to be measured)

### Version History

- **Current:** 2.1.2 (based on package.json)
- **Target Framework:** .NET 8.0
- **SignalR Version:** 8.0.0

---

**Document Version:** 1.0  
**Last Updated:** 2025-10-16  
**Next Review:** After Phase 0 completion
