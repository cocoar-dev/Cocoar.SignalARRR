namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {

    /// <summary>
    /// The <c>__type</c> values that mark an argument as a remote reference rather than data.
    /// </summary>
    /// <remarks>
    /// Some arguments are not values but handles: a cancellation token the sender can trip later, a
    /// stream the receiver has to fetch. The receiver has to recognise them to swap them back, and
    /// it used to do that by guessing from the shape — an object with a string <c>Id</c> was taken
    /// for a cancellation token, one with a string <c>Uri</c> for a stream.
    /// <para>
    /// Guessing is wrong on ordinary data that happens to look the same. An application type with a
    /// single <c>Id</c> string was silently replaced by a cancellation token, and the real argument
    /// never reached the handler. The .NET clients were shielded by knowing the parameter types; the
    /// TypeScript and Swift ones, which have none, were not.
    /// </para>
    /// <para>
    /// Marking the reference removes the guess. Receivers still accept an unmarked reference, so a
    /// newer client keeps working against a server that does not send the marker yet.
    /// </para>
    /// </remarks>
    public static class RemoteReferenceKinds {

        /// <summary>The property every remote reference carries.</summary>
        public const string PropertyName = "__type";

        /// <summary>A cancellation token the sender can trip by id.</summary>
        public const string CancellationToken = "cancellationToken";

        /// <summary>A stream the receiver fetches over HTTP.</summary>
        public const string Stream = "stream";
    }
}
