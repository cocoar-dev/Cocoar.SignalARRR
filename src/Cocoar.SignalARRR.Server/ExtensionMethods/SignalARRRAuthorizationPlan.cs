using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {

    /// <summary>
    /// Who may call a server method: <c>[AllowAnonymous]</c> anywhere on the method, its contract,
    /// its class chain or its hub, or else the <c>[Authorize]</c> data of the most specific of those
    /// levels that carries any. The source generator resolves it at compile time for every method.
    /// </summary>
    public sealed class SignalARRRAuthorizationPlan {

        public SignalARRRAuthorizationPlan(bool allowAnonymous, IReadOnlyList<IAuthorizeData> authorizeData) {
            AllowAnonymous = allowAnonymous;
            AuthorizeData = authorizeData;
        }

        /// <summary>Gets a value indicating whether <c>[AllowAnonymous]</c> applies.</summary>
        public bool AllowAnonymous { get; }

        /// <summary>Gets the authorization data that applies, or an empty list if the method is unrestricted.</summary>
        public IReadOnlyList<IAuthorizeData> AuthorizeData { get; }

        /// <summary>Gets a value indicating whether authorization has to be evaluated at all.</summary>
        public bool RequiresAuthorization => !AllowAnonymous && AuthorizeData.Count > 0;
    }
}
