using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    /// <summary>
    /// Describes how a receiving side maps the arguments of an incoming message onto the
    /// parameters of a registered method: which parameters occupy an argument slot at all, and
    /// whether trailing parameters with default values may be omitted by the caller.
    /// </summary>
    /// <remarks>
    /// The wire format carries no parameter types, only an argument array — so overloads can only
    /// be told apart by how many arguments a message carries. Which declared parameters actually
    /// correspond to an argument slot is direction-dependent: a <c>CancellationToken</c> occupies a
    /// slot going out to a client (the reference in the slot is what tells a TypeScript or Swift
    /// client which argument is the token) but not coming in to the server (the server binds its
    /// own token), and <c>[FromServices]</c> parameters are filled from the server's container.
    /// Each receiving side owns one policy instance describing its rules; the registries use it to
    /// index methods by argument count, and it must mirror exactly what that side's parameter
    /// binder skips.
    /// </remarks>
    public sealed class WireSlotPolicy {

        /// <summary>
        /// Every declared parameter occupies a slot and none may be omitted. This is the rule for
        /// methods invoked on a .NET client: the server builds the argument array from the full
        /// interface declaration, so the received count always equals the parameter count.
        /// </summary>
        public static WireSlotPolicy AllParameters { get; } =
            new WireSlotPolicy(_ => false, allowOmittedTrailingDefaults: false);

        private readonly Func<ParameterInfo, bool> _isNonSlotParameter;
        private readonly bool _allowOmittedTrailingDefaults;

        public WireSlotPolicy(Func<ParameterInfo, bool> isNonSlotParameter, bool allowOmittedTrailingDefaults) {
            _isNonSlotParameter = isNonSlotParameter ?? throw new ArgumentNullException(nameof(isNonSlotParameter));
            _allowOmittedTrailingDefaults = allowOmittedTrailingDefaults;
        }

        /// <summary>
        /// The argument counts under which <paramref name="method"/> must be reachable — a
        /// contiguous range from "all omittable trailing defaults omitted" to "every slot filled".
        /// </summary>
        public IReadOnlyList<int> GetAcceptedArgumentCounts(MethodInfo method) {
            var slotCount = 0;
            var requiredCount = 0;

            foreach (var parameter in method.GetParameters()) {
                if (_isNonSlotParameter(parameter)) {
                    continue;
                }

                slotCount++;
                if (!(_allowOmittedTrailingDefaults && parameter.HasDefaultValue)) {
                    requiredCount = slotCount;
                }
            }

            var counts = new List<int>(slotCount - requiredCount + 1);
            for (var count = requiredCount; count <= slotCount; count++) {
                counts.Add(count);
            }

            return counts;
        }
    }
}
