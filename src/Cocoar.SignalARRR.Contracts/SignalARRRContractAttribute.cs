using System;

namespace Cocoar.SignalARRR.Contracts;

/// <summary>
/// Marks an interface for compile-time proxy generation by the SignalARRR source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class SignalARRRContractAttribute : Attribute;
