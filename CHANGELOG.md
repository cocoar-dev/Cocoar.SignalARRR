# Changelog

All notable changes to SignalARRR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **BREAKING**: Updated target frameworks for modernization
  - Server: Now targets .NET 8.0 only (removed .NET Framework 4.7.2 support)
  - Client: Now targets .NET 8.0 and .NET Standard 2.0 (removed .NET Framework 4.7.2)
  - Common: Now targets .NET 8.0 and .NET Standard 2.0 (removed .NET Framework 4.7.2)
  - ProxyGenerator: Now targets .NET 8.0 and .NET Standard 2.0 (removed .NET Framework 4.7.2)
- Updated repository URLs from `doob-at` to `windischb` GitHub organization
- Removed legacy `TestClient_FullFramework` project
- Updated test projects to .NET 8.0

### Migration Guide

**For Server Applications:**
- Upgrade your ASP.NET Core application to .NET 8.0 or later
- Update `<TargetFramework>` in your `.csproj` to `net8.0`

**For Client Applications:**
- .NET Framework users: Migrate to .NET 8.0+ or use .NET Standard 2.0 compatible runtime
- Existing .NET Core/5+/6+/7+ applications: Update to .NET 8.0+ (recommended)
- Libraries: Can target .NET Standard 2.0 for broad compatibility

**Why .NET Standard 2.0 for Client?**
The client libraries target .NET Standard 2.0 to ensure maximum compatibility:
- ✅ Works with .NET 8.0+
- ✅ Works with .NET Framework 4.6.1+
- ✅ Works with Xamarin, Unity, and other .NET Standard 2.0 compatible platforms

---

## [2.1.2] - Previous Release

### Features
- Split hub methods across multiple classes via `ServerMethods<T>`
- Method-level authorization with `[Authorize]` attribute
- Continuous token validation with automatic challenge/refresh
- Server-to-client RPC with response awaiting
- Support for `IObservable<T>`, `IAsyncEnumerable<T>`, and `ChannelReader<T>` streaming
- Type-safe client proxies from interfaces
- Multi-platform support (Server, .NET Client, TypeScript Client)
