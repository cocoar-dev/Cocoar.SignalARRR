using System;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct ContractInterfaceInfo : IEquatable<ContractInterfaceInfo>
{
    public string Namespace { get; }
    public string InterfaceName { get; }
    public string FullName { get; }
    public string ProxyClassName { get; }
    public EquatableArray<ContractMethodInfo> Methods { get; }

    public ContractInterfaceInfo(
        string @namespace,
        string interfaceName,
        string fullName,
        string proxyClassName,
        EquatableArray<ContractMethodInfo> methods)
    {
        Namespace = @namespace;
        InterfaceName = interfaceName;
        FullName = fullName;
        ProxyClassName = proxyClassName;
        Methods = methods;
    }

    public bool Equals(ContractInterfaceInfo other) =>
        Namespace == other.Namespace &&
        InterfaceName == other.InterfaceName &&
        FullName == other.FullName &&
        ProxyClassName == other.ProxyClassName &&
        Methods.Equals(other.Methods);

    public override bool Equals(object? obj) => obj is ContractInterfaceInfo other && Equals(other);

    public override int GetHashCode()
    {
        var hash = HashCombine.Of(Namespace);
        hash = HashCombine.Combine(hash, HashCombine.Of(InterfaceName));
        hash = HashCombine.Combine(hash, HashCombine.Of(FullName));
        hash = HashCombine.Combine(hash, HashCombine.Of(ProxyClassName));
        hash = HashCombine.Combine(hash, Methods.GetHashCode());
        return hash;
    }
}
